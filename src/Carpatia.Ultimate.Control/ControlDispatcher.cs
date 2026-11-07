using System.Text.Json;
using System.Text.Json.Serialization;
using Carpatia.Ultimate.Broker;
using Carpatia.Ultimate.Core;
using Carpatia.Ultimate.Engine;

namespace Carpatia.Ultimate.Control;

public interface IControlHandler
{
    OperationKind Kind { get; }
    bool Recognizes(OperationRecord record);
    Task<ControlResponse> ExecuteAsync(ControlRequest request, OperationRecord? record,
        Func<ControlPreview, Task<bool>>? confirmation, CancellationToken cancellationToken);
}

/// <summary>Only registered typed operations can pass from the UI or privileged helper to the engine.</summary>
public sealed class ControlDispatcher(IOperationJournal journal, IEnumerable<IControlHandler> handlers,
    IPrivilegeContext privileges) : IControlDispatcher
{
    private readonly IReadOnlyDictionary<OperationKind, IControlHandler> _handlers = handlers.ToDictionary(x => x.Kind);

    public ControlRequest? CreateRestoreRequest(OperationRecord record)
    {
        var handler = _handlers.Values.SingleOrDefault(x => x.Recognizes(record));
        return handler is null ? null : new(ControlAction.PreviewRestore, handler.Kind) { OperationId = record.Id };
    }

    public async Task<ControlResponse> ExecuteAsync(ControlRequest request,
        Func<ControlPreview, Task<bool>>? confirmation = null, CancellationToken cancellationToken = default)
    {
        try
        {
            RequestValidation.Validate(request);
            if (!_handlers.TryGetValue(request.Kind, out var handler))
                throw new TweakUnavailableException("The requested operation is not registered.");
            if (request.Action is ControlAction.Apply or ControlAction.Restore && (!privileges.IsAdministrator || confirmation is null))
                throw new UnauthorizedAccessException("A one-shot elevated helper and fresh user confirmation are required.");
            OperationRecord? record = null;
            if (request.Action is ControlAction.Restore or ControlAction.PreviewRestore)
            {
                record = (await journal.GetAllAsync(cancellationToken)).SingleOrDefault(x => x.Id == request.OperationId)
                    ?? throw new KeyNotFoundException("The recovery record was not found.");
                if (!handler.Recognizes(record))
                    throw new ConfigurationConflictException("The recovery record does not belong to this allowed operation.");
            }
            return await handler.ExecuteAsync(request, record, confirmation, cancellationToken);
        }
        catch (OperationCanceledException) { return new(OperationStatus.Failed, "Operation cancelled. Inspect recovery records if a write had started.", Cancelled: true); }
        catch (TweakUnavailableException error) { return new(OperationStatus.Unsupported, error.Message); }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            return new(OperationStatus.Failed, $"{error.GetType().Name}: {error.Message}");
        }
    }
}

