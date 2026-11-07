# First network operations

Status: **Implemented, Hardware Test Pending**. Injected-backend tests exercise
planning, validation, persistence, repeated operations, partial failure and
restoration. No DNS, MTU, TCP or RSS writes were executed on Windows hardware in
the Linux development environment.

All modifying operations require both the engine's administrator context and a
fresh elevated-token check at the actual Windows boundary. Adapter operations
use a stable interface GUID. The PowerShell provider re-resolves that GUID to an
index or CIM object for every request; adapter aliases never become executable
code or wildcard targets.

## DNS

`GetInterfaceDnsSettings` reads configured IPv4 `NameServer` values through
IP Helper; these are static adapter overrides, not an effective DHCP server list.
A null override is represented as Automatic; ordered static addresses are
recorded independently. Empty non-null overrides are rejected because their
source cannot be distinguished safely. The setter changes only the Nameserver
field, preserving domain, search suffixes and registration controls.

Cloudflare, Quad9, Google and custom canonical IPv4 addresses are supported.
Restoring Automatic removes the static override rather than converting old DHCP
servers into a permanent static list. Verification checks the configured source
and server order. It does not promise that DNS queries succeed or that a DNS
change reduces game latency.

Native retrieval requires `Flags` to be empty and does not document independent
IPv6 source capture. Consequently **IPv6 DNS writes are Unsupported** in this
milestone; existing IPv6 settings are preserved. Native profile overrides,
encrypted-DNS server properties, unexpected flags and ambiguous configurations
are rejected. DNS-over-HTTPS/DoT configuration and NRPT policy management are not
implemented. The API writes are not a promise to override organization policy.

Windows 10 build 19041 is the minimum. Windows 11 uses the version 3 structure to
inspect encrypted-server properties. The x64 structure layout is checked against
the official SDK header by an automated ABI test.

## MTU

The adapter independently captures the selected IP family's `ActiveStore` and
`PersistentStore` through `Get-NetIPInterface`. Both stores and the driver's
`MSFT_NetAdapter.MtuSize` must be readable and unambiguous; unsupported/absent
store states are rejected instead of creating an unrestorable configuration.

Explicit `netsh interface ipv4|ipv6 set subinterface` calls set the persistent
store first and active store second. Restoration writes their original values
independently. A failed second write can be partial; the engine's recorded
readback permits recovery only while that exact partial state remains present.
The accepted range is 576–65535 for IPv4 and 1280–65535 for IPv6, additionally
bounded by the adapter's reported supported link MTU. This is not path-MTU
discovery; 1500 is never selected automatically.

## TCP auto-tuning

`Get-NetTCPSetting` captures Local, Effective and GroupPolicy values for one
allowlisted template (`Internet`, `InternetCustom` or `DatacenterCustom`). Only
unmanaged templates whose local/effective values agree are writable.
`Set-NetTCPSetting -AutoTuningLevelLocal` changes that one field only. The
Experimental setting is not offered for new changes; an originally captured
Experimental value can be restored. The whole operation is classified
Experimental and therefore requires explicit risk acknowledgement in the engine.
Existing connections can retain previously negotiated behavior.

## Receive Side Scaling

`Get-NetAdapterRss` must expose exactly one supported configuration for the
selected adapter. Only `Enabled` is modified using `Set-NetAdapterRss -InputObject
-Enabled -NoRestart`. The profile, receive queues and processor settings remain
in the snapshot and are checked for unexpected changes. A restart is reported
after both Apply and Restore. No adapter restart is performed automatically.

## Process boundary

The only process launched is the system Windows PowerShell executable, with a
fixed embedded script and six allowlisted operations. Request data is JSON on
stdin; no request value is inserted into executable script text. Module discovery
is limited to the Windows system module directory. Output is bounded to 1 MiB
per stream and every request has a 30-second deadline. A timeout can occur after
a partial write; the engine must reread and retain its recovery record.

Windows CI includes a PowerShell 5.1 parse-only test. It executes no network
operations. The x64 ABI and non-Windows failure paths are tested locally.

## Sources checked

- [IP Helper DNS retrieval](https://learn.microsoft.com/windows/win32/api/netioapi/nf-netioapi-getinterfacednssettings)
- [IP Helper DNS setting](https://learn.microsoft.com/windows/win32/api/netioapi/nf-netioapi-setinterfacednssettings)
- [DNS settings fields](https://learn.microsoft.com/windows/win32/api/netioapi/ns-netioapi-dns_interface_settings)
- [Official SDK header](https://github.com/microsoft/win32metadata/blob/main/generation/WinSDK/RecompiledIdlHeaders/shared/netioapi.h)
- [MTU policy-store reads](https://learn.microsoft.com/powershell/module/nettcpip/get-netipinterface)
- [Netsh interface commands](https://learn.microsoft.com/windows-server/networking/technologies/netsh/netsh-interface)
- [Adapter MtuSize](https://learn.microsoft.com/windows/win32/fwp/wmi/netadaptercimprov/msft-netadapter)
- [TCP template reads](https://learn.microsoft.com/powershell/module/nettcpip/get-nettcpsetting)
- [TCP template changes](https://learn.microsoft.com/powershell/module/nettcpip/set-nettcpsetting)
- [RSS reads](https://learn.microsoft.com/powershell/module/netadapter/get-netadapterrss)
- [RSS changes](https://learn.microsoft.com/powershell/module/netadapter/set-netadapterrss)
