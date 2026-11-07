using Carpatia.Ultimate.Core;
using Carpatia.Ultimate.Engine;
using Carpatia.Ultimate.Platform.Windows.Network;
using Xunit;

namespace Carpatia.Ultimate.Platform.Tests.Network;

/// <summary>Tests use isolated memory adapters. They do not claim Windows or driver validation.</summary>
public sealed class NetworkTweaksTests : IDisposable
{
    private readonly string _journalPath = Path.Combine(Path.GetTempPath(), "carpatia-network-tests", Guid.NewGuid().ToString("N"));
    private static readonly Guid AdapterId = Guid.Parse("294f902c-8357-43a6-b276-054ee1841faa");
    private static readonly Guid OtherAdapterId = Guid.Parse("953e8f72-29d7-4662-b286-b91147936ec8");
    private FileOperationJournal Journal => new(_journalPath);
    private TweakEngine Engine => new(Journal, new AdministratorContext());

    [Fact]
    public void DnsPresetsContainDocumentedOrderedAddressesAndReturnIndependentArrays()
    {
        Assert.Equal(new[] { "1.1.1.1", "1.0.0.1" }, DnsPresets.Cloudflare.Servers);
        Assert.Equal(new[] { "9.9.9.9", "149.112.112.112" }, DnsPresets.Quad9.Servers);
        Assert.Equal(new[] { "8.8.8.8", "8.8.4.4" }, DnsPresets.Google.Servers);
        Assert.Equal(DnsSource.Automatic, DnsPresets.Automatic.Source);
        Assert.Empty(DnsPresets.Automatic.Servers);
        var changed = DnsPresets.Cloudflare;
        changed.Servers[0] = "127.0.0.1";
        Assert.Equal("1.1.1.1", DnsPresets.Cloudflare.Servers[0]);
    }

    [Theory]
    [InlineData("1.1.1.1; Remove-Item C:\\*")]
    [InlineData("$(whoami)")]
    [InlineData("1.1.1.1\n8.8.8.8")]
    [InlineData("resolver.example.com")]
    [InlineData("1.1.1.999")]
    [InlineData("0.0.0.0")]
    [InlineData("255.255.255.255")]
    [InlineData("224.0.0.1")]
    [InlineData("1")]
    [InlineData("01.1.1.1")]
    [InlineData("::1")]
    public void DnsValidationRejectsInvalidNonUnicastOrExecutableText(string address)
        => Assert.NotEmpty(NetworkValidation.Dns(new(DnsSource.Static, [address]), IpFamily.IPv4));

    [Fact]
    public void DnsValidationRejectsSourceListMismatchesDuplicatesAndUnknownEnums()
    {
        Assert.NotEmpty(NetworkValidation.Dns(new(DnsSource.Automatic, ["1.1.1.1"]), IpFamily.IPv4));
        Assert.NotEmpty(NetworkValidation.Dns(new(DnsSource.Static, []), IpFamily.IPv4));
        Assert.NotEmpty(NetworkValidation.Dns(new(DnsSource.Static, ["1.1.1.1", "1.1.1.1"]), IpFamily.IPv4));
        Assert.NotEmpty(NetworkValidation.Dns(new(DnsSource.Static, null!), IpFamily.IPv4));
        Assert.NotEmpty(NetworkValidation.Dns(new((DnsSource)999, []), IpFamily.IPv4));
        Assert.NotEmpty(NetworkValidation.Dns(DnsPresets.Automatic, (IpFamily)999));
        Assert.NotEmpty(NetworkValidation.Dns(new(DnsSource.Static, Enumerable.Range(1, 9).Select(i => $"10.0.0.{i}").ToArray()), IpFamily.IPv4));
    }

    [Fact]
    public void IPv6ValueValidationDoesNotMisclassifyAnIPv4AddressOrPermitScopeInjection()
    {
        Assert.Empty(NetworkValidation.Dns(new(DnsSource.Static, ["2606:4700:4700::1111"]), IpFamily.IPv6));
        Assert.NotEmpty(NetworkValidation.Dns(new(DnsSource.Static, ["1.1.1.1"]), IpFamily.IPv6));
        Assert.NotEmpty(NetworkValidation.Dns(new(DnsSource.Static, ["fe80::1%9"]), IpFamily.IPv6));
        Assert.NotEmpty(NetworkValidation.Dns(new(DnsSource.Static, ["ff02::1"]), IpFamily.IPv6));
        Assert.NotEmpty(NetworkValidation.Dns(new(DnsSource.Static, ["::"]), IpFamily.IPv6));
    }

