using Carpatia.Ultimate.Core;
using Carpatia.Ultimate.Engine;
using Carpatia.Ultimate.Platform.Windows.Cpu;
using Xunit;

namespace Carpatia.Ultimate.Platform.Tests.Cpu;

public sealed class CpuPowerPolicyTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "carpatia-cpu-tests", Guid.NewGuid().ToString("N"));
    private static readonly Guid Scheme = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OtherScheme = new("22222222-2222-2222-2222-222222222222");
    private FileOperationJournal Journal => new(_directory);
    private TweakEngine Engine => new(Journal, new Administrator());

    [Theory]
    [InlineData(CpuPowerSetting.MinimumProcessorState, PowerPolicySource.Ac, 80)]
    [InlineData(CpuPowerSetting.MinimumProcessorState, PowerPolicySource.Dc, 80)]
    [InlineData(CpuPowerSetting.MaximumProcessorState, PowerPolicySource.Ac, 90)]
    [InlineData(CpuPowerSetting.MaximumProcessorState, PowerPolicySource.Dc, 90)]
    [InlineData(CpuPowerSetting.EnergyPerformancePreference, PowerPolicySource.Ac, 30)]
    [InlineData(CpuPowerSetting.EnergyPerformancePreference, PowerPolicySource.Dc, 30)]
    [InlineData(CpuPowerSetting.BoostMode, PowerPolicySource.Ac, 1)]
    [InlineData(CpuPowerSetting.BoostMode, PowerPolicySource.Dc, 1)]
    public async Task AppliesAndRestoresOnlyTheSelectedSchemeSourceAndSetting(CpuPowerSetting setting, PowerPolicySource source, uint requested)
    {
        var api = new FakePowerPolicyApi();
        var before = new Dictionary<(Guid, PowerPolicySource, CpuPowerSetting), uint>(api.Values);
        var tweak = new CpuPowerPolicyTweak(api, Scheme, source, setting);
        var engine = Engine;
        var preview = await engine.PreviewAsync(tweak, requested);
        var result = await engine.ApplyAsync(tweak, preview);
        Assert.Equal(OperationStatus.Verified, result.Status);
        Assert.Equal(requested, api.Values[(Scheme, source, setting)]);
        Assert.Single(api.Writes);
        Assert.Equal(1, api.ReapplyCalls);
        Assert.Equal(Scheme, api.ActiveScheme);
        foreach (var entry in before.Where(x => x.Key != (Scheme, source, setting)))
            Assert.Equal(entry.Value, api.Values[entry.Key]);

        var record = Assert.Single(await Journal.GetAllAsync());
        Assert.Equal(Scheme, record.OriginalState.GetProperty("SchemeId").GetGuid());
        Assert.Equal((int)source, record.OriginalState.GetProperty("Source").GetInt32());
        Assert.Equal(before[(Scheme, source, setting)], record.OriginalState.GetProperty("Value").GetUInt32());
        Assert.Equal(OperationStatus.Restored, (await engine.RestoreAsync(tweak, result.OperationId!.Value)).Status);
        Assert.Equal(before.Count, api.Values.Count);
        foreach (var entry in before) Assert.Equal(entry.Value, api.Values[entry.Key]);
    }

    [Fact]
    public async Task RepeatedApplyKeepsFirstOriginalAndRepeatedRestoreDoesNotOverwriteLaterChanges()
    {
        var api = new FakePowerPolicyApi();
        var tweak = new CpuPowerPolicyTweak(api, Scheme, PowerPolicySource.Ac, CpuPowerSetting.MinimumProcessorState);
        var engine = Engine;
        var applied = await engine.ApplyAsync(tweak, await engine.PreviewAsync(tweak, 60u));
        var again = await engine.ApplyAsync(tweak, await engine.PreviewAsync(tweak, 80u));
        Assert.Equal(applied.OperationId, again.OperationId);
        Assert.Equal(20u, Assert.Single(await Journal.GetAllAsync()).OriginalState.GetProperty("Value").GetUInt32());
        Assert.Equal(OperationStatus.Restored, (await engine.RestoreAsync(tweak, applied.OperationId!.Value)).Status);
        api.Values[(Scheme, PowerPolicySource.Ac, CpuPowerSetting.MinimumProcessorState)] = 33;
        Assert.Equal(OperationStatus.Restored, (await engine.RestoreAsync(tweak, applied.OperationId.Value)).Status);
        Assert.Equal(33u, api.Values[(Scheme, PowerPolicySource.Ac, CpuPowerSetting.MinimumProcessorState)]);
        Assert.Equal(3, api.Writes.Count);
    }

    [Fact]
    public async Task NoOpDoesNotCreateJournalOrWrite()
    {
        var api = new FakePowerPolicyApi();
        var tweak = new CpuPowerPolicyTweak(api, Scheme, PowerPolicySource.Ac, CpuPowerSetting.MinimumProcessorState);
        var result = await Engine.ApplyAsync(tweak, await Engine.PreviewAsync(tweak, 20u));
        Assert.Equal(OperationStatus.AlreadyApplied, result.Status);
        Assert.Empty(api.Writes);
        Assert.Empty(await Journal.GetAllAsync());
    }

    [Fact]
    public async Task MissingApiOrPolicyMetadataIsUnsupported()
    {
        var api = new FakePowerPolicyApi { IsSupported = false };
        var tweak = new CpuPowerPolicyTweak(api, Scheme, PowerPolicySource.Ac, CpuPowerSetting.EnergyPerformancePreference);
        Assert.False((await tweak.CheckCompatibilityAsync(default)).IsSupported);
        api.IsSupported = true;
        api.Capabilities[CpuPowerSetting.EnergyPerformancePreference] = PowerPolicyCapability.Unsupported("No EPP definition.");
        Assert.False((await tweak.CheckCompatibilityAsync(default)).IsSupported);
        await Assert.ThrowsAsync<TweakUnavailableException>(async () => await Engine.PreviewAsync(tweak, 30u));
        Assert.Empty(api.Writes);
    }

    [Fact]
    public async Task UnknownCurrentValueCannotBecomeAnUnsafeOriginalSnapshot()
    {
        var api = new FakePowerPolicyApi();
        api.Values[(Scheme, PowerPolicySource.Ac, CpuPowerSetting.BoostMode)] = 9;
        var tweak = new CpuPowerPolicyTweak(api, Scheme, PowerPolicySource.Ac, CpuPowerSetting.BoostMode);
        Assert.False((await tweak.CheckCompatibilityAsync(default)).IsSupported);
        await Assert.ThrowsAsync<TweakUnavailableException>(async () => await Engine.PreviewAsync(tweak, 1u));
        Assert.Empty(await Journal.GetAllAsync());
    }

    [Fact]
    public void ValidationUsesDetectedBoundsAndIncrementAndRejectsUndocumentedModes()
    {
        var api = new FakePowerPolicyApi();
        api.Capabilities[CpuPowerSetting.EnergyPerformancePreference] = new(true, "Test range", 10, 90, 5, []);
        var epp = new CpuPowerPolicyTweak(api, Scheme, PowerPolicySource.Ac, CpuPowerSetting.EnergyPerformancePreference);
        Assert.Empty(epp.Validate(20));
        Assert.NotEmpty(epp.Validate(12));
        Assert.NotEmpty(epp.Validate(5));
        Assert.NotEmpty(epp.Validate(95));
        Assert.NotEmpty(epp.Validate(101));
        var boost = new CpuPowerPolicyTweak(api, Scheme, PowerPolicySource.Ac, CpuPowerSetting.BoostMode);
        Assert.NotEmpty(boost.Validate(7));
        api.Capabilities[CpuPowerSetting.BoostMode] = new(true, "Subset", 0, 2, 1, [0, 2]);
        Assert.Empty(boost.Validate(2));
        Assert.NotEmpty(boost.Validate(1));
    }

    [Theory]
    [InlineData(CpuPowerSetting.MinimumProcessorState, 95)]
    [InlineData(CpuPowerSetting.MaximumProcessorState, 10)]
    public async Task PreviewRejectsInvertedMinimumMaximum(CpuPowerSetting setting, uint value)
    {
        var api = new FakePowerPolicyApi();
        api.Values[(Scheme, PowerPolicySource.Ac, CpuPowerSetting.MaximumProcessorState)] = 90;
        var tweak = new CpuPowerPolicyTweak(api, Scheme, PowerPolicySource.Ac, setting);
        await Assert.ThrowsAsync<ArgumentException>(async () => await Engine.PreviewAsync(tweak, value));
        Assert.Empty(api.Writes);
    }

    [Fact]
    public async Task ApplyRevalidatesCompanionChangedAfterPreview()
    {
        var api = new FakePowerPolicyApi();
        var tweak = new CpuPowerPolicyTweak(api, Scheme, PowerPolicySource.Ac, CpuPowerSetting.MinimumProcessorState);
        var preview = await Engine.PreviewAsync(tweak, 80u);
        api.Values[(Scheme, PowerPolicySource.Ac, CpuPowerSetting.MaximumProcessorState)] = 70;
        await Assert.ThrowsAsync<ArgumentException>(async () => await Engine.ApplyAsync(tweak, preview));
        Assert.Empty(api.Writes);
        Assert.Empty(await Journal.GetAllAsync());
        Assert.Equal(70u, api.Values[(Scheme, PowerPolicySource.Ac, CpuPowerSetting.MaximumProcessorState)]);
    }

    [Fact]
    public async Task SafeRestoreDoesNotRestoreCompanionOrOtherSource()
    {
        var api = new FakePowerPolicyApi();
        var tweak = new CpuPowerPolicyTweak(api, Scheme, PowerPolicySource.Ac, CpuPowerSetting.MinimumProcessorState);
        var engine = Engine;
        var result = await engine.ApplyAsync(tweak, await engine.PreviewAsync(tweak, 60u));
        api.Values[(Scheme, PowerPolicySource.Ac, CpuPowerSetting.MaximumProcessorState)] = 80;
        api.Values[(Scheme, PowerPolicySource.Dc, CpuPowerSetting.MinimumProcessorState)] = 35;
        Assert.Equal(OperationStatus.Restored, (await engine.RestoreAsync(tweak, result.OperationId!.Value)).Status);
        Assert.Equal(20u, api.Values[(Scheme, PowerPolicySource.Ac, CpuPowerSetting.MinimumProcessorState)]);
        Assert.Equal(80u, api.Values[(Scheme, PowerPolicySource.Ac, CpuPowerSetting.MaximumProcessorState)]);
        Assert.Equal(35u, api.Values[(Scheme, PowerPolicySource.Dc, CpuPowerSetting.MinimumProcessorState)]);
    }

    [Fact]
    public async Task RestoreCannotCreateAnInvalidMinimumMaximumCombination()
    {
        var api = new FakePowerPolicyApi();
        var tweak = new CpuPowerPolicyTweak(api, Scheme, PowerPolicySource.Ac, CpuPowerSetting.MinimumProcessorState);
        var engine = Engine;
        var result = await engine.ApplyAsync(tweak, await engine.PreviewAsync(tweak, 10u));
        api.Values[(Scheme, PowerPolicySource.Ac, CpuPowerSetting.MaximumProcessorState)] = 15;
        Assert.Equal(OperationStatus.RecoveryRequired, (await engine.RestoreAsync(tweak, result.OperationId!.Value)).Status);
        Assert.Equal(10u, api.Values[(Scheme, PowerPolicySource.Ac, CpuPowerSetting.MinimumProcessorState)]);
        Assert.Single(api.Writes);
    }

    [Fact]
    public async Task ChangedActiveSchemeAfterPreviewBlocksWritesAndNeverSwitchesItBack()
    {
        var api = new FakePowerPolicyApi();
        var tweak = new CpuPowerPolicyTweak(api, Scheme, PowerPolicySource.Ac, CpuPowerSetting.MinimumProcessorState);
        var preview = await Engine.PreviewAsync(tweak, 80u);
        api.ActiveScheme = OtherScheme;
        await Assert.ThrowsAsync<ConfigurationConflictException>(async () => await Engine.ApplyAsync(tweak, preview));
        Assert.Empty(api.Writes);
        Assert.Equal(OtherScheme, api.ActiveScheme);
        Assert.Empty(await Journal.GetAllAsync());
    }

    [Fact]
    public async Task SchemeSwitchDuringWriteIsDetectedAndDoesNotReapplyOldScheme()
    {
        var api = new FakePowerPolicyApi();
        api.AfterWrite = () => api.ActiveScheme = OtherScheme;
        var tweak = new CpuPowerPolicyTweak(api, Scheme, PowerPolicySource.Ac, CpuPowerSetting.MinimumProcessorState);
        var result = await Engine.ApplyAsync(tweak, await Engine.PreviewAsync(tweak, 80u));
        Assert.Equal(OperationStatus.RecoveryRequired, result.Status);
        Assert.Equal(0, api.ReapplyCalls);
        Assert.Equal(OtherScheme, api.ActiveScheme);
        Assert.Equal(20u, Assert.Single(await Journal.GetAllAsync()).OriginalState.GetProperty("Value").GetUInt32());
    }

    [Fact]
    public async Task IgnoredNativeWriteCannotBeReportedAsVerified()
    {
        var api = new FakePowerPolicyApi { IgnoreWrites = true };
        var tweak = new CpuPowerPolicyTweak(api, Scheme, PowerPolicySource.Ac, CpuPowerSetting.MinimumProcessorState);
        Assert.Equal(OperationStatus.Failed, (await Engine.ApplyAsync(tweak, await Engine.PreviewAsync(tweak, 80u))).Status);
    }

    [Fact]
    public async Task InvalidBoundChangedDuringWriteIsNotReportedAsVerified()
    {
        var api = new FakePowerPolicyApi();
        api.AfterWrite = () => api.Values[(Scheme, PowerPolicySource.Ac, CpuPowerSetting.MaximumProcessorState)] = 50;
        var tweak = new CpuPowerPolicyTweak(api, Scheme, PowerPolicySource.Ac, CpuPowerSetting.MinimumProcessorState);
        var engine = Engine;
        var result = await engine.ApplyAsync(tweak, await engine.PreviewAsync(tweak, 80u));
        Assert.Equal(OperationStatus.PartiallyApplied, result.Status);
        Assert.Equal(80u, api.Values[(Scheme, PowerPolicySource.Ac, CpuPowerSetting.MinimumProcessorState)]);
        Assert.Equal(50u, api.Values[(Scheme, PowerPolicySource.Ac, CpuPowerSetting.MaximumProcessorState)]);
        api.AfterWrite = null;
        Assert.Equal(OperationStatus.Restored, (await engine.RestoreAsync(tweak, result.OperationId!.Value)).Status);
        Assert.Equal(20u, api.Values[(Scheme, PowerPolicySource.Ac, CpuPowerSetting.MinimumProcessorState)]);
        Assert.Equal(50u, api.Values[(Scheme, PowerPolicySource.Ac, CpuPowerSetting.MaximumProcessorState)]);
    }

    [Fact]
    public async Task ReapplyFailureDoesNotBecomeVerifiedJustBecauseTheStoredIndexMatches()
    {
        var api = new FakePowerPolicyApi { FailReapply = true };
        var tweak = new CpuPowerPolicyTweak(api, Scheme, PowerPolicySource.Ac, CpuPowerSetting.MinimumProcessorState);
        var engine = Engine;
        var result = await engine.ApplyAsync(tweak, await engine.PreviewAsync(tweak, 80u));
        Assert.Equal(OperationStatus.PartiallyApplied, result.Status);
        Assert.Equal(80u, api.Values[(Scheme, PowerPolicySource.Ac, CpuPowerSetting.MinimumProcessorState)]);
        Assert.Equal(20u, Assert.Single(await Journal.GetAllAsync()).OriginalState.GetProperty("Value").GetUInt32());
        api.FailReapply = false;
        Assert.Equal(OperationStatus.Restored, (await engine.RestoreAsync(tweak, result.OperationId!.Value)).Status);
        Assert.Equal(20u, api.Values[(Scheme, PowerPolicySource.Ac, CpuPowerSetting.MinimumProcessorState)]);
    }

    [Fact]
    public async Task RestoreReapplyFailureRequiresSuccessfulRetryEvenWhenOriginalIndexWasWritten()
    {
        var api = new FakePowerPolicyApi();
        var tweak = new CpuPowerPolicyTweak(api, Scheme, PowerPolicySource.Ac, CpuPowerSetting.MinimumProcessorState);
        var engine = Engine;
        var result = await engine.ApplyAsync(tweak, await engine.PreviewAsync(tweak, 80u));
        api.FailReapply = true;
        Assert.Equal(OperationStatus.RecoveryRequired, (await engine.RestoreAsync(tweak, result.OperationId!.Value)).Status);
        Assert.Equal(20u, api.Values[(Scheme, PowerPolicySource.Ac, CpuPowerSetting.MinimumProcessorState)]);
        api.FailReapply = false;
        Assert.Equal(OperationStatus.Restored, (await engine.RestoreAsync(tweak, result.OperationId.Value)).Status);
        Assert.Equal(3, api.ReapplyCalls);
    }

    [Fact]
    public async Task AdministratorPermissionIsRequiredByTheEngine()
    {
        var api = new FakePowerPolicyApi();
        var tweak = new CpuPowerPolicyTweak(api, Scheme, PowerPolicySource.Ac, CpuPowerSetting.MinimumProcessorState);
        var engine = new TweakEngine(Journal);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(async () => await engine.ApplyAsync(tweak, await engine.PreviewAsync(tweak, 80u)));
        Assert.Empty(api.Writes);
    }

    [Fact]
    public async Task SnapshotForAnotherSourceCannotBeRestored()
    {
        var api = new FakePowerPolicyApi();
        var tweak = new CpuPowerPolicyTweak(api, Scheme, PowerPolicySource.Ac, CpuPowerSetting.MinimumProcessorState);
        await Assert.ThrowsAsync<ConfigurationConflictException>(async () =>
            await tweak.RestoreAsync(new(Scheme, PowerPolicySource.Dc, CpuPowerSetting.MinimumProcessorState, 20, 100), default));
        Assert.Empty(api.Writes);
    }

    [Fact]
    public async Task CancelledOperationDoesNotWrite()
    {
        var api = new FakePowerPolicyApi();
        var tweak = new CpuPowerPolicyTweak(api, Scheme, PowerPolicySource.Ac, CpuPowerSetting.MinimumProcessorState);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await tweak.ApplyAsync(80, new CancellationToken(true)));
        Assert.Empty(api.Writes);
    }

    [Fact]
    public void NativeApiIsGuardedOnNonWindowsHosts()
    {
        if (OperatingSystem.IsWindows()) return;
        var api = new NativePowerPolicyApi();
        Assert.False(api.IsSupported);
        Assert.False(api.GetCapability(CpuPowerSetting.MinimumProcessorState).IsSupported);
        Assert.Throws<PlatformNotSupportedException>(() => api.GetActiveScheme());
        Assert.Throws<PlatformNotSupportedException>(() => api.WriteValue(Scheme, PowerPolicySource.Ac, CpuPowerSetting.MinimumProcessorState, 80));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }

    private sealed class Administrator : IPrivilegeContext { public bool IsAdministrator => true; }

    private sealed class FakePowerPolicyApi : IPowerPolicyApi
    {
        public bool IsSupported { get; set; } = true;
        public Guid ActiveScheme { get; set; } = Scheme;
        public bool IgnoreWrites { get; set; }
        public bool FailReapply { get; set; }
        public Action? AfterWrite { get; set; }
        public int ReapplyCalls { get; private set; }
        public List<(Guid Scheme, PowerPolicySource Source, CpuPowerSetting Setting, uint Value)> Writes { get; } = [];
        public Dictionary<(Guid, PowerPolicySource, CpuPowerSetting), uint> Values { get; } = [];
        public Dictionary<CpuPowerSetting, PowerPolicyCapability> Capabilities { get; } = [];

        public FakePowerPolicyApi()
        {
            foreach (var setting in Enum.GetValues<CpuPowerSetting>())
            {
                Capabilities[setting] = setting == CpuPowerSetting.BoostMode
                    ? new(true, "Fake supported policy", 0, 6, 1, [0, 1, 2, 3, 4, 5, 6])
                    : new(true, "Fake supported policy", 0, 100, 1, []);
                foreach (var scheme in new[] { Scheme, OtherScheme })
                    foreach (var source in Enum.GetValues<PowerPolicySource>())
                        Values[(scheme, source, setting)] = setting switch
                        {
                            CpuPowerSetting.MinimumProcessorState => 20,
                            CpuPowerSetting.MaximumProcessorState => 100,
                            CpuPowerSetting.EnergyPerformancePreference => 50,
                            _ => 2
                        };
            }
        }
        public Guid GetActiveScheme() => ActiveScheme;
        public PowerPolicyCapability GetCapability(CpuPowerSetting setting) => Capabilities[setting];
        public uint ReadValue(Guid schemeId, PowerPolicySource source, CpuPowerSetting setting) => Values[(schemeId, source, setting)];
        public void WriteValue(Guid schemeId, PowerPolicySource source, CpuPowerSetting setting, uint value)
        {
            Writes.Add((schemeId, source, setting, value));
            if (!IgnoreWrites) Values[(schemeId, source, setting)] = value;
            AfterWrite?.Invoke();
        }
        public void ReapplyActiveScheme(Guid expectedSchemeId)
        {
            Assert.Equal(ActiveScheme, expectedSchemeId);
            ReapplyCalls++;
            if (FailReapply) throw new System.ComponentModel.Win32Exception(5, "Simulated PowerSetActiveScheme failure.");
        }
    }
}
