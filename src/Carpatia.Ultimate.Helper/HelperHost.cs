using System.IO.Pipes;
using System.Security.Principal;
using Carpatia.Ultimate.Broker;
using Carpatia.Ultimate.Core;

namespace Carpatia.Ultimate.Helper;

public static class HelperHost
{
    public static async Task<int> RunAsync(string[] args, IControlDispatcher dispatcher)
    {
        if (!OperatingSystem.IsWindows() || args.Length != 2 || args[0] != "--pipe"
            || !HelperPipeName.IsValid(args[1])) return 2;
        using var identity = WindowsIdentity.GetCurrent();
        if (!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator)) return 3;

        await using var pipe = new NamedPipeClientStream(".", args[1], PipeDirection.InOut,
            PipeOptions.Asynchronous);
        try
        {
            using var connectTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await pipe.ConnectAsync(connectTimeout.Token);
            if (!Equals(pipe.GetAccessControl().GetOwner(typeof(SecurityIdentifier)), identity.User)) return 3;
            var request = await ControlWire.ReadAsync<ControlRequest>(pipe, connectTimeout.Token);
            if (request.Action is not (ControlAction.Apply or ControlAction.Restore)
                || request.ReviewedPreview is null)
            {
                await ControlWire.WriteAsync(pipe, new ControlResponse(OperationStatus.Failed,
                    "Only a reviewed Apply or Restore request is accepted by the helper."));
                return 4;
            }
            ControlResponse response;
            try
            {
                // The dispatcher validates the allowlist and exact fresh state before
                // calling confirmation. No cancellation token can skip post-write verification.
                response = await dispatcher.ExecuteAsync(request, NativeConfirmation.ConfirmAsync, CancellationToken.None);
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                response = new ControlResponse(OperationStatus.RecoveryRequired,
                    "The helper could not complete the operation. Inspect the recovery journal. " + error.Message);
            }
            using var sendTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await ControlWire.WriteAsync(pipe, response, sendTimeout.Token);
            return 0;
        }
        catch (Exception error) when (error is IOException or OperationCanceledException or UnauthorizedAccessException)
        {
            // The caller knows that loss of a result after dispatch requires recovery.
            return 5;
        }
    }
}
