using Carpatia.Ultimate.Core;

namespace Carpatia.Ultimate.Platform.Windows.Network;

internal static class NetworkDefinitions
{
    public static TweakDefinition Create(string id, string resource, string name, string description,
        string[] values, string impact, string[] documentation, bool restart = false, RiskClass risk = RiskClass.Advanced)
        => new(id, resource, name, description, "Network", risk, PrivilegeRequirement.Administrator,
            ["Windows 11", "Windows 10 2004 or later"], ["Supported local Windows network configuration API"],
            values, [], documentation.Select(x => new Uri(x)).ToArray(), impact, RequiresRestart: restart,
            RequiresSuccessfulWrite: true);

    public static async ValueTask<Compatibility> Probe(Func<ValueTask> probe)
    {
        try { await probe(); return Compatibility.Supported("The resource was read through a supported Windows interface."); }
        catch (OperationCanceledException) { throw; }
        catch (Exception error) when (error is PlatformNotSupportedException or NotSupportedException
            or InvalidOperationException or IOException or System.ComponentModel.Win32Exception or UnauthorizedAccessException or ArgumentException)
        { return Compatibility.Unsupported(error.Message); }
    }

    public static void RequireNoErrors(IReadOnlyList<string> errors)
    {
        if (errors.Count != 0) throw new ArgumentException(string.Join(" ", errors));
    }
}

public sealed class DnsTweak : ITweak<DnsState, DnsConfiguration>
{
    private readonly INetworkBackend _backend;
    private readonly Guid _adapterId;
    private readonly IpFamily _family;
    public DnsTweak(Guid adapterId, IpFamily family, INetworkBackend backend)
    {
        NetworkValidation.Adapter(adapterId);
        if (!Enum.IsDefined(family)) throw new ArgumentException("Unknown IP address family.", nameof(family));
        _adapterId = adapterId; _family = family; _backend = backend;
        Definition = NetworkDefinitions.Create($"network.dns.{adapterId:N}.{family}", $"network/{adapterId:N}/dns/{family}",
            $"{family} DNS servers", "Configure ordered DNS servers for one selected adapter. Automatic restores DHCP DNS acquisition. "
            + "Profile-managed DNS, DNS-over-HTTPS and ambiguous family configurations are unavailable in this milestone.",
            ["Automatic", "Cloudflare", "Quad9", "Google", "Custom"],
            "DNS can affect name-resolution time. It does not inherently reduce game-server latency.",
            ["https://learn.microsoft.com/windows/win32/api/netioapi/nf-netioapi-getinterfacednssettings",
             "https://learn.microsoft.com/windows/win32/api/netioapi/nf-netioapi-setinterfacednssettings",
             "https://learn.microsoft.com/windows/win32/api/netioapi/ns-netioapi-dns_interface_settings"]);
    }
    public TweakDefinition Definition { get; }
    public ValueTask<Compatibility> CheckCompatibilityAsync(CancellationToken cancellationToken)
        => NetworkDefinitions.Probe(async () => { _ = await ReadAsync(cancellationToken); });
    public async ValueTask<DnsState> ReadAsync(CancellationToken cancellationToken)
    {
        var state = await _backend.ReadDnsAsync(_adapterId, _family, cancellationToken);
        EnsureIdentity(state);
        NetworkDefinitions.RequireNoErrors(NetworkValidation.Dns(new(state.Source, state.Servers), _family));
        return state;
    }
    public IReadOnlyList<string> Validate(DnsConfiguration requestedValue) => NetworkValidation.Dns(requestedValue, _family);
    public DnsState Project(DnsState currentState, DnsConfiguration requestedValue)
    {
        EnsureIdentity(currentState);
        NetworkDefinitions.RequireNoErrors(Validate(requestedValue));
        return currentState with { Source = requestedValue.Source, Servers = requestedValue.Servers.ToArray() };
    }
    public bool StatesEqual(DnsState left, DnsState right) => left.AdapterId == right.AdapterId && left.Family == right.Family
        && left.Source == right.Source && left.Servers.SequenceEqual(right.Servers, StringComparer.Ordinal);
    public string DescribeChange(DnsState currentState, DnsState expectedState)
        => $"Adapter {_adapterId:D}, {_family}: {Describe(currentState)} → {Describe(expectedState)}. Only DNS configuration changes.";
    public ValueTask ApplyAsync(DnsConfiguration requestedValue, CancellationToken cancellationToken)
    {
        NetworkDefinitions.RequireNoErrors(Validate(requestedValue));
        return _backend.WriteDnsAsync(new(_adapterId, _family, requestedValue.Source, requestedValue.Servers.ToArray()), cancellationToken);
    }
    public ValueTask RestoreAsync(DnsState originalState, CancellationToken cancellationToken)
    {
        EnsureIdentity(originalState);
        NetworkDefinitions.RequireNoErrors(NetworkValidation.Dns(new(originalState.Source, originalState.Servers), _family));
        return _backend.WriteDnsAsync(originalState, cancellationToken);
    }
    private void EnsureIdentity(DnsState state)
    {
        if (state.AdapterId != _adapterId || state.Family != _family)
            throw new ConfigurationConflictException("The DNS snapshot belongs to another interface or address family.");
    }
    private static string Describe(DnsState state) => state.Source == DnsSource.Automatic ? "Automatic (DHCP)" : string.Join(", ", state.Servers);
}

