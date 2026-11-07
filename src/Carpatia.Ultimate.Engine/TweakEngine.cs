using System.Text.Json;
using System.Text.Json.Serialization;
using Carpatia.Ultimate.Core;

namespace Carpatia.Ultimate.Engine;

public interface IPrivilegeContext { bool IsAdministrator { get; } }
public sealed class StandardUserPrivilegeContext : IPrivilegeContext { public bool IsAdministrator => false; }
public sealed record ExecutionOptions(bool RiskAcknowledged = false);
public sealed record OperationResult(OperationStatus Status, Guid? OperationId, string Message, bool RestartRequired = false);

/// <summary>Immutable serialized state prevents callers mutating a reviewed plan after preview.</summary>
public sealed class TweakPreview<TState, TValue> where TState : notnull where TValue : notnull
{
    internal TweakPreview(ITweak<TState, TValue> source, JsonElement before, JsonElement expected,
        JsonElement requested, string description, bool hasChanges)
    {
        Source = source;
        Before = before;
        Expected = expected;
        Requested = requested;
        DefinitionSnapshot = JsonSerializer.SerializeToElement(source.Definition);
        TweakId = source.Definition.Id;
        Description = description;
        HasChanges = hasChanges;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    internal ITweak<TState, TValue> Source { get; }
    internal JsonElement Before { get; }
    internal JsonElement Expected { get; }
    internal JsonElement Requested { get; }
    internal JsonElement DefinitionSnapshot { get; }
    public string TweakId { get; }
    /// <summary>A detached snapshot suitable for comparing a reviewed ticket in a privileged helper.</summary>
    public JsonElement BeforeState => Before.Clone();
    public JsonElement ExpectedState => Expected.Clone();
    public string Description { get; }
    public bool HasChanges { get; }
    public DateTimeOffset CreatedAt { get; }
}

/// <summary>
/// Modification is allowed only after durable snapshot persistence. Every reported
/// change is read back; unknown external states require manual conflict resolution.
/// </summary>
public sealed class TweakEngine(IOperationJournal journal, IPrivilegeContext? privileges = null)
{
    private readonly IPrivilegeContext _privileges = privileges ?? new StandardUserPrivilegeContext();
    private static readonly JsonSerializerOptions StateOptions = new()
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true
    };

    public async ValueTask<TweakPreview<TState, TValue>> PreviewAsync<TState, TValue>(
        ITweak<TState, TValue> tweak, TValue requestedValue, CancellationToken cancellationToken = default)
        where TState : notnull where TValue : notnull
    {
        ArgumentNullException.ThrowIfNull(tweak);
        ArgumentNullException.ThrowIfNull(requestedValue);
        await EnsureSupportedAsync(tweak, cancellationToken);
        Validate(tweak, requestedValue);
        var records = await journal.GetAllAsync(cancellationToken);
        EnsureNoConflicts(tweak.Definition, records);
        var current = await tweak.ReadAsync(cancellationToken);
        var before = Serialize(current);
        var expected = tweak.Project(Deserialize<TState>(before), requestedValue);
        return new(tweak, before, Serialize(expected), Serialize(requestedValue),
            tweak.DescribeChange(current, expected), !tweak.StatesEqual(current, expected));
    }

