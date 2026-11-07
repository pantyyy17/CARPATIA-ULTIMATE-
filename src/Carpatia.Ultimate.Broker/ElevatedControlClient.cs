using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using Carpatia.Ultimate.Core;
using Microsoft.Win32.SafeHandles;

namespace Carpatia.Ultimate.Broker;

/// <summary>
/// Launches one fixed helper for one reviewed mutation. It never kills a helper
/// after a request may have reached it: interruption must not skip recovery.
/// </summary>
public sealed class ElevatedControlClient
{
    public async Task<ControlResponse> ExecuteAsync(ControlRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
            return new(OperationStatus.Unsupported, "Windows elevation is unavailable on this operating system.");
        if (request.Action is not (ControlAction.Apply or ControlAction.Restore))
            throw new ArgumentException("Only reviewed Apply and Restore operations use the elevated helper.", nameof(request));
        if (request.ReviewedPreview is null)
            throw new ArgumentException("A reviewed preview is required before requesting elevation.", nameof(request));
        // Validate size/serialization before launching a privileged process.
        _ = ControlWire.Serialize(request);
        cancellationToken.ThrowIfCancellationRequested();
        // ShellExecute with runas can wait for a UAC prompt. Keep that off the UI thread.
        return await Task.Run(() => OperatingSystem.IsWindows()
            ? ExecuteWindowsAsync(request, cancellationToken)
            : Task.FromResult(new ControlResponse(OperationStatus.Unsupported, "Windows elevation is unavailable.")), cancellationToken);
    }

    [SupportedOSPlatform("windows")]
    private static async Task<ControlResponse> ExecuteWindowsAsync(ControlRequest request, CancellationToken cancellationToken)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Helper", "Carpatia.Ultimate.Helper.exe");
        if (!File.Exists(path))
            return new(OperationStatus.Unsupported, "The one-shot helper is not installed. Build or publish the helper beside the app before enabling changes.");
        var pipeName = HelperPipeName.Create();
        using var identity = WindowsIdentity.GetCurrent();
        var userSid = identity.User ?? throw new UnauthorizedAccessException("The Windows user identity is unavailable.");
        var security = new PipeSecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.SetOwner(userSid);
        security.AddAccessRule(new PipeAccessRule(userSid, PipeAccessRights.ReadWrite, AccessControlType.Allow));
        // CurrentUserOnly is deliberately not used: .NET checks elevation level
        // as well as SID, which would reject our intentionally elevated helper.
        await using var pipe = NamedPipeServerStreamAcl.Create(pipeName, PipeDirection.InOut, 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.FirstPipeInstance,
            4096, 4096, security);
        Process? helper = null;
        var requestMayHaveBeenSent = false;
        try
        {
            // No caller-provided executable, path, operation argument or script.
            helper = Process.Start(new ProcessStartInfo
            {
                FileName = path,
                Arguments = "--pipe " + pipeName,
                WorkingDirectory = Path.GetDirectoryName(path)!,
                UseShellExecute = true,
                Verb = "runas"
            });
            if (helper is null) return new(OperationStatus.Failed, "Windows did not start the helper. Nothing was applied.");

            using var connectionTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            using var connectionCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                connectionTimeout.Token, cancellationToken);
            await pipe.WaitForConnectionAsync(connectionCancellation.Token);
            if (!GetNamedPipeClientProcessId(pipe.SafePipeHandle, out var connectedProcess)
                || connectedProcess != (uint)helper.Id)
                throw new InvalidDataException("The pipe client is not the helper process that Windows launched.");
            cancellationToken.ThrowIfCancellationRequested();
            // An interrupted write may still have delivered a complete message.
            requestMayHaveBeenSent = true;
            using var responseTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(10));
            await ControlWire.WriteAsync(pipe, request, responseTimeout.Token);
            return await ControlWire.ReadAsync<ControlResponse>(pipe, responseTimeout.Token);
        }
        catch (Win32Exception error) when (error.NativeErrorCode == 1223)
        {
            return new(OperationStatus.Failed, "Administrator permission was cancelled. Nothing was applied.", Cancelled: true);
        }
        catch (OperationCanceledException) when (!requestMayHaveBeenSent)
        {
            return new(OperationStatus.Failed, "The elevation request was cancelled or timed out before a change was sent.", Cancelled: true);
        }
        catch (Exception error) when (error is IOException or Win32Exception or InvalidOperationException or OperationCanceledException)
        {
            return requestMayHaveBeenSent
                ? new(OperationStatus.RecoveryRequired, "The helper result could not be read. Inspect the recovery journal before making another change. " + error.Message)
                : new(OperationStatus.Failed, "The helper could not be connected. Nothing was applied. " + error.Message);
        }
        finally
        {
            // Dispose closes our process handle; it does not terminate execution.
            helper?.Dispose();
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeClientProcessId(SafePipeHandle pipe, out uint clientProcessId);
}
