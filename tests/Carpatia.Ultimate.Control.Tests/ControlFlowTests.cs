using System.Text.Json;
using Carpatia.Ultimate.Broker;
using Carpatia.Ultimate.Control;
using Carpatia.Ultimate.Core;
using Carpatia.Ultimate.Engine;
using Xunit;

namespace Carpatia.Ultimate.Control.Tests;

/// <summary>Real control/engine/journal flow with isolated state; no live Windows APIs or elevation.</summary>
public sealed class ControlFlowTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "carpatia-control", Guid.NewGuid().ToString("N"));
    private readonly Guid _target = Guid.NewGuid();
    private readonly FakeSystem _system = new();
    private FileOperationJournal Journal => new(_directory);

    [Fact]
    public async Task StandardUserCanPreviewWithoutConfirmationOrMutation()
    {
        var response = await Dispatcher(false).ExecuteAsync(Request(ControlAction.Preview));
        Assert.Equal(OperationStatus.Available, response.Status);
        Assert.Equal(20u, response.Preview!.Before.GetProperty("Value").GetUInt32());
        Assert.Equal(60u, response.Preview.Expected.GetProperty("Value").GetUInt32());
        Assert.Equal(0, _system.ApplyCalls);
        Assert.Empty(await Journal.GetAllAsync());
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    public async Task ApplyRequiresBothAdministratorContextAndConfirmation(bool administrator, bool provideConfirmation)
    {
        var request = await ReviewedApplyAsync();
        var confirmations = 0;
        Func<ControlPreview, Task<bool>>? confirm = provideConfirmation ? _ => { confirmations++; return Task.FromResult(true); } : null;
        var response = await Dispatcher(administrator).ExecuteAsync(request, confirm);
        Assert.Equal(OperationStatus.Failed, response.Status);
        Assert.Contains("elevated helper", response.Message);
        Assert.Equal(0, confirmations);
        Assert.Equal(0, _system.ApplyCalls);
        Assert.Empty(await Journal.GetAllAsync());
    }

    [Fact]
    public async Task ApplyUsesFreshReadbackAndRecordsOriginalOnlyAfterConfirmation()
    {
        var request = await ReviewedApplyAsync();
        var confirmed = false;
        var response = await Dispatcher(true).ExecuteAsync(request, async preview =>
        {
            Assert.Equal(0, _system.ApplyCalls);
            Assert.Empty(await Journal.GetAllAsync());
            Assert.True(JsonElement.DeepEquals(request.ReviewedPreview!.Before, preview.Before));
            Assert.True(JsonElement.DeepEquals(request.ReviewedPreview.Expected, preview.Expected));
            confirmed = true;
            return true;
        });
        Assert.True(confirmed);
        Assert.Equal(OperationStatus.Verified, response.Status);
        Assert.Equal(60u, _system.Value);
        Assert.Equal(1, _system.ApplyCalls);
        Assert.Equal(20u, Assert.Single(await Journal.GetAllAsync()).OriginalState.GetProperty("Value").GetUInt32());
    }

    [Fact]
    public async Task DenyingFreshConfirmationLeavesMachineAndJournalUntouched()
    {
        var response = await Dispatcher(true).ExecuteAsync(await ReviewedApplyAsync(), _ => Task.FromResult(false));
        Assert.True(response.Cancelled);
        Assert.Equal(OperationStatus.Available, response.Status);
        Assert.Equal(20u, _system.Value);
        Assert.Empty(await Journal.GetAllAsync());
    }

    [Fact]
    public async Task ChangedStateInvalidatesReviewedApplyBeforeHelperConfirmation()
    {
        var request = await ReviewedApplyAsync();
        _system.Value = 35;
        var confirmations = 0;
        var response = await Dispatcher(true).ExecuteAsync(request, _ => { confirmations++; return Task.FromResult(true); });
        Assert.Equal(OperationStatus.Failed, response.Status);
        Assert.Contains("reviewed plan", response.Message);
        Assert.Equal(0, confirmations);
        Assert.Equal(0, _system.ApplyCalls);
        Assert.Equal(35u, _system.Value);
    }

    [Fact]
    public async Task StateChangedDuringApplyConfirmationIsRejectedByBoundEnginePlan()
    {
        var response = await Dispatcher(true).ExecuteAsync(await ReviewedApplyAsync(), _ =>
        {
            _system.Value = 35;
            return Task.FromResult(true);
        });
        Assert.Equal(OperationStatus.Failed, response.Status);
        Assert.Equal(0, _system.ApplyCalls);
        Assert.Equal(35u, _system.Value);
        Assert.Empty(await Journal.GetAllAsync());
    }

    [Theory]
    [InlineData("expired")]
    [InlineData("future")]
    [InlineData("before")]
    [InlineData("expected")]
    [InlineData("resource")]
    [InlineData("description")]
    public async Task ForgedOrExpiredReviewCannotReachConfirmationOrWrite(string changed)
    {
        var request = await ReviewedApplyAsync();
        var reviewed = request.ReviewedPreview!;
        reviewed = changed switch
        {
            "expired" => reviewed with { CapturedAt = DateTimeOffset.UtcNow.AddMinutes(-16) },
            "future" => reviewed with { CapturedAt = DateTimeOffset.UtcNow.AddMinutes(1) },
            "before" => reviewed with { Before = JsonSerializer.SerializeToElement(new FakeState(_target, 21)) },
            "expected" => reviewed with { Expected = JsonSerializer.SerializeToElement(new FakeState(_target, 61)) },
            "resource" => reviewed with { ResourceId = "different-resource" },
            "description" => reviewed with { Description = "Approve an unrelated setting" },
            _ => throw new ArgumentOutOfRangeException(nameof(changed))
        };
        var confirmations = 0;
        var response = await Dispatcher(true).ExecuteAsync(request with { ReviewedPreview = reviewed },
            _ => { confirmations++; return Task.FromResult(true); });
        Assert.Equal(OperationStatus.Failed, response.Status);
        Assert.Equal(0, confirmations);
        Assert.Equal(0, _system.ApplyCalls);
    }

    [Fact]
    public async Task ReviewedTargetAndValueCannotBeChangedAfterReview()
    {
        var request = await ReviewedApplyAsync();
        var confirmations = 0;
        var dispatcher = Dispatcher(true);
        var wrongTarget = await dispatcher.ExecuteAsync(request with { SchemeId = Guid.NewGuid() },
            _ => { confirmations++; return Task.FromResult(true); });
        var wrongValue = await dispatcher.ExecuteAsync(request with { NumericValue = 70 },
            _ => { confirmations++; return Task.FromResult(true); });
        Assert.Equal(OperationStatus.Failed, wrongTarget.Status);
        Assert.Equal(OperationStatus.Failed, wrongValue.Status);
        Assert.Equal(0, confirmations);
        Assert.Equal(0, _system.ApplyCalls);
    }

    [Fact]
    public async Task ApplyingAnAlreadyPresentValueDoesNotCreateASnapshotOrWrite()
    {
        var request = await ReviewedApplyAsync(20);
        var confirmations = 0;
        var response = await Dispatcher(true).ExecuteAsync(request, _ => { confirmations++; return Task.FromResult(true); });
        Assert.Equal(OperationStatus.AlreadyApplied, response.Status);
        Assert.Equal(1, confirmations);
        Assert.Null(response.OperationId);
        Assert.Equal(0, _system.ApplyCalls);
        Assert.Empty(await Journal.GetAllAsync());
    }

    [Fact]
    public async Task RestorePreviewIsExplicitReadOnlyAndReconstructsItsTargetFromTheJournal()
    {
        var operation = await ApplyAsync();
        var dispatcher = Dispatcher(false);
        var restore = dispatcher.CreateRestoreRequest(Assert.Single(await Journal.GetAllAsync()));
        Assert.NotNull(restore);
        Assert.Equal(ControlAction.PreviewRestore, restore.Action);
        Assert.Null(restore.SchemeId);
        Assert.Null(restore.NumericValue);
        var preview = await dispatcher.ExecuteAsync(restore);
        Assert.Equal(OperationStatus.RestoreAvailable, preview.Status);
        Assert.Equal(operation.OperationId, preview.OperationId);
        Assert.Equal(60u, preview.Preview!.Before.GetProperty("Value").GetUInt32());
        Assert.Equal(20u, preview.Preview.Expected.GetProperty("Value").GetUInt32());
        Assert.Equal(0, _system.RestoreCalls);
        Assert.Equal(60u, _system.Value);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task RestoreRequiresElevationAndFreshConfirmation(bool administrator, bool provideConfirmation)
    {
        await ApplyAsync();
        var request = await ReviewedRestoreAsync();
        var confirmations = 0;
        Func<ControlPreview, Task<bool>>? confirm = provideConfirmation ? _ => { confirmations++; return Task.FromResult(true); } : null;
        var response = await Dispatcher(administrator).ExecuteAsync(request, confirm);
        Assert.Equal(OperationStatus.Failed, response.Status);
        Assert.Equal(0, confirmations);
        Assert.Equal(0, _system.RestoreCalls);
        Assert.Equal(60u, _system.Value);
    }

    [Fact]
    public async Task ReviewedRestoreVerifiesActualOriginalAndCanBeRepeatedWithoutWrites()
    {
        await ApplyAsync();
        var request = await ReviewedRestoreAsync();
        var response = await Dispatcher(true).ExecuteAsync(request, _ => Task.FromResult(true));
        Assert.Equal(OperationStatus.Restored, response.Status);
        Assert.Equal(20u, _system.Value);
        Assert.Equal(1, _system.RestoreCalls);
        var repeated = await Dispatcher(true).ExecuteAsync(await ReviewedRestoreAsync(), _ => Task.FromResult(true));
        Assert.Equal(OperationStatus.Restored, repeated.Status);
        Assert.Equal(1, _system.RestoreCalls);
    }

    [Fact]
    public async Task APreviouslyRestoredRecordCannotReportALaterExternalSettingAsRestored()
    {
        await ApplyAsync();
        var response = await Dispatcher(true).ExecuteAsync(await ReviewedRestoreAsync(), _ => Task.FromResult(true));
        Assert.Equal(OperationStatus.Restored, response.Status);
        _system.Value = 35;
        var repeated = await Dispatcher(true).ExecuteAsync(await ReviewedRestoreAsync(), _ => Task.FromResult(true));
        Assert.Equal(OperationStatus.Failed, repeated.Status);
        Assert.Equal(35u, _system.Value);
        Assert.Equal(1, _system.RestoreCalls);
    }

    [Fact]
    public async Task ChangedStateBetweenRestoreReviewAndRequestIsBlockedBeforeConfirmation()
    {
        await ApplyAsync();
        var request = await ReviewedRestoreAsync();
        _system.Value = 71;
        var confirmations = 0;
        var response = await Dispatcher(true).ExecuteAsync(request, _ => { confirmations++; return Task.FromResult(true); });
        Assert.Equal(OperationStatus.Failed, response.Status);
        Assert.Equal(0, confirmations);
        Assert.Equal(0, _system.RestoreCalls);
        Assert.Equal(71u, _system.Value);
    }

    [Theory]
    [InlineData("expected")]
    [InlineData("before")]
    [InlineData("expired")]
    [InlineData("future")]
    public async Task ForgedRestoreReviewCannotReachConfirmation(string changed)
    {
        await ApplyAsync();
        var request = await ReviewedRestoreAsync();
        var preview = request.ReviewedPreview!;
        preview = changed switch
        {
            "expected" => preview with { Expected = JsonSerializer.SerializeToElement(new FakeState(_target, 99)) },
            "before" => preview with { Before = JsonSerializer.SerializeToElement(new FakeState(_target, 99)) },
            "expired" => preview with { CapturedAt = DateTimeOffset.UtcNow.AddMinutes(-16) },
            "future" => preview with { CapturedAt = DateTimeOffset.UtcNow.AddMinutes(1) },
            _ => throw new ArgumentOutOfRangeException(nameof(changed))
        };
        var confirmations = 0;
        var response = await Dispatcher(true).ExecuteAsync(request with { ReviewedPreview = preview },
            _ => { confirmations++; return Task.FromResult(true); });
        Assert.Equal(OperationStatus.Failed, response.Status);
        Assert.Equal(0, confirmations);
        Assert.Equal(0, _system.RestoreCalls);
    }

    [Fact]
    public async Task UnknownRestoreRecordAndTamperedOperationKindCannotReachAnAdapter()
    {
        var dispatcher = Dispatcher(true);
        var missing = await dispatcher.ExecuteAsync(new(ControlAction.PreviewRestore, OperationKind.CpuMinimum) { OperationId = Guid.NewGuid() });
        Assert.Equal(OperationStatus.Failed, missing.Status);
        await ApplyAsync();
        var request = await ReviewedRestoreAsync();
        var wrongKind = await dispatcher.ExecuteAsync(request with { Kind = OperationKind.CpuMaximum }, _ => Task.FromResult(true));
        Assert.Equal(OperationStatus.Failed, wrongKind.Status);
        Assert.Contains("does not belong", wrongKind.Message);
        Assert.Equal(0, _system.RestoreCalls);
    }

    [Fact]
    public async Task UnknownUnregisteredOperationReturnsUnsupportedWithoutExecuting()
    {
        var response = await Dispatcher(false).ExecuteAsync(new(ControlAction.Preview, OperationKind.Mtu)
        {
            AdapterId = Guid.NewGuid(), AddressFamily = NetworkFamily.IPv4, NumericValue = 1492
        });
        Assert.Equal(OperationStatus.Unsupported, response.Status);
        Assert.Equal(0, _system.ApplyCalls);
    }

    [Fact]
    public async Task RestoreWithoutReviewedStateIsRejectedEvenWithAdministratorAndConfirmation()
    {
        var applied = await ApplyAsync();
        var response = await Dispatcher(true).ExecuteAsync(new(ControlAction.Restore, OperationKind.CpuMinimum)
        {
            OperationId = applied.OperationId
        }, _ => Task.FromResult(true));
        Assert.Equal(OperationStatus.Failed, response.Status);
        Assert.Equal(0, _system.RestoreCalls);
    }

    [Fact]
    public async Task DecliningRestoreConfirmationDoesNotChangeTrackedOrActualState()
    {
        await ApplyAsync();
        var response = await Dispatcher(true).ExecuteAsync(await ReviewedRestoreAsync(), _ => Task.FromResult(false));
        Assert.True(response.Cancelled);
        Assert.Equal(OperationStatus.RestoreAvailable, response.Status);
        Assert.Equal(60u, _system.Value);
        Assert.Equal(0, _system.RestoreCalls);
        Assert.Equal(OperationStatus.Verified, Assert.Single(await Journal.GetAllAsync()).Status);
    }

    [Fact]
    public async Task RestoreStateChangingDuringConfirmationCannotUseAnOlderKnownSnapshot()
    {
        await ApplyAsync(40);
        await ApplyAsync(60); // BeforeState is now 40, original remains 20.
        var request = await ReviewedRestoreAsync();
        var response = await Dispatcher(true).ExecuteAsync(request, _ =>
        {
            _system.Value = 40; // Known to journal, but different from the exact reviewed current state.
            return Task.FromResult(true);
        });
        Assert.Equal(OperationStatus.Failed, response.Status);
        Assert.Equal(0, _system.RestoreCalls);
        Assert.Equal(40u, _system.Value);
    }

    [Fact]
    public async Task JournalOriginalChangingDuringConfirmationCannotChangeTheConfirmedRestoreTarget()
    {
        await ApplyAsync();
        var request = await ReviewedRestoreAsync();
        var response = await Dispatcher(true).ExecuteAsync(request, async _ =>
        {
            var record = Assert.Single(await Journal.GetAllAsync());
            await Journal.SaveAsync(record with { OriginalState = JsonSerializer.SerializeToElement(new FakeState(_target, 99)) });
            return true;
        });
        Assert.Equal(OperationStatus.Failed, response.Status);
        Assert.Equal(0, _system.RestoreCalls);
        Assert.Equal(60u, _system.Value);
    }

    private ControlRequest Request(ControlAction action, uint value = 60) => new(action, OperationKind.CpuMinimum)
    {
        SchemeId = _target, PowerSource = PowerSource.Ac, NumericValue = value
    };

    private async Task<ControlRequest> ReviewedApplyAsync(uint value = 60)
    {
        var request = Request(ControlAction.Preview, value);
        var preview = await Dispatcher(false).ExecuteAsync(request);
        Assert.NotNull(preview.Preview);
        return request with { Action = ControlAction.Apply, ReviewedPreview = preview.Preview };
    }

    private async Task<ControlResponse> ApplyAsync(uint value = 60)
    {
        var response = await Dispatcher(true).ExecuteAsync(await ReviewedApplyAsync(value), _ => Task.FromResult(true));
        Assert.Equal(OperationStatus.Verified, response.Status);
        return response;
    }

    private async Task<ControlRequest> ReviewedRestoreAsync()
    {
        var record = Assert.Single(await Journal.GetAllAsync());
        var dispatcher = Dispatcher(false);
        var request = dispatcher.CreateRestoreRequest(record)!;
        var preview = await dispatcher.ExecuteAsync(request);
        Assert.Equal(OperationStatus.RestoreAvailable, preview.Status);
        return request with { Action = ControlAction.Restore, ReviewedPreview = preview.Preview };
    }

    private ControlDispatcher Dispatcher(bool administrator)
    {
        var privileges = new Privileges(administrator);
        var engine = new TweakEngine(Journal, privileges);
        IControlHandler Handler(OperationKind kind) => new TweakControlHandler<FakeState, uint>(kind, engine,
            request => new FakeTweak(_system, request.SchemeId!.Value, kind), request => request.NumericValue!.Value,
            record => record.TweakId == FakeTweak.Id(_target, kind) && record.ResourceId == FakeTweak.Resource(_target, kind),
            _ => new FakeTweak(_system, _target, kind));
        return new ControlDispatcher(Journal, [Handler(OperationKind.CpuMinimum), Handler(OperationKind.CpuMaximum)], privileges);
    }

    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }

    private sealed record Privileges(bool IsAdministrator) : IPrivilegeContext;
    public sealed record FakeState(Guid Target, uint Value);
    private sealed class FakeSystem
    {
        public uint Value { get; set; } = 20;
        public int ApplyCalls { get; set; }
        public int RestoreCalls { get; set; }
    }
    private sealed class FakeTweak(FakeSystem system, Guid target, OperationKind kind) : ITweak<FakeState, uint>
    {
        public static string Id(Guid target, OperationKind kind) => $"test.{kind}.{target:N}";
        public static string Resource(Guid target, OperationKind kind) => $"test/{target:N}/{kind}";
        public TweakDefinition Definition { get; } = new(Id(target, kind), Resource(target, kind), $"{kind} test policy",
            "Isolated state adapter, no Windows modifications", "Tests", RiskClass.Advanced, PrivilegeRequirement.Administrator,
            ["Test"], [], ["0–100"], [], [], "No performance implications.");
        public ValueTask<Compatibility> CheckCompatibilityAsync(CancellationToken cancellationToken) => ValueTask.FromResult(Compatibility.Supported());
        public ValueTask<FakeState> ReadAsync(CancellationToken cancellationToken) => ValueTask.FromResult(new FakeState(target, system.Value));
        public IReadOnlyList<string> Validate(uint requestedValue) => requestedValue <= 100 ? [] : ["Out of range."];
        public FakeState Project(FakeState currentState, uint requestedValue) => currentState with { Value = requestedValue };
        public bool StatesEqual(FakeState left, FakeState right) => left == right;
        public string DescribeChange(FakeState currentState, FakeState expectedState) => $"{Definition.Name}: {currentState.Value} → {expectedState.Value}";
        public ValueTask ApplyAsync(uint requestedValue, CancellationToken cancellationToken)
        {
            system.ApplyCalls++; system.Value = requestedValue;
            return ValueTask.CompletedTask;
        }
        public ValueTask RestoreAsync(FakeState originalState, CancellationToken cancellationToken)
        {
            if (originalState.Target != target) throw new ConfigurationConflictException("Wrong test target.");
            system.RestoreCalls++; system.Value = originalState.Value;
            return ValueTask.CompletedTask;
        }
    }
}