    [Fact]
    public async Task DnsApplyAndRepeatedApplyRetainAutomaticSourceForRestoration()
    {
        var backend = new MemoryBackend();
        var tweak = new DnsTweak(AdapterId, IpFamily.IPv4, backend);
        var engine = Engine;
        var first = await engine.ApplyAsync(tweak, await engine.PreviewAsync(tweak, DnsPresets.Cloudflare));
        Assert.Equal(OperationStatus.Verified, first.Status);
        Assert.Equal(DnsSource.Static, backend.Dns.Source);
        Assert.Equal(DnsPresets.Cloudflare.Servers, backend.Dns.Servers);
        var repeated = await engine.ApplyAsync(tweak, await engine.PreviewAsync(tweak, DnsPresets.Cloudflare));
        Assert.Equal(OperationStatus.AlreadyApplied, repeated.Status);
        await engine.ApplyAsync(tweak, await engine.PreviewAsync(tweak, DnsPresets.Google));
        var record = Assert.Single(await Journal.GetAllAsync());
        Assert.Equal((int)DnsSource.Automatic, record.OriginalState.GetProperty("Source").GetInt32());
        Assert.Empty(record.OriginalState.GetProperty("Servers").EnumerateArray());
        Assert.Equal(first.OperationId, record.Id);
        Assert.Equal(OperationStatus.Restored, (await engine.RestoreAsync(tweak, record.Id)).Status);
        Assert.Equal(DnsSource.Automatic, backend.Dns.Source);
        Assert.Empty(backend.Dns.Servers);
    }

    [Fact]
    public async Task DnsAutomaticRequestRestoresOriginalStaticAddressOrder()
    {
        var backend = new MemoryBackend { Dns = new(AdapterId, IpFamily.IPv4, DnsSource.Static, ["8.8.4.4", "8.8.8.8", "10.20.30.40"]) };
        var tweak = new DnsTweak(AdapterId, IpFamily.IPv4, backend);
        var engine = Engine;
        var result = await engine.ApplyAsync(tweak, await engine.PreviewAsync(tweak, DnsPresets.Automatic));
        Assert.Equal(DnsSource.Automatic, backend.Dns.Source);
        Assert.Empty(backend.Dns.Servers);
        await engine.RestoreAsync(tweak, result.OperationId!.Value);
        Assert.Equal(DnsSource.Static, backend.Dns.Source);
        Assert.Equal(new[] { "8.8.4.4", "8.8.8.8", "10.20.30.40" }, backend.Dns.Servers);
    }

    [Fact]
    public async Task DnsPreviewCopiesRequestedListSoLaterMutationCannotChangeReviewedPlan()
    {
        var backend = new MemoryBackend();
        var tweak = new DnsTweak(AdapterId, IpFamily.IPv4, backend);
        var engine = Engine;
        var requested = new DnsConfiguration(DnsSource.Static, ["1.1.1.1", "1.0.0.1"]);
        var preview = await engine.PreviewAsync(tweak, requested);
        requested.Servers[0] = "8.8.8.8";
        await engine.ApplyAsync(tweak, preview);
        Assert.Equal(new[] { "1.1.1.1", "1.0.0.1" }, backend.Dns.Servers);
    }

    [Fact]
    public async Task DnsSourceChangeByAnotherProgramBlocksAutomaticRestore()
    {
        var backend = new MemoryBackend();
        var tweak = new DnsTweak(AdapterId, IpFamily.IPv4, backend);
        var engine = Engine;
        var result = await engine.ApplyAsync(tweak, await engine.PreviewAsync(tweak, DnsPresets.Cloudflare));
        backend.Dns = backend.Dns with { Source = DnsSource.Static, Servers = ["10.0.0.53"] };
        await Assert.ThrowsAsync<ConfigurationConflictException>(async () => await engine.RestoreAsync(tweak, result.OperationId!.Value));
        Assert.Equal(new[] { "10.0.0.53" }, backend.Dns.Servers);
        Assert.Equal(1, backend.DnsWrites);
    }

