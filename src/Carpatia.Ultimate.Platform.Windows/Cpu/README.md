# Processor power-policy adapters

Status: **Implemented, Hardware Test Pending**. These adapters configure documented Windows power policies; they do not access MSRs, CPU voltage, PL1/PL2, AMD PPT/TDC/EDC, BIOS settings or firmware protection controls.

`CpuPowerPolicyTweak` implements `ITweak<CpuPowerState, uint>` for minimum processor state, maximum processor state, Energy Performance Preference and processor boost mode. The caller explicitly selects an existing active scheme GUID and either AC or DC. There is no default change or automatic 100% minimum state.

`IPowerPolicyApi` is injectable. `NativePowerPolicyApi` uses only allowlisted PowrProf functions. No process, PowerShell command, service or network request is created. Calls are synchronous and short; application/broker callers should run an operation on a worker thread, not the UI thread.

## State and validation

- Every snapshot retains the selected scheme GUID, AC/DC source, setting and exact original DWORD index.
- Native capability detection reads the Windows definition's minimum, maximum and increment. Boost values must be DWORD values returned by `PowerReadPossibleValue` and belong to the seven documented modes (0–6). Missing or unknown metadata is unsupported.
- Current values must fit the metadata before they can become recovery snapshots.
- Minimum and maximum are cross-checked for the same source at preview, immediately before Apply and at Restore. Companion bounds are read-only context; restoring minimum does not restore maximum, or vice versa.
- The selected scheme must remain active before and after reads, writes and reapplication. The adapter never deliberately activates another plan. If Windows changed plans, select the original plan in Windows before retrying restoration.
- Windows has no compare-and-swap variant of `PowerSetActiveScheme`. An external program can change schemes between the final guard and the native call. The adapter checks before/after and reports observable conflicts, but cannot guarantee atomic exclusion against other programs. The application journal serializes its own changes only.
- Readback verifies the stored policy index. Windows power-mode overlays, firmware and processor support can limit or ignore its hardware effect. No frequency, FPS, energy or performance improvement is claimed.
- `RequiresSuccessfulWrite` is true: a failed reapply is not accepted as verified merely because the stored index matches. Failed/partial state remains in the operation journal for recovery.

Administrator authorization is enforced by the engine/broker. The P/Invoke adapter itself is a library, not a security boundary. The production helper must accept only typed, allowlisted requests and revalidate them.

## Native references verified during implementation

GUIDs and mode constants were checked against Microsoft's [WinNT.h](https://github.com/microsoft/win32metadata/blob/main/generation/WinSDK/RecompiledIdlHeaders/um/winnt.h). Signatures were checked against [PowrProf.h](https://github.com/microsoft/win32metadata/blob/main/generation/WinSDK/RecompiledIdlHeaders/um/powrprof.h) and [PowerSetting.h](https://github.com/microsoft/win32metadata/blob/main/generation/WinSDK/RecompiledIdlHeaders/um/powersetting.h).

- [PowerReadACValueIndex](https://learn.microsoft.com/windows/win32/api/powrprof/nf-powrprof-powerreadacvalueindex) / [PowerReadDCValueIndex](https://learn.microsoft.com/windows/win32/api/powrprof/nf-powrprof-powerreaddcvalueindex).
- [PowerWriteACValueIndex](https://learn.microsoft.com/windows/win32/api/powersetting/nf-powersetting-powerwriteacvalueindex) / [PowerWriteDCValueIndex](https://learn.microsoft.com/windows/win32/api/powersetting/nf-powersetting-powerwritedcvalueindex).
- [PowerGetActiveScheme](https://learn.microsoft.com/windows/win32/api/powersetting/nf-powersetting-powergetactivescheme) returns an allocated GUID, released with `LocalFree`.
- [PowerSetActiveScheme](https://learn.microsoft.com/windows/win32/api/powersetting/nf-powersetting-powersetactivescheme) reapplies the selected active scheme.
- [PowerReadPossibleValue](https://learn.microsoft.com/windows/win32/api/powrprof/nf-powrprof-powerreadpossiblevalue) exposes enumerated policy values.

Tests use an in-memory fake of the API and the real persistent journal/engine. A separate Windows integration test only reads native state. No native power setting has been modified or validated on Windows hardware in the Linux development environment. Isolated Windows VM Apply/Verify/Restore testing remains required before release.
