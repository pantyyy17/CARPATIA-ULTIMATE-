using Carpatia.Ultimate.Diagnostics;
using Xunit;

namespace Carpatia.Ultimate.Diagnostics.Tests;

public sealed class DiagnosticsTests
{
    [Fact]
    public void CpuUsesKernelPlusUserAndSubtractsIdle()
    {
        // Deltas: kernel 300 (includes idle 100) + user 100 => busy 300 / total 400.
        Assert.Equal(75d, UtilizationMath.CpuPercent(new(100, 200, 300), new(200, 500, 400)));
    }

    [Fact]
    public void CpuFullyIdleIsZeroRatherThanNegative()
    {
        Assert.Equal(0d, UtilizationMath.CpuPercent(new(0, 0, 0), new(100, 100, 0)));
    }

    [Fact]
    public void CpuFullyBusyIsOneHundred()
    {
        Assert.Equal(100d, UtilizationMath.CpuPercent(new(0, 0, 0), new(0, 100, 100)));
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(100, 50, 0)]
    [InlineData(101, 100, 0)]
    public void InvalidCpuCountersRemainUnavailable(ulong idle, ulong kernel, ulong user)
    {
        Assert.Null(UtilizationMath.CpuPercent(new(0, 0, 0), new(idle, kernel, user)));
    }

    [Fact]
    public void ResetCpuCountersRemainUnavailable()
    {
        Assert.Null(UtilizationMath.CpuPercent(new(100, 100, 100), new(90, 100, 150)));
        Assert.Null(UtilizationMath.CpuPercent(new(100, 100, 100), new(100, 90, 150)));
        Assert.Null(UtilizationMath.CpuPercent(new(100, 100, 100), new(100, 150, 90)));
    }

    [Theory]
    [InlineData(100, 25, 75)]
    [InlineData(100, 100, 0)]
    [InlineData(100, 0, 100)]
    public void MemoryUsesActualAvailableBytes(ulong total, ulong available, double expected)
    {
        Assert.Equal(expected, UtilizationMath.MemoryPercent(total, available));
    }

    [Fact]
    public void InvalidMemoryIsUnavailable()
    {
        Assert.Null(UtilizationMath.MemoryPercent(0, 0));
        Assert.Null(UtilizationMath.MemoryPercent(100, 101));
    }

    [Fact]
    public async Task NonWindowsNeverReturnsInventedWindowsHardwareOrMetrics()
    {
        if (OperatingSystem.IsWindows()) return;
        IHardwareDiagnostics diagnostics = new WindowsHardwareDiagnostics();
        var hardware = await diagnostics.ReadHardwareAsync();
        var metrics = await diagnostics.SampleUtilizationAsync();
        Assert.False(hardware.IsSupported);
        Assert.Null(hardware.CpuName);
        Assert.Empty(hardware.GraphicsAdapters);
        Assert.Null(hardware.SystemDrive);
        Assert.Null(metrics.CpuPercent);
        Assert.Null(metrics.MemoryPercent);
        Assert.Null(metrics.GpuPercent);
        Assert.NotEmpty(hardware.Notices);
        Assert.NotEmpty(metrics.Notices);
    }

    [Fact]
    public async Task CancelledRequestsDoNotBeginDetection()
    {
        IHardwareDiagnostics diagnostics = new WindowsHardwareDiagnostics();
        var token = new CancellationToken(true);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => diagnostics.ReadHardwareAsync(token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => diagnostics.SampleUtilizationAsync(token));
    }

    [Fact]
    public void HardwareWritesAreExplicitlyUnsupported()
    {
        var tuning = HardwareSnapshot.Capabilities.Where(c => c.Id.EndsWith("hardware-tuning", StringComparison.Ordinal)).ToArray();
        Assert.Equal(2, tuning.Length);
        Assert.All(tuning, c => Assert.Equal(DiagnosticCapabilityState.Unsupported, c.State));
    }
}