public sealed class MtuTweak : ITweak<MtuState, MtuConfiguration>
{
    private readonly Guid _adapterId;
    private readonly IpFamily _family;
    private readonly INetworkBackend _backend;
    public MtuTweak(Guid adapterId, IpFamily family, INetworkBackend backend)
    {
        NetworkValidation.Adapter(adapterId);
        if (!Enum.IsDefined(family)) throw new ArgumentException("Unknown IP address family.", nameof(family));
        _adapterId = adapterId; _family = family; _backend = backend;
        Definition = NetworkDefinitions.Create($"network.mtu.{adapterId:N}.{family}", $"network/{adapterId:N}/mtu/{family}",
            $"{family} interface MTU", "Set the selected interface's active and persistent MTU. Original stores are recorded independently. "
            + "1500 is not suitable for every connection; this control does not run path-MTU discovery.",
            ["1492", "1500", "Custom"], "An unsuitable MTU can interrupt connectivity. No automatic best-MTU claim is made.",
            ["https://learn.microsoft.com/powershell/module/nettcpip/get-netipinterface",
             "https://learn.microsoft.com/windows-server/networking/technologies/netsh/netsh-interface"]);
    }
    public TweakDefinition Definition { get; }
    public ValueTask<Compatibility> CheckCompatibilityAsync(CancellationToken cancellationToken)
        => NetworkDefinitions.Probe(async () => { _ = await ReadAsync(cancellationToken); });
    public async ValueTask<MtuState> ReadAsync(CancellationToken cancellationToken)
    {
        var state = await _backend.ReadMtuAsync(_adapterId, _family, cancellationToken);
        EnsureState(state);
        return state;
    }
    public IReadOnlyList<string> Validate(MtuConfiguration requestedValue) => NetworkValidation.Mtu(requestedValue.Bytes, _family);
    public MtuState Project(MtuState currentState, MtuConfiguration requestedValue)
    {
        EnsureState(currentState);
        NetworkDefinitions.RequireNoErrors(NetworkValidation.Mtu(requestedValue.Bytes, _family, currentState.LinkMtuBytes));
        return currentState with { ActiveBytes = requestedValue.Bytes, PersistentBytes = requestedValue.Bytes };
    }
    public bool StatesEqual(MtuState left, MtuState right) => left == right;
    public string DescribeChange(MtuState currentState, MtuState expectedState)
        => $"Adapter {_adapterId:D}, {_family}: active {currentState.ActiveBytes}, persistent {currentState.PersistentBytes} "
            + $"→ {expectedState.ActiveBytes} bytes in both stores. Connectivity can be interrupted.";
    public async ValueTask ApplyAsync(MtuConfiguration requestedValue, CancellationToken cancellationToken)
    {
        var current = await ReadAsync(cancellationToken);
        await _backend.WriteMtuAsync(Project(current, requestedValue), cancellationToken);
    }
    public ValueTask RestoreAsync(MtuState originalState, CancellationToken cancellationToken)
    {
        EnsureState(originalState);
        return _backend.WriteMtuAsync(originalState, cancellationToken);
    }
    private void EnsureState(MtuState state)
    {
        if (state.AdapterId != _adapterId || state.Family != _family)
            throw new ConfigurationConflictException("The MTU snapshot belongs to another interface or family.");
        if (state.LinkMtuBytes == 0 || state.LinkMtuBytes > 65535)
            throw new NotSupportedException("The adapter did not report a usable link MTU.");
        NetworkDefinitions.RequireNoErrors(NetworkValidation.Mtu(state.ActiveBytes, _family, state.LinkMtuBytes));
        NetworkDefinitions.RequireNoErrors(NetworkValidation.Mtu(state.PersistentBytes, _family, state.LinkMtuBytes));
    }
}