    public async ValueTask<OperationResult> ApplyAsync<TState, TValue>(ITweak<TState, TValue> tweak,
        TweakPreview<TState, TValue> preview, ExecutionOptions? options = null,
        CancellationToken cancellationToken = default) where TState : notnull where TValue : notnull
    {
        ArgumentNullException.ThrowIfNull(tweak);
        ArgumentNullException.ThrowIfNull(preview);
        if (!ReferenceEquals(tweak, preview.Source))
            throw new ArgumentException("The preview belongs to another adapter.", nameof(preview));
        if (DateTimeOffset.UtcNow - preview.CreatedAt > TimeSpan.FromMinutes(15))
            throw new ConfigurationConflictException("The preview has expired. Review a fresh system reading.");
        if (!JsonElement.DeepEquals(preview.DefinitionSnapshot, JsonSerializer.SerializeToElement(tweak.Definition)))
            throw new ConfigurationConflictException("The operation definition changed after review. Create a new preview.");
        await EnsureSupportedAsync(tweak, cancellationToken);
        EnsurePermission(tweak.Definition, options);
        if (!tweak.Definition.CanRestore)
            throw new TweakUnavailableException("This foundation only executes fully restorable changes.");
        var requested = Deserialize<TValue>(preview.Requested);
        Validate(tweak, requested);
        await using var execution = await journal.AcquireExecutionLeaseAsync(cancellationToken);
        await using var resource = await journal.AcquireResourceLeaseAsync(tweak.Definition.ResourceId, cancellationToken);
        var records = await journal.GetAllAsync(cancellationToken);
        EnsureNoConflicts(tweak.Definition, records);
        var active = GetActive(tweak.Definition, records);
        if (active is not null && active.Status is not (OperationStatus.Verified or OperationStatus.PendingRestart))
            throw new ConfigurationConflictException("An unfinished operation requires recovery before another Apply.");
        if (active is not null)
        {
            // A corrupt original must block further changes, not be discovered only when Restore is needed.
            _ = Deserialize<TState>(active.OriginalState);
            _ = Deserialize<TState>(active.BeforeState);
            if (active.LastObservedState is { } lastObserved) _ = Deserialize<TState>(lastObserved);
        }
        var current = await tweak.ReadAsync(cancellationToken);
        if (active is not null && !tweak.StatesEqual(current, Deserialize<TState>(active.ExpectedState)))
            throw new ConfigurationConflictException("The resource changed outside Carpatia. No setting was overwritten.");
        var expected = Deserialize<TState>(preview.Expected);
        if (!tweak.StatesEqual(expected, tweak.Project(Deserialize<TState>(Serialize(current)), requested)))
            throw new ConfigurationConflictException("The operation projection changed after review. Create a new preview.");
        if (tweak.StatesEqual(current, expected))
            return new(OperationStatus.AlreadyApplied, active?.Id, "The requested configuration is already present.",
                active?.RestartRequired == true);
        if (!tweak.StatesEqual(current, Deserialize<TState>(preview.Before)))
            throw new ConfigurationConflictException("System state changed after preview. Review the current configuration again.");

        var now = DateTimeOffset.UtcNow;
        var record = new OperationRecord(1, active?.Id ?? Guid.NewGuid(), tweak.Definition.Id,
            tweak.Definition.ResourceId, OperationStatus.Applying, active?.OriginalState ?? Serialize(current),
            Serialize(current), preview.Expected, active?.CreatedAt ?? now, now, (active?.Revision ?? 0) + 1,
            tweak.Definition.ConflictsWith.ToArray(), RestartRequired: tweak.Definition.RequiresRestart || active?.RestartRequired == true,
            RequiresSuccessfulWrite: tweak.Definition.RequiresSuccessfulWrite || active?.RequiresSuccessfulWrite == true);
        await journal.SaveAsync(record, cancellationToken); // The only path into Apply passes this durable write.
        Exception? writeError = null;
        try { await tweak.ApplyAsync(requested, cancellationToken); }
        catch (Exception error) { writeError = error; }
        return await VerifyAndRecordAsync(tweak, record, expected, current, false, writeError);
    }

    public ValueTask<OperationResult> RestoreAsync<TState, TValue>(ITweak<TState, TValue> tweak,
        Guid operationId, CancellationToken cancellationToken = default)
        where TState : notnull where TValue : notnull
        => RestoreCoreAsync(tweak, operationId, null, null, cancellationToken);

    /// <summary>Bind restoration to the exact resource state reviewed before helper confirmation.</summary>
    public ValueTask<OperationResult> RestoreReviewedAsync<TState, TValue>(ITweak<TState, TValue> tweak,
        Guid operationId, JsonElement reviewedBefore, CancellationToken cancellationToken = default)
        where TState : notnull where TValue : notnull
        => RestoreCoreAsync(tweak, operationId, reviewedBefore.Clone(), null, cancellationToken);

    /// <summary>Also bind the effective original configuration so a changed journal cannot alter the confirmed target.</summary>
    public ValueTask<OperationResult> RestoreReviewedAsync<TState, TValue>(ITweak<TState, TValue> tweak,
        Guid operationId, JsonElement reviewedBefore, JsonElement reviewedExpected,
        CancellationToken cancellationToken = default)
        where TState : notnull where TValue : notnull
        => RestoreCoreAsync(tweak, operationId, reviewedBefore.Clone(), reviewedExpected.Clone(), cancellationToken);

