using System.Net;
using System.Net.Sockets;

namespace Carpatia.Ultimate.Platform.Windows.Network;

public enum IpFamily { IPv4, IPv6 }
public enum DnsSource { Automatic, Static }
public enum TcpTemplate { Internet, InternetCustom, DatacenterCustom }
public enum TcpAutoTuningLevel { Disabled, HighlyRestricted, Restricted, Normal, Experimental }

public sealed record NetworkAdapterInfo(Guid Id, string Name, int InterfaceIndex, bool IsUp);
public sealed record DnsConfiguration(DnsSource Source, string[] Servers);
public sealed record DnsState(Guid AdapterId, IpFamily Family, DnsSource Source, string[] Servers);
public sealed record MtuConfiguration(uint Bytes);
public sealed record MtuState(Guid AdapterId, IpFamily Family, uint ActiveBytes, uint PersistentBytes, uint LinkMtuBytes);
public sealed record TcpAutoTuningState(TcpTemplate Template, TcpAutoTuningLevel Local,
    TcpAutoTuningLevel Effective, string GroupPolicy);
public sealed record RssState(Guid AdapterId, bool Enabled, string Profile, uint NumberOfReceiveQueues,
    ushort BaseProcessorGroup, byte BaseProcessorNumber, ushort MaxProcessorGroup,
    byte MaxProcessorNumber, uint MaxProcessors);

/// <summary>Only typed, allowlisted network operations cross this boundary.</summary>
public interface INetworkBackend
{
    ValueTask<IReadOnlyList<NetworkAdapterInfo>> ListAdaptersAsync(CancellationToken cancellationToken = default);
    ValueTask<DnsState> ReadDnsAsync(Guid adapterId, IpFamily family, CancellationToken cancellationToken = default);
    ValueTask WriteDnsAsync(DnsState state, CancellationToken cancellationToken = default);
    ValueTask<MtuState> ReadMtuAsync(Guid adapterId, IpFamily family, CancellationToken cancellationToken = default);
    ValueTask WriteMtuAsync(MtuState state, CancellationToken cancellationToken = default);
    ValueTask<TcpAutoTuningState> ReadTcpAutoTuningAsync(TcpTemplate template, CancellationToken cancellationToken = default);
    ValueTask WriteTcpAutoTuningAsync(TcpTemplate template, TcpAutoTuningLevel level, CancellationToken cancellationToken = default);
    ValueTask<RssState> ReadRssAsync(Guid adapterId, CancellationToken cancellationToken = default);
    ValueTask WriteRssEnabledAsync(Guid adapterId, bool enabled, CancellationToken cancellationToken = default);
}

public static class DnsPresets
{
    public static DnsConfiguration Automatic => new(DnsSource.Automatic, []);
    public static DnsConfiguration Cloudflare => new(DnsSource.Static, ["1.1.1.1", "1.0.0.1"]);
    public static DnsConfiguration Quad9 => new(DnsSource.Static, ["9.9.9.9", "149.112.112.112"]);
    public static DnsConfiguration Google => new(DnsSource.Static, ["8.8.8.8", "8.8.4.4"]);
}

public static class NetworkValidation
{
    public static IReadOnlyList<string> Dns(DnsConfiguration value, IpFamily family)
    {
        var errors = new List<string>();
        if (!Enum.IsDefined(value.Source) || !Enum.IsDefined(family)) errors.Add("Unknown DNS source or address family.");
        if (value.Servers is null) return ["A DNS server list is required (empty for Automatic)."];
        if (value.Source == DnsSource.Automatic && value.Servers.Length != 0)
            errors.Add("Automatic DNS must not contain a static server list.");
        if (value.Source == DnsSource.Static && value.Servers.Length is < 1 or > 8)
            errors.Add("Static DNS requires one to eight ordered server addresses.");
        var parsed = new HashSet<IPAddress>();
        foreach (var text in value.Servers)
        {
            // Require canonical IP notation, not permissive legacy numeric IPv4 forms.
            if (!IPAddress.TryParse(text, out var address) || address.ToString() != text
                || address.AddressFamily != (family == IpFamily.IPv4 ? AddressFamily.InterNetwork : AddressFamily.InterNetworkV6)
                || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any)
                || address.Equals(IPAddress.Broadcast) || address.IsIPv6Multicast
                || (address.AddressFamily == AddressFamily.InterNetwork && address.GetAddressBytes()[0] is >= 224)
                || (address.AddressFamily == AddressFamily.InterNetworkV6 && address.ScopeId != 0))
                errors.Add($"'{text}' is not a supported canonical unicast {family} DNS address.");
            else if (!parsed.Add(address)) errors.Add("Duplicate DNS server addresses are not allowed.");
        }
        return errors;
    }

    public static IReadOnlyList<string> Mtu(uint bytes, IpFamily family, uint? linkMtu = null)
    {
        var minimum = family == IpFamily.IPv6 ? 1280u : 576u;
        if (!Enum.IsDefined(family)) return ["Unknown IP address family."];
        if (bytes < minimum || bytes > 65535) return [$"{family} MTU must be {minimum}–65535 bytes."];
        if (linkMtu is > 0 && bytes > linkMtu) return ["MTU exceeds this adapter's reported link MTU."];
        return [];
    }

    public static void Adapter(Guid id)
    {
        if (id == Guid.Empty) throw new ArgumentException("A stable, nonempty adapter GUID is required.", nameof(id));
    }
}
