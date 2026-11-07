# Phase B operations and limitations

All native modification paths below are **Implemented, Hardware Test Pending**.
Their isolated backend tests pass, but no live Windows write was executed in the
Linux development environment. Eight operation families establish the initial
catalog; they are not a broad collection of unverified performance tweaks.

| Operation | Selected scope | Verification and restoration | Current limitations |
| --- | --- | --- | --- |
| CPU minimum state | Active plan GUID, AC or DC | Exact original DWORD; stored-value readback and successful plan reapplication | Must fit native metadata and current maximum; 100% is explicit, never automatic |
| CPU maximum state | Active plan GUID, AC or DC | Same, independently of minimum | Cannot fall below current minimum |
| Energy Performance Preference | Active plan GUID, AC or DC | Exact original index and successful plan reapplication | Available only when Windows exposes validated policy metadata |
| CPU boost mode | Active plan GUID, AC or DC | Enumerated original index and successful plan reapplication | Only detected documented modes 0–6; no voltage or overclocking interface |
| DNS | Stable adapter GUID, IPv4 | Configured source and ordered static server list; Automatic removes the override | IPv6 DNS, DoH/DoT, profile overrides and ambiguous states unsupported |
| MTU | Stable adapter GUID, IPv4 or IPv6 | Active and persistent stores captured/restored independently | Both stores and link MTU must be readable; no path-MTU discovery |
| TCP auto-tuning | One supported TCP template | Local/effective/policy state; only the local auto-tuning field changes | Experimental risk classification; managed or inconsistent policy blocked |
| Receive Side Scaling | Stable adapter GUID | Enabled state plus unchanged RSS context; resource-specific restore | Driver must expose supported RSS state; restart reported, no automatic adapter restart |

## CPU policy behavior

The selected plan must remain active. The adapter never deliberately selects a
new plan. Original values include AC/DC and policy identity. Min/max companion
values are read-only validation context, not extra settings restored implicitly.
A 100% minimum policy does not mean 100% CPU utilization and can increase heat
and power consumption.

Native APIs can persist a value before plan reapplication fails. CPU definitions
therefore require successful write completion as well as matching readback. Such
failures remain Partially Applied or Recovery Required, including on Restore.
Windows overlays, firmware and CPU support can limit the hardware effect of a
verified stored value; no performance gain is asserted.

## Network behavior

DNS presets are Cloudflare, Quad9 and Google, with Automatic and custom canonical
IPv4 literals. DNS readback uses configured overrides rather than turning an
effective DHCP server list into a static configuration. Changing DNS does not
inherently reduce game-server latency. Existing IPv6 DNS settings remain untouched.

MTU accepts explicit values, including 1492/1500 where supported, bounded by
address-family and reported link limits. Persistent and active values are separate
snapshots; a second-store failure can be partial. No value is chosen automatically
as the best MTU for a connection.

The UI's TCP control targets the Internet template. The platform adapter also
models InternetCustom and DatacenterCustom for explicit supported callers. New
Experimental-level requests are disallowed; an originally captured Experimental
value can be restored. The operation itself requires experimental-risk review.
RSS changes only Enabled and defers activation to a Windows restart.

Adapter aliases are display data. Native calls and the fixed PowerShell provider
resolve the stable interface GUID for each request. No universal network-reset
script, arbitrary command string, undocumented registry write or security-disable
operation is accepted by the helper.

## Not implemented in this milestone

Full profile execution, remaining advanced CPU/network controls, path-MTU and
before/after performance diagnostics, GPU settings/telemetry, Windows Services
writes and Debloat remain In Development. Vendor voltage, frequency and power-limit
tuning is Unsupported in this build. Phase C expansion is gated on real Windows
recovery verification for the first operations.

## Sources and implementation detail

- [CPU API contracts, GUIDs and limits](../src/Carpatia.Ultimate.Platform.Windows/Cpu/README.md)
- [DNS, MTU, TCP and RSS references and boundary](../src/Carpatia.Ultimate.Platform.Windows/Network/README.md)
- [IPC, UAC and protected journal design](../src/Carpatia.Ultimate.Broker/README.md)
- [Engine recovery and review semantics](recovery.md)