    [Fact]
    public async Task DnsReadsAndRestoreRefuseAnotherInterfaceOrAddressFamily()
    {
        var backend = new MemoryBackend { Dns = new(OtherAdapterId, IpFamily.IPv4, DnsSource.Automatic, []) };
        var tweak = new DnsTweak(AdapterId, IpFamily.IPv4, backend);
        await Assert.ThrowsAsync<ConfigurationConflictException>(async () => await tweak.ReadAsync(default));
        await Assert.ThrowsAsync<ConfigurationConflictException>(async () => await tweak.RestoreAsync(backend.Dns, default));
        await Assert.ThrowsAsync<ConfigurationConflictException>(async () =>
            await tweak.RestoreAsync(new(AdapterId, IpFamily.IPv6, DnsSource.Automatic, []), default));
        Assert.Equal(0, backend.DnsWrites);
    }

    [Theory]
    [InlineData(IpFamily.IPv4, 575u, false)]
    [InlineData(IpFamily.IPv4, 576u, true)]
    [InlineData(IpFamily.IPv4, 1492u, true)]
    [InlineData(IpFamily.IPv4, 1500u, true)]
    [InlineData(IpFamily.IPv4, 65536u, false)]
    [InlineData(IpFamily.IPv6, 1279u, false)]
    [InlineData(IpFamily.IPv6, 1280u, true)]
    public void MtuValidationHonorsAddressFamilyMinimums(IpFamily family, uint bytes, bool valid)
        => Assert.Equal(valid, NetworkValidation.Mtu(bytes, family).Count == 0);

    [Fact]
    public async Task MtuApplyAndRestorePreserveOriginallyDifferentActiveAndPersistentStores()
    {
        var backend = new MemoryBackend { Mtu = new(AdapterId, IpFamily.IPv4, 1400, 1492, 1500) };
        var tweak = new MtuTweak(AdapterId, IpFamily.IPv4, backend);
        var engine = Engine;
        var result = await engine.ApplyAsync(tweak, await engine.PreviewAsync(tweak, new MtuConfiguration(1500)));
        Assert.Equal(OperationStatus.Verified, result.Status);
        Assert.Equal(1500u, backend.Mtu.ActiveBytes);
        Assert.Equal(1500u, backend.Mtu.PersistentBytes);
        await engine.ApplyAsync(tweak, await engine.PreviewAsync(tweak, new MtuConfiguration(1450)));
        var record = Assert.Single(await Journal.GetAllAsync());
        Assert.Equal(1400u, record.OriginalState.GetProperty("ActiveBytes").GetUInt32());
        Assert.Equal(1492u, record.OriginalState.GetProperty("PersistentBytes").GetUInt32());
        await engine.RestoreAsync(tweak, result.OperationId!.Value);
        Assert.Equal(1400u, backend.Mtu.ActiveBytes);
        Assert.Equal(1492u, backend.Mtu.PersistentBytes);
        Assert.Equal(1500u, backend.Mtu.LinkMtuBytes);
    }

    [Fact]
    public async Task MtuPreviewRejectsValuesAboveSelectedAdaptersReportedLinkMtu()
    {
        var backend = new MemoryBackend { Mtu = new(AdapterId, IpFamily.IPv4, 1492, 1492, 1492) };
        var tweak = new MtuTweak(AdapterId, IpFamily.IPv4, backend);
        await Assert.ThrowsAsync<ArgumentException>(async () => await Engine.PreviewAsync(tweak, new MtuConfiguration(1500)));
        Assert.Equal(0, backend.MtuWrites);
    }

    [Fact]
    public async Task MtuPersistentExternalChangeBlocksRestore()
    {
        var backend = new MemoryBackend();
        var tweak = new MtuTweak(AdapterId, IpFamily.IPv4, backend);
        var engine = Engine;
        var result = await engine.ApplyAsync(tweak, await engine.PreviewAsync(tweak, new MtuConfiguration(1492)));
        backend.Mtu = backend.Mtu with { PersistentBytes = 1450 };
        await Assert.ThrowsAsync<ConfigurationConflictException>(async () => await engine.RestoreAsync(tweak, result.OperationId!.Value));
        Assert.Equal(1450u, backend.Mtu.PersistentBytes);
        Assert.Equal(1, backend.MtuWrites);
    }

