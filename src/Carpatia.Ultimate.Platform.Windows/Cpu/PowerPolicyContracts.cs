namespace Carpatia.Ultimate.Platform.Windows.Cpu;

public enum PowerPolicySource { Ac, Dc }
public enum CpuPowerSetting
{
    MinimumProcessorState,
    MaximumProcessorState,
    EnergyPerformancePreference,
    BoostMode
}

/// <summary>Windows policy definition, not evidence of a processor's electrical tuning capability.</summary>
public sealed record PowerPolicyCapability(
    bool IsSupported,
    string Explanation,
    uint Minimum,
    uint Maximum,
    uint Increment,
    IReadOnlyList<uint> DiscreteValues)
{
    public static PowerPolicyCapability Unsupported(string explanation) => new(false, explanation, 0, 0, 1, []);
    public bool Allows(uint value) => IsSupported && value >= Minimum && value <= Maximum
        && (DiscreteValues.Count > 0 ? DiscreteValues.Contains(value) : Increment > 0 && (value - Minimum) % Increment == 0);
}

public interface IPowerPolicyApi
{
    bool IsSupported { get; }
    Guid GetActiveScheme();
    PowerPolicyCapability GetCapability(CpuPowerSetting setting);
    uint ReadValue(Guid schemeId, PowerPolicySource source, CpuPowerSetting setting);
    void WriteValue(Guid schemeId, PowerPolicySource source, CpuPowerSetting setting, uint value);
    void ReapplyActiveScheme(Guid expectedSchemeId);
}

/// <summary>Only Value is modified; CompanionValue is read-only context for min/max validation.</summary>
public sealed record CpuPowerState(
    Guid SchemeId,
    PowerPolicySource Source,
    CpuPowerSetting Setting,
    uint Value,
    uint? CompanionValue);

public static class CpuPowerPolicyCatalog
{
    public static Guid ProcessorSubgroup { get; } = new("54533251-82be-4824-96c1-47b60b740d00");

    public static Guid SettingGuid(CpuPowerSetting setting) => setting switch
    {
        CpuPowerSetting.MinimumProcessorState => new("893dee8e-2bef-41e0-89c6-b55d0929964c"),
        CpuPowerSetting.MaximumProcessorState => new("bc5038f7-23e0-4960-96da-33abaf5935ec"),
        CpuPowerSetting.EnergyPerformancePreference => new("36687f9e-e3a5-4dbf-b1dc-15eb381c6863"),
        CpuPowerSetting.BoostMode => new("be337238-0d82-4146-a960-4f3749d470c7"),
        _ => throw new ArgumentOutOfRangeException(nameof(setting))
    };

    public static IReadOnlyDictionary<uint, string> BoostModes { get; } =
        new System.Collections.ObjectModel.ReadOnlyDictionary<uint, string>(new Dictionary<uint, string>
        {
            [0] = "Disabled", [1] = "Enabled", [2] = "Aggressive", [3] = "Efficient enabled",
            [4] = "Efficient aggressive", [5] = "Aggressive at guaranteed",
            [6] = "Efficient aggressive at guaranteed"
        });

    public static string Name(CpuPowerSetting setting) => setting switch
    {
        CpuPowerSetting.MinimumProcessorState => "Minimum processor state",
        CpuPowerSetting.MaximumProcessorState => "Maximum processor state",
        CpuPowerSetting.EnergyPerformancePreference => "Energy Performance Preference",
        CpuPowerSetting.BoostMode => "Processor boost mode",
        _ => throw new ArgumentOutOfRangeException(nameof(setting))
    };

    public static string Format(CpuPowerSetting setting, uint value) => setting == CpuPowerSetting.BoostMode
        ? BoostModes.GetValueOrDefault(value, $"Unknown mode {value}") : $"{value}%";
}
