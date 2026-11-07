using Carpatia.Ultimate.Core;
using Carpatia.Ultimate.Engine;
using Xunit;

namespace Carpatia.Ultimate.Tests;

public sealed class EngineTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "carpatia-tests", Guid.NewGuid().ToString("N"));
    private FileOperationJournal Journal => new(_directory);

    [Fact]
    public async Task ApplyPersistsOriginalBeforeWritingAndVerifiesActualState()
    {
        var journal = Journal;
        var tweak = new MemoryTweak();
        tweak.BeforeApply = async () =>
        {
            var saved = Assert.Single(await journal.GetAllAsync());
            Assert.Equal(OperationStatus.Applying, saved.Status);
            Assert.Equal(20, saved.OriginalState.GetProperty("Value").GetInt32());
        };
        var engine = new TweakEngine(journal);
        var result = await engine.ApplyAsync(tweak, await engine.PreviewAsync(tweak, 80));
        Assert.Equal(OperationStatus.Verified, result.Status);
        Assert.Equal(80, tweak.State.Value);
        Assert.Equal(OperationStatus.Verified, Assert.Single(await journal.GetAllAsync()).Status);
    }

    [Fact]
    public async Task RepeatedApplyPreservesFirstOriginalAndRepeatedRestoreDoesNotOverwriteExternalState()
    {
        var tweak = new MemoryTweak();
        var engine = new TweakEngine(Journal);
        var first = await engine.ApplyAsync(tweak, await engine.PreviewAsync(tweak, 70));
        var repeated = await engine.ApplyAsync(tweak, await engine.PreviewAsync(tweak, 70));
        Assert.Equal(OperationStatus.AlreadyApplied, repeated.Status);
        var changed = await engine.ApplyAsync(tweak, await engine.PreviewAsync(tweak, 90));
        Assert.Equal(first.OperationId, changed.OperationId);
        Assert.Equal(20, Assert.Single(await Journal.GetAllAsync()).OriginalState.GetProperty("Value").GetInt32());
        Assert.Equal(OperationStatus.Restored, (await engine.RestoreAsync(tweak, first.OperationId!.Value)).Status);
        Assert.Equal(20, tweak.State.Value);
        tweak.State = new(44);
        Assert.Equal(OperationStatus.Restored, (await engine.RestoreAsync(tweak, first.OperationId.Value)).Status);
        Assert.Equal(44, tweak.State.Value);
        Assert.Equal(1, tweak.RestoreCalls);
    }

    [Fact]
    public async Task NoOpHasNoBackupAndDoesNotWrite()
    {
        var tweak = new MemoryTweak();
        var engine = new TweakEngine(Journal);
        var result = await engine.ApplyAsync(tweak, await engine.PreviewAsync(tweak, 20));
        Assert.Equal(OperationStatus.AlreadyApplied, result.Status);
        Assert.Null(result.OperationId);
        Assert.Empty(await Journal.GetAllAsync());
        Assert.Equal(0, tweak.ApplyCalls);
    }

    [Fact]
    public async Task UnsupportedHardwareAndInvalidValuesAreRejected()
    {
        var tweak = new MemoryTweak { Supported = false };
        var engine = new TweakEngine(Journal);
        await Assert.ThrowsAsync<TweakUnavailableException>(async () => await engine.PreviewAsync(tweak, 50));
        tweak.Supported = true;
        await Assert.ThrowsAsync<ArgumentException>(async () => await engine.PreviewAsync(tweak, 101));
        Assert.Equal(0, tweak.ApplyCalls);
    }

    [Fact]
    public async Task CompatibilityIsRecheckedAfterPreview()
    {
        var tweak = new MemoryTweak();
        var engine = new TweakEngine(Journal);
        var preview = await engine.PreviewAsync(tweak, 50);
        tweak.Supported = false;
        await Assert.ThrowsAsync<TweakUnavailableException>(async () => await engine.ApplyAsync(tweak, preview));
        Assert.Empty(await Journal.GetAllAsync());
    }

    [Fact]
    public async Task MissingAdministratorRightsAndUnacknowledgedRiskAreRejected()
    {
        var tweak = new MemoryTweak();
        tweak.Definition = tweak.Definition with { Privilege = PrivilegeRequirement.Administrator };
        var engine = new TweakEngine(Journal);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
            await engine.ApplyAsync(tweak, await engine.PreviewAsync(tweak, 50)));
        tweak.Definition = tweak.Definition with { Privilege = PrivilegeRequirement.StandardUser, Risk = RiskClass.Extreme };
        await Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
            await engine.ApplyAsync(tweak, await engine.PreviewAsync(tweak, 50)));
        Assert.Empty(await Journal.GetAllAsync());
    }

    [Fact]
    public async Task ExternalChangesBlockApplyAndRestore()
    {
        var tweak = new MemoryTweak();
        var engine = new TweakEngine(Journal);
        var result = await engine.ApplyAsync(tweak, await engine.PreviewAsync(tweak, 50));
        tweak.State = new(61);
        await Assert.ThrowsAsync<ConfigurationConflictException>(async () =>
            await engine.RestoreAsync(tweak, result.OperationId!.Value));
        await Assert.ThrowsAsync<ConfigurationConflictException>(async () =>
            await engine.ApplyAsync(tweak, await engine.PreviewAsync(tweak, 70)));
        Assert.Equal(61, tweak.State.Value);
    }

    [Fact]
    public async Task StaleAndWrongAdapterPreviewsAreRejected()
    {
        var tweak = new MemoryTweak();
        var engine = new TweakEngine(Journal);
        var preview = await engine.PreviewAsync(tweak, 50);
        await Assert.ThrowsAsync<ArgumentException>(async () => await engine.ApplyAsync(new MemoryTweak(), preview));
        tweak.State = new(30);
        await Assert.ThrowsAsync<ConfigurationConflictException>(async () => await engine.ApplyAsync(tweak, preview));
        Assert.Equal(0, tweak.ApplyCalls);
    }

    [Fact]
    public async Task AdapterSuccessWithoutStateChangeIsFailure()
    {
        var tweak = new MemoryTweak { IgnoreWrites = true };
        var engine = new TweakEngine(Journal);
        var result = await engine.ApplyAsync(tweak, await engine.PreviewAsync(tweak, 80));
        Assert.Equal(OperationStatus.Failed, result.Status);
        Assert.Equal(20, tweak.State.Value);
        Assert.Equal(OperationStatus.Restored, (await engine.RestoreAsync(tweak, result.OperationId!.Value)).Status);
    }

    [Fact]
    public async Task PartialWriteCanRestoreOnlyTheExactObservedPartialState()
    {
        var tweak = new MemoryTweak { PartialValue = 45 };
        var engine = new TweakEngine(Journal);
        var result = await engine.ApplyAsync(tweak, await engine.PreviewAsync(tweak, 80));
        Assert.Equal(OperationStatus.PartiallyApplied, result.Status);
        var record = Assert.Single(await Journal.GetAllAsync());
        Assert.Equal(45, record.LastObservedState!.Value.GetProperty("Value").GetInt32());
        tweak.State = new(46); // A later external edit is not owned by the operation.
        await Assert.ThrowsAsync<ConfigurationConflictException>(async () =>
            await engine.RestoreAsync(tweak, result.OperationId!.Value));
        Assert.Equal(46, tweak.State.Value);
        Assert.Equal(0, tweak.RestoreCalls);
        tweak.State = new(45);
        Assert.Equal(OperationStatus.Restored, (await engine.RestoreAsync(tweak, result.OperationId!.Value)).Status);
        Assert.Equal(20, tweak.State.Value);
    }

    [Fact]
    public async Task FailedReadbackKeepsRecoveryRecord()
    {
        var tweak = new MemoryTweak { FailReadAfterWrite = true };
        var engine = new TweakEngine(Journal);
        var result = await engine.ApplyAsync(tweak, await engine.PreviewAsync(tweak, 80));
        Assert.Equal(OperationStatus.RecoveryRequired, result.Status);
        tweak.FailReadAfterWrite = false;
        Assert.Equal(OperationStatus.Restored, (await engine.RestoreAsync(tweak, result.OperationId!.Value)).Status);
        Assert.Equal(20, tweak.State.Value);
    }

    [Fact]
    public async Task JournalFailureBeforeWritePreventsMutation()
    {
        var tweak = new MemoryTweak();
        var engine = new TweakEngine(new FailingJournal(Journal, failOnSave: 1));
        await Assert.ThrowsAsync<IOException>(async () =>
            await engine.ApplyAsync(tweak, await engine.PreviewAsync(tweak, 80)));
        Assert.Equal(0, tweak.ApplyCalls);
    }

    [Fact]
    public async Task InterruptedResultRecordingIsRecoverableFromPersistedOriginal()
    {
        var tweak = new MemoryTweak();
        var engine = new TweakEngine(new FailingJournal(Journal, failOnSave: 2));
        await Assert.ThrowsAsync<IOException>(async () =>
            await engine.ApplyAsync(tweak, await engine.PreviewAsync(tweak, 80)));
        var record = Assert.Single(await Journal.GetAllAsync());
        Assert.Equal(OperationStatus.Applying, record.Status);
        var restarted = new TweakEngine(Journal);
        await Assert.ThrowsAsync<ConfigurationConflictException>(async () =>
            await restarted.ApplyAsync(tweak, await restarted.PreviewAsync(tweak, 90)));
        Assert.Equal(OperationStatus.Restored, (await restarted.RestoreAsync(tweak, record.Id)).Status);
        Assert.Equal(20, tweak.State.Value);
    }

    [Fact]
    public async Task FailedRestoreIsNeverReportedAsRestored()
    {
        var tweak = new MemoryTweak();
        var engine = new TweakEngine(Journal);
        var result = await engine.ApplyAsync(tweak, await engine.PreviewAsync(tweak, 80));
        tweak.IgnoreWrites = true;
        Assert.Equal(OperationStatus.RecoveryRequired, (await engine.RestoreAsync(tweak, result.OperationId!.Value)).Status);
        tweak.IgnoreWrites = false;
        Assert.Equal(OperationStatus.Restored, (await engine.RestoreAsync(tweak, result.OperationId.Value)).Status);
    }

    [Fact]
    public async Task RestartRequirementIsTrackedAfterReadback()
    {
        var tweak = new MemoryTweak();
        tweak.Definition = tweak.Definition with { RequiresRestart = true };
        var engine = new TweakEngine(Journal);
        var result = await engine.ApplyAsync(tweak, await engine.PreviewAsync(tweak, 80));
        Assert.Equal(OperationStatus.PendingRestart, result.Status);
        Assert.True(result.RestartRequired);
        var restored = await engine.RestoreAsync(tweak, result.OperationId!.Value);
        Assert.Equal(OperationStatus.Restored, restored.Status);
        Assert.True(restored.RestartRequired);
        Assert.Contains("Restart Windows", restored.Message);
    }

    [Fact]
    public async Task ConflictsAreSymmetricAndPersistAcrossInstances()
    {
        var first = new MemoryTweak();
        first.Definition = first.Definition with { ConflictsWith = ["other"] };
        var second = new MemoryTweak();
        second.Definition = second.Definition with { Id = "other", ResourceId = "resource/other" };
        var engine = new TweakEngine(Journal);
        var preview = await engine.PreviewAsync(second, 55);
        await engine.ApplyAsync(first, await engine.PreviewAsync(first, 80));
        var restarted = new TweakEngine(Journal);
        await Assert.ThrowsAsync<ConfigurationConflictException>(async () => await restarted.ApplyAsync(second, preview));
        Assert.Equal(0, second.ApplyCalls);
    }

    [Fact]
    public async Task ConcurrentInstancesSerializeAndRejectStaleSecondPlan()
    {
        var tweak = new MemoryTweak();
        var first = new TweakEngine(Journal);
        var second = new TweakEngine(Journal);
        var one = await first.PreviewAsync(tweak, 70);
        var two = await second.PreviewAsync(tweak, 90);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        tweak.BeforeApply = async () => { entered.SetResult(); await release.Task; };
        var firstApply = first.ApplyAsync(tweak, one).AsTask();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var secondApply = second.ApplyAsync(tweak, two).AsTask();
        await Task.Delay(80);
        Assert.False(secondApply.IsCompleted);
        release.SetResult();
        Assert.Equal(OperationStatus.Verified, (await firstApply).Status);
        await Assert.ThrowsAsync<ConfigurationConflictException>(async () => await secondApply);
        Assert.Equal(1, tweak.ApplyCalls);
    }

    [Fact]
    public async Task CancellationAfterWriteStillVerifiesAndRecordsResult()
    {
        using var cancellation = new CancellationTokenSource();
        var tweak = new MemoryTweak { AfterApply = cancellation.Cancel };
        var engine = new TweakEngine(Journal);
        var preview = await engine.PreviewAsync(tweak, 80);
        var result = await engine.ApplyAsync(tweak, preview, cancellationToken: cancellation.Token);
        Assert.Equal(OperationStatus.Verified, result.Status);
        Assert.Equal(OperationStatus.Verified, Assert.Single(await Journal.GetAllAsync()).Status);
    }

    [Fact]
    public async Task CorruptedJournalFailsClosedWithoutChanges()
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(Path.Combine(_directory, $"operation-{Guid.NewGuid():N}.json"), "{broken");
        var tweak = new MemoryTweak();
        var engine = new TweakEngine(Journal);
        await Assert.ThrowsAsync<RecoveryDataException>(async () => await engine.PreviewAsync(tweak, 80));
        Assert.Equal(0, tweak.ApplyCalls);
    }

    [Fact]
    public async Task UnknownJournalSchemaBlocksFurtherExecution()
    {
        var tweak = new MemoryTweak();
        var engine = new TweakEngine(Journal);
        await engine.ApplyAsync(tweak, await engine.PreviewAsync(tweak, 80));
        var path = Assert.Single(Directory.GetFiles(_directory, "operation-*.json"));
        var json = await File.ReadAllTextAsync(path);
        await File.WriteAllTextAsync(path, json.Replace("\"SchemaVersion\": 1", "\"SchemaVersion\": 999", StringComparison.Ordinal));
        await Assert.ThrowsAsync<RecoveryDataException>(async () => await engine.PreviewAsync(tweak, 90));
        Assert.Equal(1, tweak.ApplyCalls);
    }

    [Fact]
    public async Task MissingTypedSnapshotPropertiesCannotSilentlyDefaultDuringRestore()
    {
        var tweak = new MemoryTweak();
        var engine = new TweakEngine(Journal);
        var result = await engine.ApplyAsync(tweak, await engine.PreviewAsync(tweak, 80));
        var record = Assert.Single(await Journal.GetAllAsync());
        await Journal.SaveAsync(record with { OriginalState = System.Text.Json.JsonSerializer.SerializeToElement(new { }) });
        await Assert.ThrowsAsync<RecoveryDataException>(async () => await engine.RestoreAsync(tweak, result.OperationId!.Value));
        Assert.Equal(80, tweak.State.Value);
        Assert.Equal(0, tweak.RestoreCalls);
    }

    [Fact]
    public async Task InterruptedBeforeSystemWriteCanBeClosedWithoutRewritingOriginalState()
    {
        var tweak = new MemoryTweak();
        var engine = new TweakEngine(new FailingJournal(Journal, failOnSave: 2));
        await Assert.ThrowsAsync<IOException>(async () =>
            await engine.ApplyAsync(tweak, await engine.PreviewAsync(tweak, 80)));
        // The same durable Applying record is present if the process died before calling the adapter.
        tweak.State = new(20);
        var record = Assert.Single(await Journal.GetAllAsync());
        var restarted = new TweakEngine(Journal);
        Assert.Equal(OperationStatus.Restored, (await restarted.RestoreAsync(tweak, record.Id)).Status);
        Assert.Equal(0, tweak.RestoreCalls);
    }

    [Fact]
    public async Task CancellationWhileWaitingForAResourceLeaseDoesNotReleaseItsOwner()
    {
        await using var owner = await Journal.AcquireResourceLeaseAsync("resource/test");
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await Journal.AcquireResourceLeaseAsync("resource/test", cancellation.Token));
        using var stillLocked = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await Journal.AcquireResourceLeaseAsync("resource/test", stillLocked.Token));
    }

    [Fact]
    public async Task DifferentTweakCannotReplaceAnotherTweaksOriginalForTheSameResource()
    {
        var first = new MemoryTweak();
        var second = new MemoryTweak();
        second.Definition = second.Definition with { Id = "other.setting" };
        var engine = new TweakEngine(Journal);
        await engine.ApplyAsync(first, await engine.PreviewAsync(first, 80));
        await Assert.ThrowsAsync<ConfigurationConflictException>(async () =>
            await engine.ApplyAsync(second, await engine.PreviewAsync(second, 70)));
        Assert.Equal("test.setting", Assert.Single(await Journal.GetAllAsync()).TweakId);
        Assert.Equal(0, second.ApplyCalls);
    }

    [Fact]
    public void ProfilesCannotReferToMissingOrAutomaticallyIncludeExperimentalTweaks()
    {
        var tweak = new MemoryTweak();
        tweak.Definition = tweak.Definition with { Risk = RiskClass.Experimental };
        var profile = new OptimizationProfile(ProfileKind.Ultra, "Test", [new("test.setting", "80"), new("missing", "1")]);
        Assert.Equal(2, profile.ValidateAgainst([tweak.Definition]).Count);
        Assert.Empty(new OptimizationProfile(ProfileKind.Balanced, "No operations in Phase A", []).ValidateAgainst([]));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    public sealed record SettingState(int Value);

    private sealed class MemoryTweak : ITweak<SettingState, int>
    {
        public TweakDefinition Definition { get; set; } = new("test.setting", "resource/test", "Test setting",
            "In-memory adapter only", "Tests", RiskClass.Advanced, PrivilegeRequirement.StandardUser,
            ["Test host"], [], ["0–100"], [], [], "No performance implication in a test adapter.");
        public SettingState State { get; set; } = new(20);
        public bool Supported { get; set; } = true;
        public bool IgnoreWrites { get; set; }
        public bool FailReadAfterWrite { get; set; }
        public int? PartialValue { get; set; }
        public int ApplyCalls { get; private set; }
        public int RestoreCalls { get; private set; }
        public Func<Task>? BeforeApply { get; set; }
        public Action? AfterApply { get; set; }
        public ValueTask<Compatibility> CheckCompatibilityAsync(CancellationToken cancellationToken)
            => ValueTask.FromResult(Supported ? Compatibility.Supported() : Compatibility.Unsupported("Hardware unavailable."));
        public ValueTask<SettingState> ReadAsync(CancellationToken cancellationToken)
        {
            if (FailReadAfterWrite && ApplyCalls > 0) throw new IOException("Test read failure.");
            return ValueTask.FromResult(State);
        }
        public IReadOnlyList<string> Validate(int requestedValue) => requestedValue is >= 0 and <= 100 ? [] : ["Value must be 0–100."];
        public SettingState Project(SettingState currentState, int requestedValue) => new(requestedValue);
        public bool StatesEqual(SettingState left, SettingState right) => left == right;
        public string DescribeChange(SettingState currentState, SettingState expectedState) => $"{currentState.Value} → {expectedState.Value}";
        public async ValueTask ApplyAsync(int requestedValue, CancellationToken cancellationToken)
        {
            if (BeforeApply is not null) await BeforeApply();
            ApplyCalls++;
            if (!IgnoreWrites) State = new(PartialValue ?? requestedValue);
            AfterApply?.Invoke();
            if (PartialValue is not null) throw new IOException("Test partial write failure.");
        }
        public ValueTask RestoreAsync(SettingState originalState, CancellationToken cancellationToken)
        {
            RestoreCalls++;
            if (!IgnoreWrites) State = originalState;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FailingJournal(IOperationJournal inner, int failOnSave) : IOperationJournal
    {
        private int _calls;
        public ValueTask<IReadOnlyList<OperationRecord>> GetAllAsync(CancellationToken cancellationToken = default) => inner.GetAllAsync(cancellationToken);
        public ValueTask<IAsyncDisposable> AcquireExecutionLeaseAsync(CancellationToken cancellationToken = default) => inner.AcquireExecutionLeaseAsync(cancellationToken);
        public ValueTask<IAsyncDisposable> AcquireResourceLeaseAsync(string resourceId, CancellationToken cancellationToken = default) => inner.AcquireResourceLeaseAsync(resourceId, cancellationToken);
        public ValueTask SaveAsync(OperationRecord record, CancellationToken cancellationToken = default)
        {
            if (++_calls == failOnSave) throw new IOException("Simulated disk write failure.");
            return inner.SaveAsync(record, cancellationToken);
        }
    }
}