    [Fact]
    public async Task PartiallyWrittenMtuIsRecordedAndRestorableFromExactObservedPartialState()
    {
        var backend = new MemoryBackend { WriteOnlyPersistentMtu = true };
        var tweak = new MtuTweak(AdapterId, IpFamily.IPv4, backend);
        var engine = Engine;
        var result = await engine.ApplyAsync(tweak, await engine.PreviewAsync(tweak, new MtuConfiguration(1492)));
        Assert.Equal(OperationStatus.PartiallyApplied, result.Status);
        Assert.Equal(1500u, backend.Mtu.ActiveBytes);
        Assert.Equal(1492u, backend.Mtu.PersistentBytes);
        Assert.Equal(1, backend.MtuWrites);
        Assert.NotNull(Assert.Single(await Journal.GetAllAsync()).LastObservedState);
        backend.WriteOnlyPersistentMtu = false;
        Assert.Equal(OperationStatus.Restored, (await engine.RestoreAsync(tweak, result.OperationId!.Value)).Status);
        Assert.Equal(1500u, backend.Mtu.ActiveBytes);
        Assert.Equal(1500u, backend.Mtu.PersistentBytes);
    }

    [Fact]
    public async Task ExternalChangeAfterPartialMtuReadbackBlocksRestoration()
    {
        var backend = new MemoryBackend { WriteOnlyPersistentMtu = true };
        var tweak = new MtuTweak(AdapterId, IpFamily.IPv4, backend);
        var engine = Engine;
        var result = await engine.ApplyAsync(tweak, await engine.PreviewAsync(tweak, new MtuConfiguration(1492)));
        Assert.Equal(OperationStatus.PartiallyApplied, result.Status);
        backend.Mtu = backend.Mtu with { ActiveBytes = 1450 };
        await Assert.ThrowsAsync<ConfigurationConflictException>(async () => await engine.RestoreAsync(tweak, result.OperationId!.Value));
        Assert.Equal(1450u, backend.Mtu.ActiveBytes);
        Assert.Equal(1, backend.MtuWrites);
    }

    [Fact]
    public async Task MtuRestorationRejectsAnotherAdapter()
    {
        var backend = new MemoryBackend();
        var tweak = new MtuTweak(AdapterId, IpFamily.IPv4, backend);
        await Assert.ThrowsAsync<ConfigurationConflictException>(async () =>
            await tweak.RestoreAsync(new(OtherAdapterId, IpFamily.IPv4, 1492, 1492, 1500), default));
        Assert.Equal(0, backend.MtuWrites);
    }

    [Fact]
    public async Task TcpManagedPolicyOrEffectiveMismatchIsUnsupported()
    {
        var backend = new MemoryBackend { Tcp = new(TcpTemplate.Internet, TcpAutoTuningLevel.Normal, TcpAutoTuningLevel.Restricted, "Restricted") };
        var tweak = new TcpAutoTuningTweak(TcpTemplate.Internet, backend);
        Assert.False((await tweak.CheckCompatibilityAsync(default)).IsSupported);
        backend.Tcp = backend.Tcp with { GroupPolicy = "NotConfigured" };
        Assert.False((await tweak.CheckCompatibilityAsync(default)).IsSupported);
        backend.Tcp = backend.Tcp with { Effective = TcpAutoTuningLevel.Normal };
        Assert.True((await tweak.CheckCompatibilityAsync(default)).IsSupported);
        Assert.Equal(0, backend.TcpWrites);
    }

    [Fact]
    public async Task TcpExperimentalValueIsNotExposedAndOtherChangesRequireExplicitRiskConsent()
    {
        var backend = new MemoryBackend();
        var tweak = new TcpAutoTuningTweak(TcpTemplate.Internet, backend);
        var engine = Engine;
        Assert.NotEmpty(tweak.Validate(TcpAutoTuningLevel.Experimental));
        Assert.NotEmpty(tweak.Validate((TcpAutoTuningLevel)999));
        await Assert.ThrowsAsync<ArgumentException>(async () => await engine.PreviewAsync(tweak, TcpAutoTuningLevel.Experimental));
        var preview = await engine.PreviewAsync(tweak, TcpAutoTuningLevel.Restricted);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(async () => await engine.ApplyAsync(tweak, preview));
        Assert.Equal(0, backend.TcpWrites);
        var result = await engine.ApplyAsync(tweak, preview, new ExecutionOptions(RiskAcknowledged: true));
        Assert.Equal(OperationStatus.Verified, result.Status);
        Assert.Equal(TcpAutoTuningLevel.Restricted, backend.Tcp.Local);
        Assert.Equal(TcpAutoTuningLevel.Restricted, backend.Tcp.Effective);
        await engine.RestoreAsync(tweak, result.OperationId!.Value);
        Assert.Equal(TcpAutoTuningLevel.Normal, backend.Tcp.Local);
        Assert.Equal(TcpAutoTuningLevel.Normal, backend.Tcp.Effective);
    }

