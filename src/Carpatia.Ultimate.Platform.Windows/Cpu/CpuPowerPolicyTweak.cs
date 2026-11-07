using System.ComponentModel;
using Carpatia.Ultimate.Core;

namespace Carpatia.Ultimate.Platform.Windows.Cpu;

/// <summary>An immutable target: one selected scheme, one AC/DC source and one documented processor policy.</summary>
public sealed class CpuPowerPolicyTweak : ITweak<CpuPowerState, uint>
{
    private readonly IPowerPolicyApi _api;
    public Guid SchemeId { get; }
    public PowerPolicySource Source { get; }
    public CpuPowerSetting Setting { get; }
    public TweakDefinition Definition { get; }

    public CpuPowerPolicyTweak(IPowerPolicyApi api, Guid schemeId, PowerPolicySource source, CpuPowerSetting setting)
    {
        ArgumentNullException.ThrowIfNull(api);
        if (schemeId == Guid.Empty) throw new ArgumentException("Select a power scheme explicitly.", nameof(schemeId));
        if (!Enum.IsDefined(source)) throw new ArgumentOutOfRangeException(nameof(source));
        if (!Enum.IsDefined(setting)) throw new ArgumentOutOfRangeException(nameof(setting));
        _api = api;
        SchemeId = schemeId;
        Source = source;
        Setting = setting;
        var key = setting switch
        {
            CpuPowerSetting.MinimumProcessorState => "minimum-state",
            CpuPowerSetting.MaximumProcessorState => "maximum-state",
            CpuPowerSetting.EnergyPerformancePreference => "energy-preference",
            _ => "boost-mode"
        };
        Definition = new($"cpu.{key}.{source.ToString().ToLowerInvariant()}.{schemeId:N}",
            $"power/{schemeId:D}/{source}/{CpuPowerPolicyCatalog.SettingGuid(setting):D}",
            $"{CpuPowerPolicyCatalog.Name(setting)} · {source.ToString().ToUpperInvariant()}",
            "Changes only the selected Windows processor power-policy index. The exact scheme, AC/DC source and original index are retained. " +
            "100% minimum state is a frequency policy, not 100% utilization; it can increase power use and heat.",
            "CPU", RiskClass.Advanced, PrivilegeRequirement.Administrator,
            ["Windows 11", "Windows 10 build 19041 or later"],
            ["Selected scheme is still active", "Windows exposes policy metadata and the selected index"],
            setting == CpuPowerSetting.BoostMode ? CpuPowerPolicyCatalog.BoostModes.Select(x => $"{x.Key}: {x.Value}").ToArray() : ["0–100%, within detected Windows bounds and increment"],
            [],
            [new("https://learn.microsoft.com/windows/win32/api/powrprof/nf-powrprof-powerreadacvalueindex"),
                new("https://learn.microsoft.com/windows/win32/api/powersetting/nf-powersetting-powerwriteacvalueindex"),
                new("https://learn.microsoft.com/windows/win32/api/powersetting/nf-powersetting-powersetactivescheme"),
                new("https://github.com/microsoft/win32metadata/blob/main/generation/WinSDK/RecompiledIdlHeaders/um/winnt.h")],
            "Readback verifies the stored policy, not a processor frequency or performance gain. Hardware, firmware and Windows power-mode overlays may limit its effect.",
            RequiresSuccessfulWrite: true);
    }