public sealed class TcpAutoTuningTweak : ITweak<TcpAutoTuningState, TcpAutoTuningLevel>
{
    private readonly TcpTemplate _template;
    private readonly INetworkBackend _backend;
    public TcpAutoTuningTweak(TcpTemplate template, INetworkBackend backend)
    {
        if (!Enum.IsDefined(template)) throw new ArgumentException("Unknown TCP template.", nameof(template));
        _template = template; _backend = backend;
        Definition = NetworkDefinitions.Create($"network.tcp.autotuning.{template}", $"network/tcp/{template}/autotuning",
            $"TCP auto-tuning · {template}", "Configure receive-window auto-tuning for this TCP template only. "
            + "Managed policy overrides are unsupported. Existing connections may retain their negotiated behavior.",
            ["Disabled", "HighlyRestricted", "Restricted", "Normal"],
            "Receive-window limits can reduce throughput. They do not guarantee lower latency.",
            ["https://learn.microsoft.com/powershell/module/nettcpip/get-nettcpsetting",
             "https://learn.microsoft.com/powershell/module/nettcpip/set-nettcpsetting"], risk: RiskClass.Experimental);
    }
    public TweakDefinition Definition { get; }
    public ValueTask<Compatibility> CheckCompatibilityAsync(CancellationToken cancellationToken)
        => NetworkDefinitions.Probe(async () => { _ = await ReadAsync(cancellationToken); });
    public async ValueTask<TcpAutoTuningState> ReadAsync(CancellationToken cancellationToken)
    {
        var state = await _backend.ReadTcpAutoTuningAsync(_template, cancellationToken);
        EnsureState(state);
        return state;
    }
    public IReadOnlyList<string> Validate(TcpAutoTuningLevel requestedValue)
        => !Enum.IsDefined(requestedValue) || requestedValue == TcpAutoTuningLevel.Experimental
            ? ["Select a supported documented TCP level. The Experimental level is not exposed."] : [];
    public TcpAutoTuningState Project(TcpAutoTuningState currentState, TcpAutoTuningLevel requestedValue)
    {
        EnsureState(currentState);
        NetworkDefinitions.RequireNoErrors(Validate(requestedValue));
        return currentState with { Local = requestedValue, Effective = requestedValue };
    }
    public bool StatesEqual(TcpAutoTuningState left, TcpAutoTuningState right) => left == right;
    public string DescribeChange(TcpAutoTuningState currentState, TcpAutoTuningState expectedState)
        => $"TCP {_template}: local/effective {currentState.Local}/{currentState.Effective} → {expectedState.Local}. "
            + "This is a template configuration change; performance must be measured separately.";
    public ValueTask ApplyAsync(TcpAutoTuningLevel requestedValue, CancellationToken cancellationToken)
    {
        NetworkDefinitions.RequireNoErrors(Validate(requestedValue));
        return _backend.WriteTcpAutoTuningAsync(_template, requestedValue, cancellationToken);
    }
    public ValueTask RestoreAsync(TcpAutoTuningState originalState, CancellationToken cancellationToken)
    {
        EnsureState(originalState);
        return _backend.WriteTcpAutoTuningAsync(_template, originalState.Local, cancellationToken);
    }
    private void EnsureState(TcpAutoTuningState state)
    {
        if (state.Template != _template) throw new ConfigurationConflictException("The TCP snapshot belongs to another template.");
        if (!Enum.IsDefined(state.Local) || !Enum.IsDefined(state.Effective)
            || !string.Equals(state.GroupPolicy, "NotConfigured", StringComparison.Ordinal)
            || state.Local != state.Effective)
            throw new NotSupportedException("TCP policy is managed, unknown or differs from its effective value. Automatic restoration is unavailable.");
    }
}