    [Fact]
    public async Task TcpOriginalExperimentalConfigurationCanBeRestoredWithoutExposingItAsARequestedValue()
    {
        var backend = new MemoryBackend { Tcp = new(TcpTemplate.Internet, TcpAutoTuningLevel.Experimental, TcpAutoTuningLevel.Experimental, "NotConfigured") };
        var tweak = new TcpAutoTuningTweak(TcpTemplate.Internet, backend);
        var engine = Engine;
        var result = await engine.ApplyAsync(tweak, await engine.PreviewAsync(tweak, TcpAutoTuningLevel.Normal), new ExecutionOptions(true));
        await engine.RestoreAsync(tweak, result.OperationId!.Value);
        Assert.Equal(TcpAutoTuningLevel.Experimental, backend.Tcp.Local);
        Assert.Equal(TcpAutoTuningLevel.Experimental, backend.Tcp.Effective);
    }

    [Fact]
    public async Task RssApplyPreservesOtherPoliciesAndTracksRestartThroughRestore()
    {
        var backend = new MemoryBackend();
        var original = backend.Rss;
        var tweak = new RssTweak(AdapterId, backend);
        var engine = Engine;
        var result = await engine.ApplyAsync(tweak, await engine.PreviewAsync(tweak, false));
        Assert.Equal(OperationStatus.PendingRestart, result.Status);
        Assert.True(result.RestartRequired);
        Assert.Equal(original with { Enabled = false }, backend.Rss);
        var restored = await engine.RestoreAsync(tweak, result.OperationId!.Value);
        Assert.Equal(OperationStatus.Restored, restored.Status);
        Assert.True(restored.RestartRequired);
        Assert.Equal(original, backend.Rss);
    }

    [Fact]
    public async Task RssExternalProfileChangeBlocksBlindRestorationOfItsEnableFlag()
    {
        var backend = new MemoryBackend();
        var tweak = new RssTweak(AdapterId, backend);
        var engine = Engine;
        var result = await engine.ApplyAsync(tweak, await engine.PreviewAsync(tweak, false));
        backend.Rss = backend.Rss with { Profile = "NUMAStatic" };
        await Assert.ThrowsAsync<ConfigurationConflictException>(async () => await engine.RestoreAsync(tweak, result.OperationId!.Value));
        Assert.Equal("NUMAStatic", backend.Rss.Profile);
        Assert.False(backend.Rss.Enabled);
        Assert.Equal(1, backend.RssWrites);
    }

    [Fact]
    public async Task RssIncompleteDriverCapabilityIsUnsupported()
    {
        var backend = new MemoryBackend();
        var tweak = new RssTweak(AdapterId, backend);
        backend.Rss = backend.Rss with { NumberOfReceiveQueues = 0 };
        Assert.False((await tweak.CheckCompatibilityAsync(default)).IsSupported);
        Assert.Equal(0, backend.RssWrites);
    }

    [Fact]
    public async Task RssRestoreRejectsWrongInterface()
    {
        var backend = new MemoryBackend();
        var tweak = new RssTweak(AdapterId, backend);
        await Assert.ThrowsAsync<ConfigurationConflictException>(async () =>
            await tweak.RestoreAsync(backend.Rss with { AdapterId = OtherAdapterId }, default));
        Assert.Equal(0, backend.RssWrites);
    }

