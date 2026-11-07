using Carpatia.Ultimate.Platform.Windows.Cpu;
using Xunit;

namespace Carpatia.Ultimate.Platform.Tests.Cpu;

public sealed class WindowsCpuFactAttribute : FactAttribute
{
    public WindowsCpuFactAttribute()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
            Skip = "Read-only PowrProf integration requires Windows 10 build 19041 or later.";
    }
}

public sealed class WindowsCpuPowerReadTests
{
    [WindowsCpuFact]
    public void ReadsActiveSchemeAndAvailablePowerPolicyWithoutWriting()
    {
        var api = new NativePowerPolicyApi();
        Assert.True(api.IsSupported);
        var active = api.GetActiveScheme();
        Assert.NotEqual(Guid.Empty, active);
        foreach (var setting in Enum.GetValues<CpuPowerSetting>())
        {
            var capability = api.GetCapability(setting);
            Assert.False(string.IsNullOrWhiteSpace(capability.Explanation));
            if (!capability.IsSupported) continue;
            var ac = api.ReadValue(active, PowerPolicySource.Ac, setting);
            var dc = api.ReadValue(active, PowerPolicySource.Dc, setting);
            Assert.True(capability.Allows(ac), $"Current AC {setting} index {ac} is outside the exposed metadata.");
            Assert.True(capability.Allows(dc), $"Current DC {setting} index {dc} is outside the exposed metadata.");
        }
    }
}
