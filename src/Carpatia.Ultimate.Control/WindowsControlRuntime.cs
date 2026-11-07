using System.Security.Principal;
using Carpatia.Ultimate.Broker;
using Carpatia.Ultimate.Core;
using Carpatia.Ultimate.Engine;
using Carpatia.Ultimate.Platform.Windows.Cpu;
using Carpatia.Ultimate.Platform.Windows.Network;
using NetTcpLevel = Carpatia.Ultimate.Platform.Windows.Network.TcpAutoTuningLevel;

namespace Carpatia.Ultimate.Control;

public sealed class WindowsPrivilegeContext : IPrivilegeContext
{
    public bool IsAdministrator
    {
        get
        {
            if (!OperatingSystem.IsWindows()) return false;
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
    }
}

public sealed record PlatformInventory(Guid? ActiveSchemeId, IReadOnlyList<NetworkAdapterInfo> Adapters, string Notice);

/// <summary>The allowlist is source-defined. Requests cannot choose a process, script, path or registry key.</summary>
public sealed class WindowsControlRuntime
{
    private readonly IPowerPolicyApi _power;
    private readonly INetworkBackend _network;
    public ControlDispatcher Dispatcher { get; }

    public WindowsControlRuntime(IOperationJournal journal, IPowerPolicyApi? power = null,
        INetworkBackend? network = null, IPrivilegeContext? privileges = null)
    {
        _power = power ?? new NativePowerPolicyApi();
        _network = network ?? new WindowsNetworkBackend();
        var rights = privileges ?? new WindowsPrivilegeContext();
        var engine = new TweakEngine(journal, rights);
        var handlers = new List<IControlHandler>();
        AddCpu(handlers, engine, OperationKind.CpuMinimum, CpuPowerSetting.MinimumProcessorState, "minimum-state");
        AddCpu(handlers, engine, OperationKind.CpuMaximum, CpuPowerSetting.MaximumProcessorState, "maximum-state");
        AddCpu(handlers, engine, OperationKind.CpuEnergyPreference, CpuPowerSetting.EnergyPerformancePreference, "energy-preference");
        AddCpu(handlers, engine, OperationKind.CpuBoost, CpuPowerSetting.BoostMode, "boost-mode");
        handlers.Add(new TweakControlHandler<DnsState, DnsConfiguration>(OperationKind.Dns, engine,
            r => new DnsTweak(r.AdapterId!.Value, Family(r), _network),
            r => new(r.DnsAutomatic == true ? DnsSource.Automatic : DnsSource.Static, r.DnsServers!.ToArray()),
            r => r.TweakId.StartsWith("network.dns.", StringComparison.Ordinal),
            r => { var s = StateJson.Read<DnsState>(r.OriginalState); return new DnsTweak(s.AdapterId, s.Family, _network); }));
        handlers.Add(new TweakControlHandler<MtuState, MtuConfiguration>(OperationKind.Mtu, engine,
            r => new MtuTweak(r.AdapterId!.Value, Family(r), _network), r => new(r.NumericValue!.Value),
            r => r.TweakId.StartsWith("network.mtu.", StringComparison.Ordinal),
            r => { var s = StateJson.Read<MtuState>(r.OriginalState); return new MtuTweak(s.AdapterId, s.Family, _network); }));
        handlers.Add(new TweakControlHandler<TcpAutoTuningState, NetTcpLevel>(OperationKind.TcpAutoTuning, engine,
            _ => new TcpAutoTuningTweak(TcpTemplate.Internet, _network), r => Enum.Parse<NetTcpLevel>(r.TcpLevel!.Value.ToString()),
            r => r.TweakId.StartsWith("network.tcp.autotuning.", StringComparison.Ordinal),
            r => { var s = StateJson.Read<TcpAutoTuningState>(r.OriginalState); return new TcpAutoTuningTweak(s.Template, _network); }));
        handlers.Add(new TweakControlHandler<RssState, bool>(OperationKind.ReceiveSideScaling, engine,
            r => new RssTweak(r.AdapterId!.Value, _network), r => r.Enabled!.Value,
            r => r.TweakId.StartsWith("network.rss.", StringComparison.Ordinal),
            r => { var s = StateJson.Read<RssState>(r.OriginalState); return new RssTweak(s.AdapterId, _network); }));
        Dispatcher = new(journal, handlers, rights);
    }

    public Task<PlatformInventory> ReadInventoryAsync(CancellationToken cancellationToken = default) => Task.Run(async () =>
    {
        Guid? scheme = null;
        var notices = new List<string>();
        try { if (_power.IsSupported) scheme = _power.GetActiveScheme(); else notices.Add("Windows power policies are unavailable."); }
        catch (Exception error) when (error is not OutOfMemoryException) { notices.Add(error.Message); }
        IReadOnlyList<NetworkAdapterInfo> adapters = [];
        try { adapters = await _network.ListAdaptersAsync(cancellationToken); }
        catch (OperationCanceledException) { throw; }
        catch (Exception error) when (error is not OutOfMemoryException) { notices.Add(error.Message); }
        return new PlatformInventory(scheme, adapters, string.Join("\n", notices));
    }, cancellationToken);

    private void AddCpu(List<IControlHandler> handlers, TweakEngine engine, OperationKind kind, CpuPowerSetting setting, string key)
    {
        handlers.Add(new TweakControlHandler<CpuPowerState, uint>(kind, engine,
            r => new CpuPowerPolicyTweak(_power, r.SchemeId!.Value, r.PowerSource == PowerSource.Ac ? PowerPolicySource.Ac : PowerPolicySource.Dc, setting),
            r => r.NumericValue!.Value,
            r => r.TweakId.StartsWith($"cpu.{key}.", StringComparison.Ordinal),
            r =>
            {
                var s = StateJson.Read<CpuPowerState>(r.OriginalState);
                if (s.Setting != setting) throw new RecoveryDataException("The stored CPU setting does not match the selected operation kind.");
                return new CpuPowerPolicyTweak(_power, s.SchemeId, s.Source, setting);
            },
            (current, original) => current with { Value = original.Value }));
    }

    private static IpFamily Family(ControlRequest request) => request.AddressFamily == NetworkFamily.IPv4 ? IpFamily.IPv4 : IpFamily.IPv6;
}