    [Fact]
    public async Task CorruptedTypedDnsSnapshotBlocksRestoreWithoutWriting()
    {
        var backend = new MemoryBackend();
        var tweak = new DnsTweak(AdapterId, IpFamily.IPv4, backend);
        var engine = Engine;
        var result = await engine.ApplyAsync(tweak, await engine.PreviewAsync(tweak, DnsPresets.Cloudflare));
        var record = Assert.Single(await Journal.GetAllAsync());
        await Journal.SaveAsync(record with { OriginalState = System.Text.Json.JsonSerializer.SerializeToElement(new { }) });
        await Assert.ThrowsAsync<RecoveryDataException>(async () => await engine.RestoreAsync(tweak, result.OperationId!.Value));
        Assert.Equal(1, backend.DnsWrites);
        Assert.Equal(DnsPresets.Cloudflare.Servers, backend.Dns.Servers);
    }

    [Fact]
    public void NetworkTweaksRequireStableAdapterIds()
    {
        var backend = new MemoryBackend();
        Assert.Throws<ArgumentException>(() => new DnsTweak(Guid.Empty, IpFamily.IPv4, backend));
        Assert.Throws<ArgumentException>(() => new MtuTweak(Guid.Empty, IpFamily.IPv4, backend));
        Assert.Throws<ArgumentException>(() => new RssTweak(Guid.Empty, backend));
        Assert.Throws<ArgumentException>(() => new TcpAutoTuningTweak((TcpTemplate)999, backend));
    }

    public void Dispose()
    {
        if (Directory.Exists(_journalPath)) Directory.Delete(_journalPath, recursive: true);
    }

    private sealed class AdministratorContext : IPrivilegeContext { public bool IsAdministrator => true; }

    private sealed class MemoryBackend : INetworkBackend
    {
        public DnsState Dns { get; set; } = new(AdapterId, IpFamily.IPv4, DnsSource.Automatic, []);
        public MtuState Mtu { get; set; } = new(AdapterId, IpFamily.IPv4, 1500, 1500, 1500);
        public TcpAutoTuningState Tcp { get; set; } = new(TcpTemplate.Internet, TcpAutoTuningLevel.Normal, TcpAutoTuningLevel.Normal, "NotConfigured");
        public RssState Rss { get; set; } = new(AdapterId, true, "ClosestStatic", 4, 0, 0, 0, 7, 4);
        public int DnsWrites { get; private set; }
        public int MtuWrites { get; private set; }
        public int TcpWrites { get; private set; }
        public int RssWrites { get; private set; }
        public bool WriteOnlyPersistentMtu { get; set; }

        public ValueTask<IReadOnlyList<NetworkAdapterInfo>> ListAdaptersAsync(CancellationToken cancellationToken = default)
            => ValueTask.FromResult<IReadOnlyList<NetworkAdapterInfo>>([new(AdapterId, "Ethernet", 7, true)]);
        public ValueTask<DnsState> ReadDnsAsync(Guid adapterId, IpFamily family, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(Dns with { Servers = Dns.Servers.ToArray() });
        public ValueTask WriteDnsAsync(DnsState state, CancellationToken cancellationToken = default)
        {
            DnsWrites++;
            Dns = state with { Servers = state.Servers.ToArray() };
            return ValueTask.CompletedTask;
        }
        public ValueTask<MtuState> ReadMtuAsync(Guid adapterId, IpFamily family, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(Mtu);
        public ValueTask WriteMtuAsync(MtuState state, CancellationToken cancellationToken = default)
        {
            MtuWrites++;
            Mtu = WriteOnlyPersistentMtu ? Mtu with { PersistentBytes = state.PersistentBytes } : state;
            return ValueTask.CompletedTask;
        }
        public ValueTask<TcpAutoTuningState> ReadTcpAutoTuningAsync(TcpTemplate template, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(Tcp);
        public ValueTask WriteTcpAutoTuningAsync(TcpTemplate template, TcpAutoTuningLevel level, CancellationToken cancellationToken = default)
        {
            TcpWrites++;
            Tcp = Tcp with { Template = template, Local = level, Effective = level };
            return ValueTask.CompletedTask;
        }
        public ValueTask<RssState> ReadRssAsync(Guid adapterId, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(Rss);
        public ValueTask WriteRssEnabledAsync(Guid adapterId, bool enabled, CancellationToken cancellationToken = default)
        {
            RssWrites++;
            Rss = Rss with { AdapterId = adapterId, Enabled = enabled };
            return ValueTask.CompletedTask;
        }
    }
}
