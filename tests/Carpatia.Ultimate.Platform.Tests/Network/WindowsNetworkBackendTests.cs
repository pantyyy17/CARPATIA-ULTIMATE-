using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Carpatia.Ultimate.Platform.Windows.Network;
using Xunit;

namespace Carpatia.Ultimate.Platform.Tests.Network;

public sealed class WindowsNetworkBackendTests
{
    [Fact]
    public void NativeDnsV3MatchesOfficialX64SdkAbi()
    {
        if (IntPtr.Size != 8) return; // The distributed application and Windows CI target x64.
        var type = typeof(WindowsNetworkBackend).GetNestedType("NativeDnsSettings", BindingFlags.NonPublic)!;
        Assert.Equal(112, Marshal.SizeOf(type));
        foreach (var (field, offset) in new (string, int)[]
        {
            ("Version", 0), ("Flags", 8), ("Domain", 16), ("NameServer", 24), ("SearchList", 32),
            ("RegistrationEnabled", 40), ("RegisterAdapterName", 44), ("EnableLlmnr", 48),
            ("QueryAdapterName", 52), ("ProfileNameServer", 56), ("DisableUnconstrainedQueries", 64),
            ("SupplementalSearchList", 72), ("ServerPropertyCount", 80), ("ServerProperties", 88),
            ("ProfileServerPropertyCount", 96), ("ProfileServerProperties", 104)
        }) Assert.Equal(offset, Marshal.OffsetOf(type, field).ToInt32());
    }

    [NonWindowsFact]
    public async Task RealBackendFailsClosedOnNonWindowsWithoutStartingAProcess()
    {
        var backend = new WindowsNetworkBackend();
        var id = Guid.NewGuid();
        await Assert.ThrowsAsync<PlatformNotSupportedException>(async () => await backend.ListAdaptersAsync());
        await Assert.ThrowsAsync<PlatformNotSupportedException>(async () => await backend.ReadDnsAsync(id, IpFamily.IPv4));
        await Assert.ThrowsAsync<PlatformNotSupportedException>(async () => await backend.ReadMtuAsync(id, IpFamily.IPv6));
        await Assert.ThrowsAsync<PlatformNotSupportedException>(async () => await backend.ReadTcpAutoTuningAsync(TcpTemplate.Internet));
        await Assert.ThrowsAsync<PlatformNotSupportedException>(async () => await backend.ReadRssAsync(id));
        await Assert.ThrowsAsync<PlatformNotSupportedException>(async () => await backend.WriteDnsAsync(new(id, IpFamily.IPv4, DnsSource.Automatic, [])));
        await Assert.ThrowsAsync<PlatformNotSupportedException>(async () => await backend.WriteMtuAsync(new(id, IpFamily.IPv4, 1500, 1500, 1500)));
        await Assert.ThrowsAsync<PlatformNotSupportedException>(async () => await backend.WriteTcpAutoTuningAsync(TcpTemplate.Internet, TcpAutoTuningLevel.Normal));
        await Assert.ThrowsAsync<PlatformNotSupportedException>(async () => await backend.WriteRssEnabledAsync(id, true));
    }

    [SupportedWindowsFact]
    public async Task UnvalidatedIpv6DnsConfigurationIsExplicitlyUnavailable()
    {
        var backend = new WindowsNetworkBackend();
        await Assert.ThrowsAsync<NotSupportedException>(async () =>
            await backend.ReadDnsAsync(Guid.NewGuid(), IpFamily.IPv6));
    }

    [WindowsFact]
    public async Task EmbeddedScriptParsesInWindowsPowerShellWithoutExecutingItsOperations()
    {
        var type = typeof(WindowsNetworkBackend).Assembly.GetType("Carpatia.Ultimate.Platform.Windows.Network.PowerShellNetworkRunner")!;
        var script = (string)type.GetField("Script", BindingFlags.NonPublic | BindingFlags.Static)!.GetRawConstantValue()!;
        var encodedScript = Convert.ToBase64String(Encoding.UTF8.GetBytes(script));
        var parser = "$tokens=$null;$errors=$null;[void][System.Management.Automation.Language.Parser]::ParseInput("
            + "[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('" + encodedScript
            + "')),[ref]$tokens,[ref]$errors);if($errors.Count -gt 0){$errors|ForEach-Object{[Console]::Error.WriteLine($_.Message)};exit 1};exit 0";
        var executable = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "WindowsPowerShell", "v1.0", "powershell.exe");
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
        foreach (var argument in new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-EncodedCommand",
            Convert.ToBase64String(Encoding.Unicode.GetBytes(parser)) }) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try
        {
            var errors = process.StandardError.ReadToEndAsync(cancellation.Token);
            await process.WaitForExitAsync(cancellation.Token);
            Assert.True(process.ExitCode == 0, await errors);
        }
        finally { if (!process.HasExited) process.Kill(entireProcessTree: true); }
    }

    private sealed class NonWindowsFactAttribute : FactAttribute
    {
        public NonWindowsFactAttribute() { if (OperatingSystem.IsWindows()) Skip = "This test verifies the non-Windows failure path."; }
    }
    private sealed class WindowsFactAttribute : FactAttribute
    {
        public WindowsFactAttribute() { if (!OperatingSystem.IsWindows()) Skip = "Windows PowerShell 5.1 syntax test requires Windows."; }
    }
    private sealed class SupportedWindowsFactAttribute : FactAttribute
    {
        public SupportedWindowsFactAttribute()
        {
            if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041)) Skip = "Requires Windows build 19041 or later.";
        }
    }
}