public sealed class TweakControlHandler<TState, TValue>(
    OperationKind kind,
    TweakEngine engine,
    Func<ControlRequest, ITweak<TState, TValue>> createAdapter,
    Func<ControlRequest, TValue> getValue,
    Func<OperationRecord, bool> recognizes,
    Func<OperationRecord, ITweak<TState, TValue>> restoreAdapter,
    Func<TState, TState, TState>? restoreProjection = null) : IControlHandler
    where TState : notnull where TValue : notnull
{
    public OperationKind Kind => kind;
    public bool Recognizes(OperationRecord record) => recognizes(record);

    public async Task<ControlResponse> ExecuteAsync(ControlRequest request, OperationRecord? record,
        Func<ControlPreview, Task<bool>>? confirmation, CancellationToken cancellationToken)
    {
        if (request.Action is ControlAction.Restore or ControlAction.PreviewRestore)
        {
            ArgumentNullException.ThrowIfNull(record);
            var adapter = restoreAdapter(record);
            if (adapter.Definition.Id != record.TweakId || adapter.Definition.ResourceId != record.ResourceId)
                throw new RecoveryDataException("The saved target does not match the registered resource identity.");
            var supported = await adapter.CheckCompatibilityAsync(cancellationToken);
            if (!supported.IsSupported) throw new TweakUnavailableException(supported.Explanation);
            var current = await adapter.ReadAsync(cancellationToken);
            var original = StateJson.Read<TState>(record.OriginalState);
            var expected = restoreProjection is null ? original : restoreProjection(current, original);
            var preview = new ControlPreview(record.ResourceId,
                $"Restore {adapter.Definition.Name}. Only this operation's original configuration is restored. " +
                "External changes are checked again before writing.",
                JsonSerializer.SerializeToElement(current), JsonSerializer.SerializeToElement(expected), DateTimeOffset.UtcNow);
            if (request.Action == ControlAction.PreviewRestore)
                return new(OperationStatus.RestoreAvailable, "Review the original configuration before restoration.", preview, record.Id);
            ValidateReviewedPlan(request.ReviewedPreview!, preview);
            if (confirmation is null || !await confirmation(preview))
                return new(OperationStatus.RestoreAvailable, "Restoration cancelled before execution.", preview, record.Id, true);
            var result = await engine.RestoreReviewedAsync(adapter, record.Id, preview.Before, preview.Expected, cancellationToken);
            return new(result.Status, result.Message, preview, result.OperationId, RestartRequired: result.RestartRequired);
        }

        var tweak = createAdapter(request);
        var plan = await engine.PreviewAsync(tweak, getValue(request), cancellationToken);
        var fresh = new ControlPreview(tweak.Definition.ResourceId,
            $"{plan.Description}\nRisk class: {tweak.Definition.Risk}. {tweak.Definition.ExpectedImpact}" +
            (tweak.Definition.RequiresRestart ? "\nRestart Windows after applying or restoring this configuration." : ""),
            plan.BeforeState, plan.ExpectedState, plan.CreatedAt);
        if (request.Action == ControlAction.Preview)
            return new(plan.HasChanges ? OperationStatus.Available : OperationStatus.AlreadyApplied,
                plan.HasChanges ? "Review the exact configuration change before applying." : "The requested configuration is already present.", fresh);
        ValidateReviewedPlan(request.ReviewedPreview!, fresh);
        if (confirmation is null || !await confirmation(fresh))
            return new(OperationStatus.Available, "Change cancelled before execution.", fresh, Cancelled: true);
        // Risk acknowledgement is supplied only after the helper displays the fresh plan.
        var applied = await engine.ApplyAsync(tweak, plan, new ExecutionOptions(RiskAcknowledged: true), cancellationToken);
        return new(applied.Status, applied.Message, fresh, applied.OperationId, RestartRequired: applied.RestartRequired);
    }

    private static void ValidateReviewedPlan(ControlPreview reviewed, ControlPreview fresh)
    {
        var age = DateTimeOffset.UtcNow - reviewed.CapturedAt;
        if (age > TimeSpan.FromMinutes(15) || age < TimeSpan.FromSeconds(-5)
            || reviewed.ResourceId != fresh.ResourceId || reviewed.Description != fresh.Description
            || !JsonElement.DeepEquals(reviewed.Before, fresh.Before)
            || !JsonElement.DeepEquals(reviewed.Expected, fresh.Expected))
            throw new ConfigurationConflictException("The reviewed plan has expired or the resource changed. Read and review a new plan.");
    }
}

internal static class StateJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true,
        RespectNullableAnnotations = true
    };

    public static T Read<T>(JsonElement value) where T : notnull
    {
        try
        {
            var result = value.Deserialize<T>(Options) ?? throw new RecoveryDataException("The saved state is null.");
            if (!JsonElement.DeepEquals(value, JsonSerializer.SerializeToElement(result)))
                throw new RecoveryDataException("The recovery state schema is not understood by this adapter.");
            return result;
        }
        catch (JsonException error) { throw new RecoveryDataException("The recovery state cannot be safely decoded.", error); }
    }
}
