using Carpatia.Ultimate.Diagnostics;
using Xunit;

namespace Carpatia.Ultimate.Diagnostics.Tests;

public sealed class WindowsFactAttribute : FactAttribute
{
    public WindowsFactAttribute()
    {
        if (!OperatingSystem.IsWindows())
            Skip = "Requires a Windows host; Linux cannot validate Win32/WMI hardware detection.";
    }
}

public sealed class WindowsDiagnosticsTests
{
    [WindowsFact]
    public async Task ReadsHardwareWithoutElevationOrSystemChanges()
    {
        var snapshot = await new WindowsHardwareDiagnostics().ReadHardwareAsync();
        Assert.True(snapshot.IsSupported);
        Assert.False(string.IsNullOrWhiteSpace(snapshot.OperatingSystem));
        Assert.True(snapshot.LogicalProcessorCount > 0);
        Assert.True(snapshot.TotalMemoryBytes > 0);
        Assert.NotNull(snapshot.SystemDrive);
        Assert.True(snapshot.SystemDrive.TotalBytes > 0);
        Assert.InRange(snapshot.SystemDrive.FreeBytes, 0, snapshot.SystemDrive.TotalBytes);
    }

    [WindowsFact]
    public async Task SamplesRealMemoryAndOptionalCpuWithoutInventingGpuTelemetry()
    {
        var sample = await new WindowsHardwareDiagnostics().SampleUtilizationAsync();
        Assert.NotNull(sample.MemoryPercent);
        Assert.InRange(sample.MemoryPercent.Value, 0, 100);
        if (sample.CpuPercent is { } cpu) Assert.InRange(cpu, 0, 100);
        Assert.True(sample.TotalMemoryBytes > 0);
        Assert.Null(sample.GpuPercent);
    }
}
