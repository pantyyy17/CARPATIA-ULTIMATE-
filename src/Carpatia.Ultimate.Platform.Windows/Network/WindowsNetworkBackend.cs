using System.ComponentModel;
using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace Carpatia.Ultimate.Platform.Windows.Network;

/// <summary>
/// DNS uses IP Helper; other settings use a fixed, embedded, allowlisted Windows
/// PowerShell script. User values travel as JSON over stdin, never executable text.
/// All writes independently require an elevated Windows token.
/// </summary>
public sealed class WindowsNetworkBackend : INetworkBackend
{
    private readonly PowerShellNetworkRunner _runner = new();

    public ValueTask<IReadOnlyList<NetworkAdapterInfo>> ListAdaptersAsync(CancellationToken cancellationToken = default)
    {
        RequireWindows();
        cancellationToken.ThrowIfCancellationRequested();
        var result = new List<NetworkAdapterInfo>();
        foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Guid.TryParse(adapter.Id, out var id) || id == Guid.Empty) continue;
            var properties = adapter.GetIPProperties();
            var index = adapter.Supports(NetworkInterfaceComponent.IPv4)
                ? properties.GetIPv4Properties().Index : properties.GetIPv6Properties().Index;
            result.Add(new(id, adapter.Name, index, adapter.OperationalStatus == OperationalStatus.Up));
        }
        return ValueTask.FromResult<IReadOnlyList<NetworkAdapterInfo>>(result.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToArray());
    }

    public ValueTask<DnsState> ReadDnsAsync(Guid adapterId, IpFamily family, CancellationToken cancellationToken = default)
    {
        RequireWindows();
        NetworkValidation.Adapter(adapterId);
        cancellationToken.ThrowIfCancellationRequested();
        if (family != IpFamily.IPv4)
            throw new NotSupportedException("IPv6 DNS source capture is not validated. IPv6 DNS changes are unavailable; existing IPv6 configuration is preserved.");
        RequireAdapter(adapterId);
        var settings = new NativeDnsSettings { Version = OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000) ? 3u : 1u };
        var code = GetInterfaceDnsSettings(adapterId, ref settings);
        if (code != 0) throw new Win32Exception((int)code, "Windows could not read interface DNS configuration.");
        try
        {
            if ((settings.Flags & 1) != 0 || !string.IsNullOrWhiteSpace(Marshal.PtrToStringUni(settings.ProfileNameServer)))
                throw new NotSupportedException("Profile-managed or ambiguous DNS configuration cannot be restored safely by this adapter.");
            // Header also defines DoH-profile, encrypted-DNS, DDR and DoT flags above 0x1000.
            // No unknown newer setting is silently accepted by this versioned adapter.
            if ((settings.Flags & ~0x0ffeUL) != 0 || settings.ServerPropertyCount != 0 || settings.ProfileServerPropertyCount != 0)
                throw new NotSupportedException("DNS-over-HTTPS interface properties are present. This adapter does not modify encrypted-DNS configurations.");
            var configured = Marshal.PtrToStringUni(settings.NameServer);
            if (settings.NameServer != IntPtr.Zero && string.IsNullOrWhiteSpace(configured))
                throw new NotSupportedException("The interface exposes an empty DNS override whose source cannot be restored unambiguously.");
            var servers = string.IsNullOrWhiteSpace(configured) ? []
                : configured.Split([' ', ',', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => IPAddress.TryParse(x, out var address) ? address.ToString() : x).ToArray();
            var source = servers.Length == 0 ? DnsSource.Automatic : DnsSource.Static;
            NetworkDefinitions.RequireNoErrors(NetworkValidation.Dns(new(source, servers), family));
            return ValueTask.FromResult(new DnsState(adapterId, family, source, servers));
        }
        finally { FreeInterfaceDnsSettings(ref settings); }
    }

    public async ValueTask WriteDnsAsync(DnsState state, CancellationToken cancellationToken = default)
    {
        RequireAdministrator();
        NetworkDefinitions.RequireNoErrors(NetworkValidation.Dns(new(state.Source, state.Servers), state.Family));
        _ = await ReadDnsAsync(state.AdapterId, state.Family, cancellationToken); // Recheck profile/DoH compatibility immediately before writing.
        var names = state.Source == DnsSource.Automatic ? null : string.Join(',', state.Servers);
        var pointer = names is null ? IntPtr.Zero : Marshal.StringToHGlobalUni(names);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var settings = new NativeDnsSettings { Version = 1, Flags = 0x0002, NameServer = pointer };
            var code = SetInterfaceDnsSettings(state.AdapterId, ref settings);
            if (code != 0) throw new Win32Exception((int)code, "Windows rejected the interface DNS configuration.");
        }
        finally { if (pointer != IntPtr.Zero) Marshal.FreeHGlobal(pointer); }
    }

    public async ValueTask<MtuState> ReadMtuAsync(Guid adapterId, IpFamily family, CancellationToken cancellationToken = default)
    {
        RequireWindows(); NetworkValidation.Adapter(adapterId);
        if (!Enum.IsDefined(family)) throw new ArgumentException("Unknown IP family.", nameof(family));
        return await _runner.ExecuteAsync<MtuState>(new { Operation = "ReadMtu", AdapterId = adapterId, Family = family.ToString() }, cancellationToken);
    }

    public async ValueTask WriteMtuAsync(MtuState state, CancellationToken cancellationToken = default)
    {
        RequireAdministrator(); NetworkValidation.Adapter(state.AdapterId);
        NetworkDefinitions.RequireNoErrors(NetworkValidation.Mtu(state.ActiveBytes, state.Family, state.LinkMtuBytes));
        NetworkDefinitions.RequireNoErrors(NetworkValidation.Mtu(state.PersistentBytes, state.Family, state.LinkMtuBytes));
        _ = await _runner.ExecuteAsync<bool>(new { Operation = "WriteMtu", state.AdapterId, Family = state.Family.ToString(),
            state.ActiveBytes, state.PersistentBytes }, cancellationToken);
    }

    public async ValueTask<TcpAutoTuningState> ReadTcpAutoTuningAsync(TcpTemplate template, CancellationToken cancellationToken = default)
    {
        RequireWindows();
        if (!Enum.IsDefined(template)) throw new ArgumentException("Unknown TCP template.", nameof(template));
        return await _runner.ExecuteAsync<TcpAutoTuningState>(new { Operation = "ReadTcp", Template = template.ToString() }, cancellationToken);
    }

    public async ValueTask WriteTcpAutoTuningAsync(TcpTemplate template, TcpAutoTuningLevel level, CancellationToken cancellationToken = default)
    {
        RequireAdministrator();
        if (!Enum.IsDefined(template) || !Enum.IsDefined(level)) throw new ArgumentException("Unknown TCP template or level.");
        _ = await _runner.ExecuteAsync<bool>(new { Operation = "WriteTcp", Template = template.ToString(), Level = level.ToString() }, cancellationToken);
    }

    public async ValueTask<RssState> ReadRssAsync(Guid adapterId, CancellationToken cancellationToken = default)
    {
        RequireWindows(); NetworkValidation.Adapter(adapterId);
        return await _runner.ExecuteAsync<RssState>(new { Operation = "ReadRss", AdapterId = adapterId }, cancellationToken);
    }

    public async ValueTask WriteRssEnabledAsync(Guid adapterId, bool enabled, CancellationToken cancellationToken = default)
    {
        RequireAdministrator(); NetworkValidation.Adapter(adapterId);
        _ = await _runner.ExecuteAsync<bool>(new { Operation = "WriteRss", AdapterId = adapterId, Enabled = enabled }, cancellationToken);
    }

    private static void RequireAdapter(Guid id)
    {
        if (NetworkInterface.GetAllNetworkInterfaces().Count(x => Guid.TryParse(x.Id, out var candidate) && candidate == id) != 1)
            throw new NotSupportedException("The selected adapter is missing or its identity is ambiguous. Select it again.");
    }

    private static void RequireWindows()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
            throw new PlatformNotSupportedException("Network configuration requires Windows 10 build 19041 or later.");
    }

    private static void RequireAdministrator()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
            throw new PlatformNotSupportedException("Network configuration requires Windows 10 build 19041 or later.");
        using var identity = WindowsIdentity.GetCurrent();
        if (!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
            throw new UnauthorizedAccessException("An elevated Windows token is required for network modifications.");
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeDnsSettings
    {
        public uint Version;
        public ulong Flags;
        public IntPtr Domain;
        public IntPtr NameServer;
        public IntPtr SearchList;
        public uint RegistrationEnabled;
        public uint RegisterAdapterName;
        public uint EnableLlmnr;
        public uint QueryAdapterName;
        public IntPtr ProfileNameServer;
        public uint DisableUnconstrainedQueries;
        public IntPtr SupplementalSearchList;
        public uint ServerPropertyCount;
        public IntPtr ServerProperties;
        public uint ProfileServerPropertyCount;
        public IntPtr ProfileServerProperties;
    }

    [DllImport("iphlpapi.dll", ExactSpelling = true)]
    private static extern uint GetInterfaceDnsSettings(Guid adapterId, ref NativeDnsSettings settings);
    [DllImport("iphlpapi.dll", ExactSpelling = true)]
    private static extern uint SetInterfaceDnsSettings(Guid adapterId, ref NativeDnsSettings settings);
    [DllImport("iphlpapi.dll", ExactSpelling = true)]
    private static extern void FreeInterfaceDnsSettings(ref NativeDnsSettings settings);
}