    private async ValueTask<OperationResult> RestoreCoreAsync<TState, TValue>(ITweak<TState, TValue> tweak,
        Guid operationId, JsonElement? reviewedBefore, JsonElement? reviewedExpected, CancellationToken cancellationToken)
        where TState : notnull where TValue : notnull
    {
        ArgumentNullException.ThrowIfNull(tweak);
        await EnsureSupportedAsync(tweak, cancellationToken);
        // Restoration removes the reviewed tuning risk, but must still satisfy privileges.
        EnsurePermission(tweak.Definition, new ExecutionOptions(RiskAcknowledged: true));
        if (!tweak.Definition.CanRestore) throw new TweakUnavailableException("Restoration is unsupported.");
        await using var execution = await journal.AcquireExecutionLeaseAsync(cancellationToken);
        await using var resource = await journal.AcquireResourceLeaseAsync(tweak.Definition.ResourceId, cancellationToken);
        var records = await journal.GetAllAsync(cancellationToken);
        var record = records.SingleOrDefault(x => x.Id == operationId)
            ?? throw new KeyNotFoundException("Recovery operation was not found.");
        if (record.TweakId != tweak.Definition.Id || record.ResourceId != tweak.Definition.ResourceId)
            throw new ConfigurationConflictException("The recovery record belongs to another resource.");
        if (record.Status == OperationStatus.Restored && !reviewedBefore.HasValue)
            return new(OperationStatus.Restored, record.Id, "This operation was already restored. No state was overwritten.",
                record.RestartRequired);
        if (record.Status != OperationStatus.Restored && GetActive(tweak.Definition, records)?.Id != record.Id)
            throw new ConfigurationConflictException("Another operation owns this resource.");
        var current = await tweak.ReadAsync(cancellationToken);
        if (reviewedBefore is { } reviewedState && !JsonElement.DeepEquals(reviewedState, Serialize(current)))
            throw new ConfigurationConflictException("The resource changed after restoration review. Read and review a new plan.");
        var original = Deserialize<TState>(record.OriginalState);
        if (reviewedExpected is { } reviewedTarget && !tweak.StatesEqual(original, Deserialize<TState>(reviewedTarget)))
            throw new ConfigurationConflictException("The saved original changed after restoration review. Read and review a new plan.");
        if (record.Status == OperationStatus.Restored)
        {
            if (!tweak.StatesEqual(current, original))
                throw new ConfigurationConflictException("This operation was already restored, but its resource has since changed. No later configuration was overwritten.");
            return new(OperationStatus.Restored, record.Id, "This operation was already restored. No state was overwritten.",
                record.RestartRequired);
        }
        var expected = Deserialize<TState>(record.ExpectedState);
        var before = Deserialize<TState>(record.BeforeState);
        var lastObserved = record.LastObservedState is { } savedObservation
            ? Deserialize<TState>(savedObservation) : default;
        // Some Windows APIs persist configuration before a separate activation step.
        // After an interrupted/failed write, matching storage alone cannot prove activation.
        var requiresSuccessfulWrite = tweak.Definition.RequiresSuccessfulWrite || record.RequiresSuccessfulWrite;
        var writeCompletionNeeded = requiresSuccessfulWrite
            && record.Status is not (OperationStatus.Verified or OperationStatus.PendingRestart);
        if (tweak.StatesEqual(current, original) && !writeCompletionNeeded)
        {
            await journal.SaveAsync(record with { Status = OperationStatus.Restored, UpdatedAt = DateTimeOffset.UtcNow,
                Detail = "Original configuration verified; no write was needed." }, cancellationToken);
            return new(OperationStatus.Restored, record.Id, "The original configuration is verified.", record.RestartRequired);
        }
        if (!tweak.StatesEqual(current, expected)
            && !tweak.StatesEqual(current, before)
            && !(record.LastObservedState.HasValue && tweak.StatesEqual(current, lastObserved!)))
            throw new ConfigurationConflictException("The current configuration is unknown or externally changed. Automatic restore is blocked.");
        record = record with { Status = OperationStatus.Restoring, UpdatedAt = DateTimeOffset.UtcNow,
            Revision = record.Revision + 1, Detail = null, BeforeState = Serialize(current), LastObservedState = null,
            RequiresSuccessfulWrite = requiresSuccessfulWrite };
        await journal.SaveAsync(record, cancellationToken);
        Exception? writeError = null;
        try { await tweak.RestoreAsync(original, cancellationToken); }
        catch (Exception error) { writeError = error; }
        return await VerifyAndRecordAsync(tweak, record, original, current, true, writeError);
    }