public sealed class RssTweak : ITweak<RssState, bool>
{
    private readonly Guid _adapterId;
    private readonly INetworkBackend _backend;
    public RssTweak(Guid adapterId, INetworkBackend backend)
    {
        NetworkValidation.Adapter(adapterId);
        _adapterId = adapterId; _backend = backend;
        Definition = NetworkDefinitions.Create($"network.rss.{adapterId:N}", $"network/{adapterId:N}/rss",
            "Receive Side Scaling", "Enable or disable RSS only on an adapter whose driver exposes the supported RSS interface. "
            + "The adapter is not automatically restarted; restart Windows after configuration changes.",
            ["Enabled", "Disabled"], "RSS distributes receive processing. Effects depend on the workload, adapter and driver.",
            ["https://learn.microsoft.com/powershell/module/netadapter/get-netadapterrss",
             "https://learn.microsoft.com/powershell/module/netadapter/set-netadapterrss"], restart: true);
    }
    public TweakDefinition Definition { get; }
    public ValueTask<Compatibility> CheckCompatibilityAsync(CancellationToken cancellationToken)
        => NetworkDefinitions.Probe(async () => { _ = await ReadAsync(cancellationToken); });
    public async ValueTask<RssState> ReadAsync(CancellationToken cancellationToken)
    {
        var state = await _backend.ReadRssAsync(_adapterId, cancellationToken);
        EnsureState(state);
        return state;
    }
    public IReadOnlyList<string> Validate(bool requestedValue) => [];
    public RssState Project(RssState currentState, bool requestedValue)
    {
        EnsureState(currentState);
        return currentState with { Enabled = requestedValue };
    }
    public bool StatesEqual(RssState left, RssState right) => left == right;
    public string DescribeChange(RssState currentState, RssState expectedState)
        => $"Adapter {_adapterId:D}: RSS {currentState.Enabled} → {expectedState.Enabled}. Restart is required; other RSS policies are preserved.";
    public ValueTask ApplyAsync(bool requestedValue, CancellationToken cancellationToken)
        => _backend.WriteRssEnabledAsync(_adapterId, requestedValue, cancellationToken);
    public ValueTask RestoreAsync(RssState originalState, CancellationToken cancellationToken)
    {
        EnsureState(originalState);
        return _backend.WriteRssEnabledAsync(_adapterId, originalState.Enabled, cancellationToken);
    }
    private void EnsureState(RssState state)
    {
        if (state.AdapterId != _adapterId) throw new ConfigurationConflictException("The RSS snapshot belongs to another adapter.");
        if (string.IsNullOrWhiteSpace(state.Profile) || state.NumberOfReceiveQueues == 0 || state.MaxProcessors == 0)
            throw new NotSupportedException("The driver did not expose a complete supported RSS configuration.");
    }
}
