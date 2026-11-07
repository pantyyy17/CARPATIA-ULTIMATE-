using System.Text.Json;
using Carpatia.Ultimate.Core;
using Carpatia.Ultimate.Engine;
using Xunit;

namespace Carpatia.Ultimate.Tests;

/// <summary>Compound state and recovery-schema regressions required before enabling live adapters.</summary>
public sealed class EngineSafetyTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "carpatia-safety", Guid.NewGuid().ToString("N"));
    private FileOperationJournal Journal => new(_directory);

    [Fact]
    public async Task PartialCompoundWriteRetainsFirstOriginalAndCanBeRestoredAfterRestart()
    {
        var tweak = new CompoundTweak { PartiallyApply = true };
        var first = new TweakEngine(Journal);
        var result = await first.ApplyAsync(tweak, await first.PreviewAsync(tweak, 60));
        Assert.Equal(OperationStatus.PartiallyApplied, result.Status);
        Assert.Equal(new CompoundState(60, 20, "adapter-a"), tweak.State);
        var saved = Assert.Single(await Journal.GetAllAsync());
        Assert.Equal(10, saved.OriginalState.GetProperty("First").GetInt32());
        Assert.Equal(20, saved.LastObservedState!.Value.GetProperty("Second").GetInt32());
        Assert.Equal(OperationStatus.Restored,
            (await new TweakEngine(Journal).RestoreAsync(tweak, result.OperationId!.Value)).Status);
        Assert.Equal(new CompoundState(10, 20, "adapter-a"), tweak.State);
        Assert.Equal(1, tweak.RestoreCalls);
    }

    [Fact]
    public async Task PartiallyFailedRestoreCanRetryFromItsOwnObservedState()
    {
        var tweak = new CompoundTweak();
        var engine = new TweakEngine(Journal);
        var applied = await engine.ApplyAsync(tweak, await engine.PreviewAsync(tweak, 60));
        tweak.PartiallyRestore = true;
        var failed = await engine.RestoreAsync(tweak, applied.OperationId!.Value);
        Assert.Equal(OperationStatus.RecoveryRequired, failed.Status);
        Assert.Equal(new CompoundState(10, 60, "adapter-a"), tweak.State);
        var saved = Assert.Single(await Journal.GetAllAsync());
        Assert.Equal(60, saved.BeforeState.GetProperty("First").GetInt32());
        Assert.Equal(10, saved.LastObservedState!.Value.GetProperty("First").GetInt32());
        tweak.PartiallyRestore = false;
        Assert.Equal(OperationStatus.Restored,
            (await new TweakEngine(Journal).RestoreAsync(tweak, applied.OperationId.Value)).Status);
        Assert.Equal(new CompoundState(10, 20, "adapter-a"), tweak.State);
    }

    [Fact]
    public async Task ExternalEditAfterFailedRestoreIsNeverOverwritten()
    {
        var tweak = new CompoundTweak();
        var engine = new TweakEngine(Journal);
        var applied = await engine.ApplyAsync(tweak, await engine.PreviewAsync(tweak, 60));
        tweak.PartiallyRestore = true;
        await engine.RestoreAsync(tweak, applied.OperationId!.Value);
        tweak.State = tweak.State with { Second = 71 };
        tweak.PartiallyRestore = false;
        await Assert.ThrowsAsync<ConfigurationConflictException>(async () =>
            await engine.RestoreAsync(tweak, applied.OperationId.Value));
        Assert.Equal(71, tweak.State.Second);
        Assert.Equal(1, tweak.RestoreCalls);
    }

    [Fact]
    public async Task StoredValueDoesNotImplySuccessfulActivationWhenAdapterRequiresCompletion()
    {
        var tweak = new CompoundTweak { FailApplyActivation = true };
        tweak.Definition = tweak.Definition with { RequiresSuccessfulWrite = true };
        var engine = new TweakEngine(Journal);
        var applied = await engine.ApplyAsync(tweak, await engine.PreviewAsync(tweak, 60));
        Assert.Equal(new CompoundState(60, 60, "adapter-a"), tweak.State);
        Assert.Equal(OperationStatus.PartiallyApplied, applied.Status);
        Assert.Contains("activation did not complete", applied.Message);
        Assert.NotNull(Assert.Single(await Journal.GetAllAsync()).LastObservedState);
        await Assert.ThrowsAsync<ConfigurationConflictException>(async () =>
            await engine.ApplyAsync(tweak, await engine.PreviewAsync(tweak, 60)));
        Assert.Equal(OperationStatus.Restored,
            (await engine.RestoreAsync(tweak, applied.OperationId!.Value)).Status);
    }

    [Fact]
    public async Task RestoreActivationFailureCannotBeClosedByReadbackAloneOnRetry()
    {
        var tweak = new CompoundTweak();
        tweak.Definition = tweak.Definition with { RequiresSuccessfulWrite = true };
        var engine = new TweakEngine(Journal);
        var applied = await engine.ApplyAsync(tweak, await engine.PreviewAsync(tweak, 60));
        tweak.FailRestoreActivation = true;
        Assert.Equal(OperationStatus.RecoveryRequired,
            (await engine.RestoreAsync(tweak, applied.OperationId!.Value)).Status);
        Assert.Equal(new CompoundState(10, 20, "adapter-a"), tweak.State);
        Assert.Equal(OperationStatus.RecoveryRequired,
            (await new TweakEngine(Journal).RestoreAsync(tweak, applied.OperationId.Value)).Status);
        Assert.Equal(2, tweak.RestoreCalls);
        tweak.FailRestoreActivation = false;
        Assert.Equal(OperationStatus.Restored,
            (await engine.RestoreAsync(tweak, applied.OperationId.Value)).Status);
        Assert.Equal(3, tweak.RestoreCalls);
    }

    [Fact]
    public async Task OrdinaryAdaptersStillPermitVerifiedReadbackAfterAnAdapterError()
    {
        var tweak = new CompoundTweak { FailApplyActivation = true };
        var engine = new TweakEngine(Journal);
        var result = await engine.ApplyAsync(tweak, await engine.PreviewAsync(tweak, 60));
        Assert.Equal(OperationStatus.Verified, result.Status);
        Assert.Contains("verified despite", result.Message);
    }

    [Fact]
    public async Task RecordedActivationRequirementCannotBeWeakenedByANewerDefinition()
    {
        var tweak = new CompoundTweak();
        tweak.Definition = tweak.Definition with { RequiresSuccessfulWrite = true };
        var engine = new TweakEngine(Journal);
        var applied = await engine.ApplyAsync(tweak, await engine.PreviewAsync(tweak, 60));
        Assert.True(Assert.Single(await Journal.GetAllAsync()).RequiresSuccessfulWrite);
        tweak.Definition = tweak.Definition with { RequiresSuccessfulWrite = false };
        tweak.FailRestoreActivation = true;
        Assert.Equal(OperationStatus.RecoveryRequired,
            (await new TweakEngine(Journal).RestoreAsync(tweak, applied.OperationId!.Value)).Status);
        Assert.Equal(OperationStatus.RecoveryRequired,
            (await new TweakEngine(Journal).RestoreAsync(tweak, applied.OperationId.Value)).Status);
        Assert.Equal(2, tweak.RestoreCalls);
    }

    [Fact]
    public async Task PendingRestartIsNotLostWhenLaterMetadataChanges()
    {
        var tweak = new CompoundTweak();
        tweak.Definition = tweak.Definition with { RequiresRestart = true };
        var engine = new TweakEngine(Journal);
        await engine.ApplyAsync(tweak, await engine.PreviewAsync(tweak, 60));
        tweak.Definition = tweak.Definition with { RequiresRestart = false };
        var later = await engine.ApplyAsync(tweak, await engine.PreviewAsync(tweak, 70));
        Assert.Equal(OperationStatus.PendingRestart, later.Status);
        Assert.True(later.RestartRequired);
        Assert.True(Assert.Single(await Journal.GetAllAsync()).RestartRequired);
    }

    [Fact]
    public async Task ReviewedMetadataCannotChangeResourceOrPrivilegesBeforeApply()
    {
        var tweak = new CompoundTweak();
        var engine = new TweakEngine(Journal);
        var preview = await engine.PreviewAsync(tweak, 60);
        tweak.Definition = tweak.Definition with { ResourceId = "network/adapter-b" };
        await Assert.ThrowsAsync<ConfigurationConflictException>(async () => await engine.ApplyAsync(tweak, preview));
        Assert.Empty(await Journal.GetAllAsync());
        Assert.Equal(0, tweak.ApplyCalls);
    }

    [Fact]
    public async Task ProjectionChangingAfterReviewInvalidatesThePlan()
    {
        var tweak = new CompoundTweak();
        var engine = new TweakEngine(Journal);
        var preview = await engine.PreviewAsync(tweak, 60);
        tweak.ProjectionOffset = 5;
        await Assert.ThrowsAsync<ConfigurationConflictException>(async () => await engine.ApplyAsync(tweak, preview));
        Assert.Equal(0, tweak.ApplyCalls);
        Assert.Empty(await Journal.GetAllAsync());
    }

    [Fact]
    public async Task CorruptOriginalSnapshotBlocksFurtherApplyBeforeAnyWrite()
    {
        var tweak = new CompoundTweak();
        var engine = new TweakEngine(Journal);
        await engine.ApplyAsync(tweak, await engine.PreviewAsync(tweak, 60));
        var saved = Assert.Single(await Journal.GetAllAsync());
        await Journal.SaveAsync(saved with { OriginalState = JsonSerializer.SerializeToElement(new { }) });
        await Assert.ThrowsAsync<RecoveryDataException>(async () =>
            await engine.ApplyAsync(tweak, await engine.PreviewAsync(tweak, 70)));
        Assert.Equal(1, tweak.ApplyCalls);
        Assert.Equal(new CompoundState(60, 60, "adapter-a"), tweak.State);
    }

    [Fact]
    public async Task NullInNonnullableSnapshotFieldCannotReachRestoreAdapter()
    {
        var tweak = new CompoundTweak();
        var engine = new TweakEngine(Journal);
        var applied = await engine.ApplyAsync(tweak, await engine.PreviewAsync(tweak, 60));
        var saved = Assert.Single(await Journal.GetAllAsync());
        await Journal.SaveAsync(saved with
        {
            OriginalState = JsonSerializer.SerializeToElement(new { First = 10, Second = 20, AdapterId = (string?)null })
        });
        await Assert.ThrowsAsync<RecoveryDataException>(async () => await engine.RestoreAsync(tweak, applied.OperationId!.Value));
        Assert.Equal(0, tweak.RestoreCalls);
    }

    [Fact]
    public async Task CorruptExpectedStateCannotBeClosedAsANoOpRestore()
    {
        var tweak = new CompoundTweak();
        var engine = new TweakEngine(Journal);
        var applied = await engine.ApplyAsync(tweak, await engine.PreviewAsync(tweak, 60));
        var saved = Assert.Single(await Journal.GetAllAsync());
        tweak.State = new(10, 20, "adapter-a");
        await Journal.SaveAsync(saved with { ExpectedState = JsonSerializer.SerializeToElement(new { }) });
        await Assert.ThrowsAsync<RecoveryDataException>(async () => await engine.RestoreAsync(tweak, applied.OperationId!.Value));
        Assert.Equal(0, tweak.RestoreCalls);
        Assert.Equal(OperationStatus.Verified, Assert.Single(await Journal.GetAllAsync()).Status);
    }

    [Fact]
    public async Task ReviewedRestoreCannotUseAKnownButUnreviewedPriorState()
    {
        var tweak = new CompoundTweak();
        var engine = new TweakEngine(Journal);
        await engine.ApplyAsync(tweak, await engine.PreviewAsync(tweak, 60));
        var applied = await engine.ApplyAsync(tweak, await engine.PreviewAsync(tweak, 70));
        var reviewed = JsonSerializer.SerializeToElement(tweak.State);
        var original = Assert.Single(await Journal.GetAllAsync()).OriginalState;
        tweak.State = new(60, 60, "adapter-a");
        await Assert.ThrowsAsync<ConfigurationConflictException>(async () =>
            await engine.RestoreReviewedAsync(tweak, applied.OperationId!.Value, reviewed, original));
        Assert.Equal(0, tweak.RestoreCalls);
        Assert.Equal(new CompoundState(60, 60, "adapter-a"), tweak.State);
    }

    [Fact]
    public async Task ReviewedRestoreCannotCloseAnUnreviewedNoOp()
    {
        var tweak = new CompoundTweak();
        var engine = new TweakEngine(Journal);
        var applied = await engine.ApplyAsync(tweak, await engine.PreviewAsync(tweak, 60));
        var reviewed = JsonSerializer.SerializeToElement(tweak.State);
        tweak.State = new(10, 20, "adapter-a");
        await Assert.ThrowsAsync<ConfigurationConflictException>(async () =>
            await engine.RestoreReviewedAsync(tweak, applied.OperationId!.Value, reviewed));
        Assert.Equal(OperationStatus.Verified, Assert.Single(await Journal.GetAllAsync()).Status);
        Assert.Equal(0, tweak.RestoreCalls);
    }

    [Fact]
    public async Task ReviewedRestoreBindsTheOriginalEvenIfJournalChangesBeforeExecution()
    {
        var tweak = new CompoundTweak();
        var engine = new TweakEngine(Journal);
        var applied = await engine.ApplyAsync(tweak, await engine.PreviewAsync(tweak, 60));
        var reviewed = JsonSerializer.SerializeToElement(tweak.State);
        var saved = Assert.Single(await Journal.GetAllAsync());
        var reviewedExpected = saved.OriginalState;
        await Journal.SaveAsync(saved with { OriginalState = JsonSerializer.SerializeToElement(new CompoundState(11, 22, "adapter-a")) });
        await Assert.ThrowsAsync<ConfigurationConflictException>(async () =>
            await engine.RestoreReviewedAsync(tweak, applied.OperationId!.Value, reviewed, reviewedExpected));
        Assert.Equal(0, tweak.RestoreCalls);
        Assert.Equal(new CompoundState(60, 60, "adapter-a"), tweak.State);
    }

    [Fact]
    public async Task ReviewedRestoreAllowsProjectedReadOnlyContextWhileBindingTheEffectiveOriginal()
    {
        var tweak = new ContextTweak();
        var engine = new TweakEngine(Journal);
        var applied = await engine.ApplyAsync(tweak, await engine.PreviewAsync(tweak, 60));
        tweak.State = tweak.State with { ReadOnlyContext = 90 };
        var before = JsonSerializer.SerializeToElement(tweak.State);
        var expected = JsonSerializer.SerializeToElement(tweak.State with { Value = 10 });
        Assert.Equal(OperationStatus.Restored,
            (await engine.RestoreReviewedAsync(tweak, applied.OperationId!.Value, before, expected)).Status);
        Assert.Equal(new ContextState(10, 90), tweak.State);
    }

    [Fact]
    public async Task ReviewedRestoreStillBlocksContextChangesEvenWhenAdapterEqualityIgnoresThem()
    {
        var tweak = new ContextTweak();
        var engine = new TweakEngine(Journal);
        var applied = await engine.ApplyAsync(tweak, await engine.PreviewAsync(tweak, 60));
        var before = JsonSerializer.SerializeToElement(tweak.State);
        tweak.State = tweak.State with { ReadOnlyContext = 90 };
        await Assert.ThrowsAsync<ConfigurationConflictException>(async () =>
            await engine.RestoreReviewedAsync(tweak, applied.OperationId!.Value, before));
        Assert.Equal(0, tweak.RestoreCalls);
        Assert.Equal(new ContextState(60, 90), tweak.State);
    }

    [Theory]
    [InlineData(OperationStatus.Available)]
    [InlineData(OperationStatus.Unsupported)]
    [InlineData(OperationStatus.AlreadyApplied)]
    [InlineData(OperationStatus.RestoreAvailable)]
    public async Task DisplayOnlyStatusesCannotBecomeResourceOwners(OperationStatus status)
    {
        var tweak = new CompoundTweak();
        var engine = new TweakEngine(Journal);
        await engine.ApplyAsync(tweak, await engine.PreviewAsync(tweak, 60));
        var saved = Assert.Single(await Journal.GetAllAsync());
        await Assert.ThrowsAsync<RecoveryDataException>(async () => await Journal.SaveAsync(saved with { Status = status }));
        Assert.Equal(OperationStatus.Verified, Assert.Single(await Journal.GetAllAsync()).Status);
    }

    [Fact]
    public async Task ReviewedStateCanBeRoundTrippedWithoutExposingMutableAdapterState()
    {
        var tweak = new CompoundTweak();
        var engine = new TweakEngine(Journal);
        var preview = await engine.PreviewAsync(tweak, 60);
        var ticket = JsonSerializer.Serialize(new { preview.TweakId, preview.BeforeState, preview.ExpectedState, preview.CreatedAt });
        using var parsed = JsonDocument.Parse(ticket);
        Assert.True(JsonElement.DeepEquals(preview.BeforeState, parsed.RootElement.GetProperty("BeforeState")));
        Assert.Equal(10, preview.BeforeState.GetProperty("First").GetInt32());
        Assert.Equal(60, preview.ExpectedState.GetProperty("First").GetInt32());
        tweak.State = new(33, 44, "adapter-a");
        Assert.Equal(10, preview.BeforeState.GetProperty("First").GetInt32());
        await Assert.ThrowsAsync<ConfigurationConflictException>(async () => await engine.ApplyAsync(tweak, preview));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }

    public sealed record CompoundState(int First, int Second, string AdapterId);

    public sealed record ContextState(int Value, int ReadOnlyContext);
    private sealed class ContextTweak : ITweak<ContextState, int>
    {
        public TweakDefinition Definition { get; } = new("context.setting", "context/resource", "Context setting", "Test only",
            "Tests", RiskClass.Advanced, PrivilegeRequirement.StandardUser, ["test"], [], ["0–100"], [], [], "No system effects.");
        public ContextState State { get; set; } = new(10, 100);
        public int RestoreCalls { get; private set; }
        public ValueTask<Compatibility> CheckCompatibilityAsync(CancellationToken cancellationToken) => ValueTask.FromResult(Compatibility.Supported());
        public ValueTask<ContextState> ReadAsync(CancellationToken cancellationToken) => ValueTask.FromResult(State);
        public IReadOnlyList<string> Validate(int requestedValue) => requestedValue is >= 0 and <= 100 ? [] : ["Out of range."];
        public ContextState Project(ContextState currentState, int requestedValue) => currentState with { Value = requestedValue };
        public bool StatesEqual(ContextState left, ContextState right) => left.Value == right.Value;
        public string DescribeChange(ContextState currentState, ContextState expectedState) => $"{currentState.Value} → {expectedState.Value}";
        public ValueTask ApplyAsync(int requestedValue, CancellationToken cancellationToken)
        { State = State with { Value = requestedValue }; return ValueTask.CompletedTask; }
        public ValueTask RestoreAsync(ContextState originalState, CancellationToken cancellationToken)
        { RestoreCalls++; State = State with { Value = originalState.Value }; return ValueTask.CompletedTask; }
    }

    private sealed class CompoundTweak : ITweak<CompoundState, int>
    {
        public TweakDefinition Definition { get; set; } = new("network.compound", "network/adapter-a", "Compound test operation",
            "Two-field adapter simulation", "Network", RiskClass.Advanced, PrivilegeRequirement.StandardUser,
            ["test"], [], ["0–100"], [], [], "No real system impact.");
        public CompoundState State { get; set; } = new(10, 20, "adapter-a");
        public bool PartiallyApply { get; set; }
        public bool PartiallyRestore { get; set; }
        public bool FailApplyActivation { get; set; }
        public bool FailRestoreActivation { get; set; }
        public int ProjectionOffset { get; set; }
        public int ApplyCalls { get; private set; }
        public int RestoreCalls { get; private set; }
        public ValueTask<Compatibility> CheckCompatibilityAsync(CancellationToken cancellationToken) => ValueTask.FromResult(Compatibility.Supported());
        public ValueTask<CompoundState> ReadAsync(CancellationToken cancellationToken) => ValueTask.FromResult(State);
        public IReadOnlyList<string> Validate(int requestedValue) => requestedValue is >= 0 and <= 100 ? [] : ["Out of range."];
        public CompoundState Project(CompoundState currentState, int requestedValue)
            => currentState with { First = requestedValue + ProjectionOffset, Second = requestedValue + ProjectionOffset };
        public bool StatesEqual(CompoundState left, CompoundState right) => left == right;
        public string DescribeChange(CompoundState currentState, CompoundState expectedState) => $"{currentState} → {expectedState}";
        public ValueTask ApplyAsync(int requestedValue, CancellationToken cancellationToken)
        {
            ApplyCalls++;
            State = State with { First = requestedValue };
            if (PartiallyApply) throw new IOException("Second field write failed.");
            State = State with { Second = requestedValue };
            if (FailApplyActivation) throw new IOException("Simulated activation error.");
            return ValueTask.CompletedTask;
        }
        public ValueTask RestoreAsync(CompoundState originalState, CancellationToken cancellationToken)
        {
            RestoreCalls++;
            State = State with { First = originalState.First };
            if (PartiallyRestore) throw new IOException("Second field restore failed.");
            State = originalState;
            if (FailRestoreActivation) throw new IOException("Simulated activation error.");
            return ValueTask.CompletedTask;
        }
    }
}
