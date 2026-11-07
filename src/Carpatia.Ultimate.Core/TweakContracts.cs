namespace Carpatia.Ultimate.Core;

public enum RiskClass { Advanced, Experimental, Extreme }
public enum PrivilegeRequirement { StandardUser, Administrator }
public enum FeatureStatus { WorkingAndTested, ImplementedHardwareTestPending, InDevelopment, Unsupported }
public enum OperationStatus
{
    Available, Unsupported, AlreadyApplied, Applying, Verified, PendingRestart,
    Failed, PartiallyApplied, RestoreAvailable, Restoring, Restored, RecoveryRequired
}

/// <summary>Metadata describes evidence and limitations; it is never evidence that a change succeeded.</summary>
public sealed record TweakDefinition(
    string Id,
    string ResourceId,
    string Name,
    string Description,
    string Category,
    RiskClass Risk,
    PrivilegeRequirement Privilege,
    IReadOnlyList<string> SupportedOperatingSystems,
    IReadOnlyList<string> RequiredCapabilities,
    IReadOnlyList<string> SupportedValues,
    IReadOnlyList<string> ConflictsWith,
    IReadOnlyList<Uri> Documentation,
    string ExpectedImpact,
    bool CanRestore = true,
    bool RequiresRestart = false,
    string? Recommendation = null,
    bool RequiresSuccessfulWrite = false);

public sealed record Compatibility(bool IsSupported, string Explanation)
{
    public static Compatibility Supported(string explanation = "Supported") => new(true, explanation);
    public static Compatibility Unsupported(string explanation) => new(false, explanation);
}

/// <summary>
/// State and requested values remain typed all the way to the platform adapter.
/// Implementations must represent all state they modify, including absence of a setting.
/// They must not accept executable command strings.
/// </summary>
public interface ITweak<TState, TValue> where TState : notnull where TValue : notnull
{
    TweakDefinition Definition { get; }
    ValueTask<Compatibility> CheckCompatibilityAsync(CancellationToken cancellationToken);
    ValueTask<TState> ReadAsync(CancellationToken cancellationToken);
    IReadOnlyList<string> Validate(TValue requestedValue);
    TState Project(TState currentState, TValue requestedValue);
    bool StatesEqual(TState left, TState right);
    string DescribeChange(TState currentState, TState expectedState);
    ValueTask ApplyAsync(TValue requestedValue, CancellationToken cancellationToken);
    ValueTask RestoreAsync(TState originalState, CancellationToken cancellationToken);
}

public sealed class TweakUnavailableException(string message) : InvalidOperationException(message);
public sealed class ConfigurationConflictException(string message) : InvalidOperationException(message);
public sealed class RecoveryDataException(string message, Exception? innerException = null)
    : IOException(message, innerException);

public sealed record ProfileSetting(string TweakId, string DisplayValue);

public enum ProfileKind { Balanced, Performance, Ultra, Custom }

/// <summary>Catalog references are validated before a profile can become executable.</summary>
public sealed record OptimizationProfile(ProfileKind Kind, string Description, IReadOnlyList<ProfileSetting> Settings)
{
    public IReadOnlyList<string> ValidateAgainst(IEnumerable<TweakDefinition> registeredTweaks)
    {
        var catalog = registeredTweaks.ToDictionary(x => x.Id, StringComparer.Ordinal);
        var errors = new List<string>();
        foreach (var setting in Settings)
        {
            if (!catalog.TryGetValue(setting.TweakId, out var definition))
                errors.Add($"Tweak '{setting.TweakId}' is not registered.");
            else if (Kind != ProfileKind.Custom && definition.Risk != RiskClass.Advanced)
                errors.Add($"{Kind} cannot automatically include {definition.Risk} tweak '{definition.Id}'.");
        }
        foreach (var duplicate in Settings.GroupBy(x => x.TweakId).Where(x => x.Count() > 1))
            errors.Add($"Tweak '{duplicate.Key}' is selected more than once.");
        return errors;
    }
}
