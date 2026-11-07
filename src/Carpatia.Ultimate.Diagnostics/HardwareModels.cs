namespace Carpatia.Ultimate.Diagnostics;

public interface IHardwareDiagnostics
{
    Task<HardwareSnapshot> ReadHardwareAsync(CancellationToken cancellationToken = default);
    Task<UtilizationSnapshot> SampleUtilizationAsync(CancellationToken cancellationToken = default);
}

public sealed record GraphicsAdapterInfo(string Name, string? DriverVersion, string? DeviceId);

public sealed record SystemDriveInfo(string Name, long TotalBytes, long FreeBytes)
{
    public double UsedPercent => TotalBytes > 0 ? 100d * (TotalBytes - FreeBytes) / TotalBytes : 0;
}

public enum DiagnosticCapabilityState { Available, Unsupported, InDevelopment }

public sealed record DiagnosticCapability(
    string Id, string Name, DiagnosticCapabilityState State, string Explanation);

public sealed record HardwareSnapshot(
    bool IsSupported,
    string OperatingSystem,
    string? CpuName,
    int LogicalProcessorCount,
    int? PhysicalCoreCount,
    IReadOnlyList<GraphicsAdapterInfo> GraphicsAdapters,
    ulong? TotalMemoryBytes,
    SystemDriveInfo? SystemDrive,
    IReadOnlyList<string> Notices,
    DateTimeOffset CapturedAt)
{
    public static IReadOnlyList<DiagnosticCapability> Capabilities { get; } = Array.AsReadOnly(new[]
    {
        new DiagnosticCapability("cpu-utilization", "CPU utilization", DiagnosticCapabilityState.Available,
            "Sampled on demand using Windows GetSystemTimes. This is aggregate utilization, not frequency or performance."),
        new DiagnosticCapability("memory-utilization", "Physical memory", DiagnosticCapabilityState.Available,
            "Physical memory availability from GlobalMemoryStatusEx; cached memory remains available to Windows."),
        new DiagnosticCapability("gpu-telemetry", "GPU telemetry", DiagnosticCapabilityState.InDevelopment,
            "No GPU utilization, clock, temperature or power provider is integrated. Values remain unavailable."),
        new DiagnosticCapability("cpu-hardware-tuning", "CPU voltage / power / clock tuning", DiagnosticCapabilityState.Unsupported,
            "No validated vendor write interface is integrated. Firmware locks and security controls are respected."),
        new DiagnosticCapability("gpu-hardware-tuning", "GPU voltage / power / clock tuning", DiagnosticCapabilityState.Unsupported,
            "Device detection does not establish permission or support for modifying clocks or voltage.")
    });
}

public sealed record UtilizationSnapshot(
    double? CpuPercent,
    double? MemoryPercent,
    double? GpuPercent,
    ulong? UsedMemoryBytes,
    ulong? TotalMemoryBytes,
    SystemDriveInfo? SystemDrive,
    IReadOnlyList<string> Notices,
    DateTimeOffset CapturedAt);
