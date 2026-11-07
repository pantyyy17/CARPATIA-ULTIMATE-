using System.ComponentModel;
using System.Runtime.InteropServices;
using Carpatia.Ultimate.Core;

namespace Carpatia.Ultimate.Platform.Windows.Cpu;

/// <summary>Allowlisted documented PowrProf calls; no registry writes, commands or firmware access.</summary>
public sealed class NativePowerPolicyApi : IPowerPolicyApi
{
    public bool IsSupported => OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041);

    public Guid GetActiveScheme()
    {
        RequireWindows();
        Check(Native.PowerGetActiveScheme(0, out var memory), "Read active power scheme");
        if (memory == 0) throw new IOException("Windows returned no active power scheme.");
        try { return Marshal.PtrToStructure<Guid>(memory); }
        finally { Native.LocalFree(memory); }
    }

    public PowerPolicyCapability GetCapability(CpuPowerSetting setting)
    {
        if (!IsSupported) return PowerPolicyCapability.Unsupported("Windows 10 build 19041 or later is required.");
        var subgroup = CpuPowerPolicyCatalog.ProcessorSubgroup;
        var id = CpuPowerPolicyCatalog.SettingGuid(setting);
        const string explanation = "Windows exposes this power-policy definition. Firmware and the processor may limit or ignore its effect.";
        if (setting == CpuPowerSetting.BoostMode)
        {
            var values = new List<uint>();
            // Only documented modes 0–6 are accepted, even if a future driver publishes extra values.
            for (uint index = 0; index <= 6; index++)
            {
                uint size = sizeof(uint);
                var error = Native.PowerReadPossibleValue(0, in subgroup, in id, out var type, index, out var value, ref size);
                if (error is 2 or 259) break; // ERROR_FILE_NOT_FOUND / ERROR_NO_MORE_ITEMS.
                if (error != 0 || type != 4 || size != sizeof(uint))
                    return PowerPolicyCapability.Unsupported("Windows did not expose validated DWORD boost-mode metadata.");
                if (CpuPowerPolicyCatalog.BoostModes.ContainsKey(value)) values.Add(value);
            }
            return values.Count == 0
                ? PowerPolicyCapability.Unsupported("No documented processor boost mode is available.")
                : new(true, explanation, values.Min(), values.Max(), 1, values.Distinct().Order().ToArray());
        }

        var minError = Native.PowerReadValueMin(0, in subgroup, in id, out var min);
        var maxError = Native.PowerReadValueMax(0, in subgroup, in id, out var max);
        var incrementError = Native.PowerReadValueIncrement(0, in subgroup, in id, out var increment);
        if (minError != 0 || maxError != 0 || incrementError != 0 || min > max || max > 100 || increment == 0)
            return PowerPolicyCapability.Unsupported("Windows did not expose a validated 0–100 policy range and increment.");
        return new(true, explanation, min, max, increment, []);
    }

    public uint ReadValue(Guid schemeId, PowerPolicySource source, CpuPowerSetting setting)
    {
        RequireWindows();
        ValidateSelection(schemeId, source);
        var subgroup = CpuPowerPolicyCatalog.ProcessorSubgroup;
        var id = CpuPowerPolicyCatalog.SettingGuid(setting);
        uint value;
        var error = source == PowerPolicySource.Ac
            ? Native.PowerReadACValueIndex(0, in schemeId, in subgroup, in id, out value)
            : Native.PowerReadDCValueIndex(0, in schemeId, in subgroup, in id, out value);
        Check(error, "Read selected processor power policy");
        return value;
    }

    public void WriteValue(Guid schemeId, PowerPolicySource source, CpuPowerSetting setting, uint value)
    {
        RequireWindows();
        ValidateSelection(schemeId, source);
        if (!GetCapability(setting).Allows(value))
            throw new ArgumentOutOfRangeException(nameof(value), "The value is not supported by Windows policy metadata.");
        RequireActive(schemeId);
        var subgroup = CpuPowerPolicyCatalog.ProcessorSubgroup;
        var id = CpuPowerPolicyCatalog.SettingGuid(setting);
        Check(source == PowerPolicySource.Ac
            ? Native.PowerWriteACValueIndex(0, in schemeId, in subgroup, in id, value)
            : Native.PowerWriteDCValueIndex(0, in schemeId, in subgroup, in id, value),
            "Write selected processor power policy");
        RequireActive(schemeId);
    }

    public void ReapplyActiveScheme(Guid expectedSchemeId)
    {
        RequireWindows();
        if (expectedSchemeId == Guid.Empty) throw new ArgumentException("A power scheme must be selected.", nameof(expectedSchemeId));
        RequireActive(expectedSchemeId);
        Check(Native.PowerSetActiveScheme(0, in expectedSchemeId), "Reapply active power scheme");
        RequireActive(expectedSchemeId);
    }

    private void RequireActive(Guid expected)
    {
        if (GetActiveScheme() != expected)
            throw new ConfigurationConflictException("The active power scheme changed. The adapter will not deliberately switch it back. Review the selected scheme again.");
    }

    private void RequireWindows()
    {
        if (!IsSupported) throw new PlatformNotSupportedException("Windows 10 build 19041 or later is required.");
    }

    private static void ValidateSelection(Guid schemeId, PowerPolicySource source)
    {
        if (schemeId == Guid.Empty) throw new ArgumentException("A power scheme must be selected.", nameof(schemeId));
        if (!Enum.IsDefined(source)) throw new ArgumentOutOfRangeException(nameof(source));
    }

    private static void Check(uint error, string operation)
    {
        if (error != 0) throw new Win32Exception(unchecked((int)error), $"{operation} failed (Windows error {error}).");
    }

    private static class Native
    {
        [DllImport("powrprof.dll")] internal static extern uint PowerGetActiveScheme(nint root, out nint scheme);
        [DllImport("powrprof.dll")] internal static extern uint PowerSetActiveScheme(nint root, in Guid scheme);
        [DllImport("powrprof.dll")] internal static extern uint PowerReadACValueIndex(nint root, in Guid scheme, in Guid subgroup, in Guid setting, out uint value);
        [DllImport("powrprof.dll")] internal static extern uint PowerReadDCValueIndex(nint root, in Guid scheme, in Guid subgroup, in Guid setting, out uint value);
        [DllImport("powrprof.dll")] internal static extern uint PowerWriteACValueIndex(nint root, in Guid scheme, in Guid subgroup, in Guid setting, uint value);
        [DllImport("powrprof.dll")] internal static extern uint PowerWriteDCValueIndex(nint root, in Guid scheme, in Guid subgroup, in Guid setting, uint value);
        [DllImport("powrprof.dll")] internal static extern uint PowerReadValueMin(nint root, in Guid subgroup, in Guid setting, out uint value);
        [DllImport("powrprof.dll")] internal static extern uint PowerReadValueMax(nint root, in Guid subgroup, in Guid setting, out uint value);
        [DllImport("powrprof.dll")] internal static extern uint PowerReadValueIncrement(nint root, in Guid subgroup, in Guid setting, out uint value);
        [DllImport("powrprof.dll")] internal static extern uint PowerReadPossibleValue(nint root, in Guid subgroup, in Guid setting, out uint type, uint index, out uint value, ref uint size);
        [DllImport("kernel32.dll")] internal static extern nint LocalFree(nint memory);
    }
}