    private async ValueTask<OperationResult> VerifyAndRecordAsync<TState, TValue>(ITweak<TState, TValue> tweak,
        OperationRecord record, TState expected, TState before, bool restoring, Exception? writeError)
        where TState : notnull where TValue : notnull
    {
        OperationStatus status;
        string detail;
        JsonElement? observed = null;
        try
        {
            // Once a write starts, cancellation cannot skip readback or durable result recording.
            var actual = await tweak.ReadAsync(CancellationToken.None);
            observed = Serialize(actual);
            if (tweak.StatesEqual(actual, expected))
            {
                if (writeError is not null && record.RequiresSuccessfulWrite)
                {
                    status = restoring ? OperationStatus.RecoveryRequired : OperationStatus.PartiallyApplied;
                    detail = $"Stored configuration matches, but activation did not complete ({writeError.GetType().Name}). Recovery is required.";
                }
                else
                {
                    status = restoring ? OperationStatus.Restored
                        : record.RestartRequired ? OperationStatus.PendingRestart : OperationStatus.Verified;
                    detail = writeError is null ? "Configuration read back and verified."
                        : $"Configuration verified despite adapter error ({writeError.GetType().Name}).";
                    if (record.RestartRequired) detail += " Restart Windows to activate this configuration.";
                }
            }
            else
            {
                status = restoring ? OperationStatus.RecoveryRequired
                    : tweak.StatesEqual(actual, before) ? OperationStatus.Failed : OperationStatus.PartiallyApplied;
                detail = "Readback did not match the requested configuration. Review recovery before continuing.";
                if (writeError is not null) detail += $" Adapter error: {writeError.GetType().Name}.";
            }
        }
        catch (Exception error)
        {
            status = OperationStatus.RecoveryRequired;
            detail = $"State could not be verified ({error.GetType().Name}). The original snapshot has been retained.";
        }
        await journal.SaveAsync(record with { Status = status, UpdatedAt = DateTimeOffset.UtcNow, Detail = detail,
            LastObservedState = observed }, CancellationToken.None);
        return new(status, record.Id, detail, record.RestartRequired);
    }

    private static async ValueTask EnsureSupportedAsync<TState, TValue>(ITweak<TState, TValue> tweak,
        CancellationToken cancellationToken) where TState : notnull where TValue : notnull
    {
        var compatibility = await tweak.CheckCompatibilityAsync(cancellationToken);
        if (!compatibility.IsSupported) throw new TweakUnavailableException(compatibility.Explanation);
    }

    private void EnsurePermission(TweakDefinition definition, ExecutionOptions? options)
    {
        if (definition.Privilege == PrivilegeRequirement.Administrator && !_privileges.IsAdministrator)
            throw new UnauthorizedAccessException("This operation requires a narrowly scoped elevated adapter.");
        if (definition.Risk != RiskClass.Advanced && options?.RiskAcknowledged != true)
            throw new UnauthorizedAccessException($"{definition.Risk} operations require explicit risk acknowledgement.");
    }

    private static void Validate<TState, TValue>(ITweak<TState, TValue> tweak, TValue requestedValue)
        where TState : notnull where TValue : notnull
    {
        var errors = tweak.Validate(requestedValue);
        if (errors.Count != 0) throw new ArgumentException(string.Join(Environment.NewLine, errors), nameof(requestedValue));
    }

    private static OperationRecord? GetActive(TweakDefinition definition, IReadOnlyList<OperationRecord> records)
    {
        var active = records.Where(x => x.ResourceId == definition.ResourceId && x.Status != OperationStatus.Restored).ToArray();
        if (active.Length > 1) throw new RecoveryDataException("More than one unrestored operation owns a resource.");
        if (active.Length == 1 && active[0].TweakId != definition.Id)
            throw new ConfigurationConflictException("Another tweak owns the original snapshot for this resource.");
        return active.SingleOrDefault();
    }

    private static void EnsureNoConflicts(TweakDefinition definition, IReadOnlyList<OperationRecord> records)
    {
        if (records.Any(x => x.Status != OperationStatus.Restored
            && (definition.ConflictsWith.Contains(x.TweakId) || x.ConflictsWith.Contains(definition.Id))))
            throw new ConfigurationConflictException("A conflicting tweak is active. Restore it before continuing.");
    }

    private static JsonElement Serialize<T>(T value) => JsonSerializer.SerializeToElement(value, StateOptions);
    private static T Deserialize<T>(JsonElement value)
    {
        try
        {
            var typed = value.Deserialize<T>(StateOptions)
                ?? throw new RecoveryDataException("The saved state cannot be read by this adapter.");
            // A changed state type must not silently supply defaults for missing fields.
            if (!JsonElement.DeepEquals(value, Serialize(typed)))
                throw new RecoveryDataException("The saved state schema differs from the current adapter. Automatic recovery is blocked.");
            return typed;
        }
        catch (JsonException error)
        {
            throw new RecoveryDataException("The saved state cannot be read safely by this adapter.", error);
        }
    }
}
