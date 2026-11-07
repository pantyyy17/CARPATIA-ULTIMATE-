using System.Management;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security;
using Microsoft.Win32;

namespace Carpatia.Ultimate.Diagnostics;

/// <summary>Read-only Windows diagnostics. No elevation, network access or persistent polling.</summary>
/// <remarks>WMI runs on a worker thread. Cancellation is observed between queries and rows;
/// it cannot abort a Windows WMI provider that has already blocked inside a native call.</remarks>
public sealed class WindowsHardwareDiagnostics : IHardwareDiagnostics
{
    private static readonly TimeSpan SampleInterval = TimeSpan.FromMilliseconds(300);

    public Task<HardwareSnapshot> ReadHardwareAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.Run(() => OperatingSystem.IsWindows()
            ? ReadWindowsHardware(cancellationToken)
            : new HardwareSnapshot(false, RuntimeInformation.OSDescription, null,
                Environment.ProcessorCount, null, [], null, null,
                ["Windows hardware APIs are unavailable on this operating system."], DateTimeOffset.UtcNow), cancellationToken);
    }

    public async Task<UtilizationSnapshot> SampleUtilizationAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows())
            return new(null, null, null, null, null, null,
                ["Windows utilization APIs are unavailable on this operating system."], DateTimeOffset.UtcNow);

        // Each request owns its pair of counters, so concurrent consumers cannot contaminate a sample.
        var before = NativeMethods.ReadCpuTimes();
        await Task.Delay(SampleInterval, cancellationToken).ConfigureAwait(false);
        var after = NativeMethods.ReadCpuTimes();
        var memory = NativeMethods.ReadMemory();
        var notices = new List<string>();
        double? cpu = before is { } start && after is { } end
            ? UtilizationMath.CpuPercent(start, end) : null;
        // GetSystemTimes is scoped to the caller's processor group on large machines.
        // Do not present one group's counters as a machine-wide utilization percentage.
        if (NativeMethods.LogicalProcessorCount > 64)
        {
            cpu = null;
            notices.Add("Aggregate CPU utilization is unavailable on systems spanning multiple processor groups.");
        }
        if (cpu is null) notices.Add("Windows did not provide a valid CPU counter sample.");
        if (memory is null) notices.Add("Physical memory information is unavailable.");
        notices.Add("GPU utilization is unavailable: no validated telemetry provider is integrated.");

        return new(cpu,
            memory is { } m ? UtilizationMath.MemoryPercent(m.Total, m.Available) : null,
            null, memory is { } used ? used.Total - used.Available : null, memory?.Total,
            ReadSystemDrive(notices), notices.AsReadOnly(), DateTimeOffset.UtcNow);
    }

    [SupportedOSPlatform("windows")]
    private static HardwareSnapshot ReadWindowsHardware(CancellationToken cancellationToken)
    {
        var notices = new List<string>();
        string? cpuName = null;
        int? physicalCores = null;
        var graphics = new List<GraphicsAdapterInfo>();
        var os = RuntimeInformation.OSDescription;

        ReadWmi("SELECT Name, NumberOfCores FROM Win32_Processor", notices, item =>
        {
            cpuName ??= Convert.ToString(item["Name"])?.Trim();
            if (item["NumberOfCores"] is { } count)
                physicalCores = (physicalCores ?? 0) + Convert.ToInt32(count);
        }, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(cpuName))
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
                cpuName = (key?.GetValue("ProcessorNameString") as string)?.Trim();
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException or IOException)
            {
                notices.Add($"CPU registry information could not be read: {ex.Message}");
            }
        }

        ReadWmi("SELECT Name, DriverVersion, PNPDeviceID FROM Win32_VideoController", notices, item =>
        {
            var name = Convert.ToString(item["Name"]);
            if (!string.IsNullOrWhiteSpace(name))
                graphics.Add(new(name.Trim(), Convert.ToString(item["DriverVersion"]), Convert.ToString(item["PNPDeviceID"])));
        }, cancellationToken);
        ReadWmi("SELECT Caption, Version, BuildNumber FROM Win32_OperatingSystem", notices, item =>
        {
            var caption = Convert.ToString(item["Caption"]);
            if (!string.IsNullOrWhiteSpace(caption))
                os = $"{caption.Trim()} · Build {item["BuildNumber"]} ({item["Version"]})";
        }, cancellationToken);

        var memory = NativeMethods.ReadMemory();
        if (memory is null) notices.Add("Physical memory information is unavailable.");
        if (graphics.Count == 0) notices.Add("Windows did not report a graphics adapter.");
        if (cpuName is null) notices.Add("Windows did not report a processor model.");
        return new(true, os, cpuName, NativeMethods.LogicalProcessorCount, physicalCores,
            graphics.AsReadOnly(), memory?.Total, ReadSystemDrive(notices),
            notices.AsReadOnly(), DateTimeOffset.UtcNow);
    }

    [SupportedOSPlatform("windows")]
    private static void ReadWmi(string query, List<string> notices, Action<ManagementBaseObject> consume,
        CancellationToken cancellationToken)
    {
        try
        {
            var options = new System.Management.EnumerationOptions
            {
                ReturnImmediately = false,
                Timeout = TimeSpan.FromSeconds(4),
                Rewindable = false
            };
            using var searcher = new ManagementObjectSearcher(new ManagementScope(@"\\.\root\cimv2"),
                new ObjectQuery(query), options);
            using var results = searcher.Get();
            foreach (ManagementBaseObject item in results)
            {
                using (item)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    consume(item);
                }
            }
        }
        catch (Exception ex) when (ex is ManagementException or COMException or UnauthorizedAccessException or SecurityException)
        {
            notices.Add($"A Windows hardware query was unavailable: {ex.Message}");
        }
    }

    private static SystemDriveInfo? ReadSystemDrive(List<string> notices)
    {
        try
        {
            var root = Path.GetPathRoot(Environment.SystemDirectory);
            if (string.IsNullOrWhiteSpace(root))
            {
                notices.Add("The Windows system drive could not be identified.");
                return null;
            }
            var drive = new DriveInfo(root);
            if (!drive.IsReady)
            {
                notices.Add("The Windows system drive is not ready.");
                return null;
            }
            return new(drive.Name, drive.TotalSize, drive.TotalFreeSpace);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            notices.Add($"The Windows system drive could not be read: {ex.Message}");
            return null;
        }
    }
}