    public ValueTask<Compatibility> CheckCompatibilityAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_api.IsSupported) return ValueTask.FromResult(Compatibility.Unsupported("Windows processor power APIs are unavailable."));
        try
        {
            EnsureActiveScheme();
            var capability = _api.GetCapability(Setting);
            if (!capability.IsSupported) return ValueTask.FromResult(Compatibility.Unsupported(capability.Explanation));
            var current = _api.ReadValue(SchemeId, Source, Setting);
            if (!capability.Allows(current))
                return ValueTask.FromResult(Compatibility.Unsupported("The current policy index is not described by validated metadata. It cannot be safely restored."));
            if (CompanionSetting is { } companion)
            {
                var peer = _api.ReadValue(SchemeId, Source, companion);
                if (!_api.GetCapability(companion).Allows(peer))
                    return ValueTask.FromResult(Compatibility.Unsupported("The companion minimum/maximum policy cannot be validated."));
            }
            EnsureActiveScheme();
            return ValueTask.FromResult(Compatibility.Supported(capability.Explanation));
        }
        catch (Exception error) when (error is Win32Exception or PlatformNotSupportedException)
        {
            return ValueTask.FromResult(Compatibility.Unsupported($"The selected processor policy is unavailable: {error.Message}"));
        }
    }

    public ValueTask<CpuPowerState> ReadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureActiveScheme();
        var value = _api.ReadValue(SchemeId, Source, Setting);
        uint? peer = CompanionSetting is { } companion ? _api.ReadValue(SchemeId, Source, companion) : null;
        EnsureActiveScheme();
        return ValueTask.FromResult(new CpuPowerState(SchemeId, Source, Setting, value, peer));
    }

    public IReadOnlyList<string> Validate(uint requestedValue)
    {
        if (Setting == CpuPowerSetting.BoostMode && !CpuPowerPolicyCatalog.BoostModes.ContainsKey(requestedValue))
            return ["Select a documented processor boost mode (0–6)."];
        if (Setting != CpuPowerSetting.BoostMode && requestedValue > 100)
            return ["Processor policy percentages must be within 0–100."];
        return _api.GetCapability(Setting).Allows(requestedValue)
            ? [] : ["The requested index is not supported by detected Windows bounds, increment or enumerated values."];
    }

    public CpuPowerState Project(CpuPowerState currentState, uint requestedValue)
    {
        ValidateIdentity(currentState);
        if (!PairIsValid(Setting, currentState.Value, currentState.CompanionValue))
            throw new ConfigurationConflictException("An invalid existing minimum/maximum combination cannot become a new original snapshot. Review recovery or correct the bound in Windows.");
        var errors = Validate(requestedValue);
        if (errors.Count != 0) throw new ArgumentException(string.Join(" ", errors), nameof(requestedValue));
        ValidatePair(requestedValue, currentState.CompanionValue);
        return currentState with { Value = requestedValue };
    }

    // Companion values belong to another resource and are never restored here. Their
    // validity still matters: an invalid pair cannot verify against a valid preview.
    // Comparing the validity flags (rather than requiring true) keeps equality reflexive,
    // so a persisted invalid LastObservedState can be recognized for safe recovery.
    public bool StatesEqual(CpuPowerState left, CpuPowerState right) =>
        left.SchemeId == right.SchemeId && left.Source == right.Source && left.Setting == right.Setting && left.Value == right.Value
        && PairIsValid(left.Setting, left.Value, left.CompanionValue) == PairIsValid(right.Setting, right.Value, right.CompanionValue);

    public string DescribeChange(CpuPowerState currentState, CpuPowerState expectedState) =>
        $"{Definition.Name}: {CpuPowerPolicyCatalog.Format(Setting, currentState.Value)} → {CpuPowerPolicyCatalog.Format(Setting, expectedState.Value)}. " +
        $"Scheme {SchemeId:D}; only {Source.ToString().ToUpperInvariant()} is changed. Original index will be retained. " +
        "Windows policy readback does not guarantee a hardware or performance effect.";

    public async ValueTask ApplyAsync(uint requestedValue, CancellationToken cancellationToken)
    {
        var current = await ReadAsync(cancellationToken);
        _ = Project(current, requestedValue); // Fresh bounds and companion validation immediately before writing.
        cancellationToken.ThrowIfCancellationRequested();
        WriteAndReapply(requestedValue);
    }

    public async ValueTask RestoreAsync(CpuPowerState originalState, CancellationToken cancellationToken)
    {
        ValidateIdentity(originalState);
        if (!PairIsValid(Setting, originalState.Value, originalState.CompanionValue))
            throw new RecoveryDataException("The original processor snapshot has an invalid minimum/maximum combination.");
        var current = await ReadAsync(cancellationToken);
        var errors = Validate(originalState.Value);
        if (errors.Count != 0) throw new TweakUnavailableException(string.Join(" ", errors));
        // A known partial state can be invalid. Restoring its original may safely repair it,
        // but must still respect the companion value as it exists now.
        ValidatePair(originalState.Value, current.CompanionValue);
        cancellationToken.ThrowIfCancellationRequested();
        WriteAndReapply(originalState.Value);
    }

    private void WriteAndReapply(uint value)
    {
        EnsureActiveScheme();
        _api.WriteValue(SchemeId, Source, Setting, value);
        EnsureActiveScheme();
        _api.ReapplyActiveScheme(SchemeId);
        EnsureActiveScheme();
    }

    private CpuPowerSetting? CompanionSetting => Setting switch
    {
        CpuPowerSetting.MinimumProcessorState => CpuPowerSetting.MaximumProcessorState,
        CpuPowerSetting.MaximumProcessorState => CpuPowerSetting.MinimumProcessorState,
        _ => null
    };

    private void ValidatePair(uint requested, uint? companion)
    {
        if (CompanionSetting is null) return;
        if (companion is null || companion > 100)
            throw new ConfigurationConflictException("The companion processor state is unavailable or invalid.");
        if (Setting == CpuPowerSetting.MinimumProcessorState && requested > companion)
            throw new ArgumentException("Minimum processor state cannot exceed the current maximum for the selected AC/DC source.");
        if (Setting == CpuPowerSetting.MaximumProcessorState && requested < companion)
            throw new ArgumentException("Maximum processor state cannot be below the current minimum for the selected AC/DC source.");
    }

    private static bool PairIsValid(CpuPowerSetting setting, uint value, uint? companion) => setting switch
    {
        CpuPowerSetting.MinimumProcessorState => companion is <= 100 && value <= companion,
        CpuPowerSetting.MaximumProcessorState => companion is <= 100 && value >= companion,
        _ => true
    };

    private void ValidateIdentity(CpuPowerState state)
    {
        if (state.SchemeId != SchemeId || state.Source != Source || state.Setting != Setting)
            throw new ConfigurationConflictException("The processor snapshot belongs to a different scheme, AC/DC source or setting.");
    }

    private void EnsureActiveScheme()
    {
        if (_api.GetActiveScheme() != SchemeId)
            throw new ConfigurationConflictException("The selected power scheme is no longer active. No deliberate scheme switch is allowed. Select the original scheme in Windows before restoring its settings.");
    }
}
