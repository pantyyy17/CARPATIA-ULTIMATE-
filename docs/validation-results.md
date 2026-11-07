# Actual validation results — Phase B source

Validation includes Linux development checks and hosted Windows CI on 2026-11-07,
using .NET SDK 10.0.403. Native configuration writes, UAC interactions and
protected-journal filesystem behavior have not been exercised on a real test PC.

## Automated test results

| Test project | Linux passed / skipped | Windows passed / skipped |
| --- | ---: | ---: |
| Engine and recovery | 48 / 0 | 48 / 0 |
| Typed review and control flow | 73 / 0 | 73 / 0 |
| Broker protocol/security-policy logic | 50 / 0 | 50 / 0 |
| CPU and network platform adapters | 71 / 3 | 73 / 1 |
| Diagnostics | 14 / 2 | 16 / 0 |
| **Total** | **256 / 5** | **260 / 1** |

No test failed in either environment. Windows executes the native read-only CPU
policy and hardware checks, PowerShell parse check and IPv6-DNS unavailable
check. The non-Windows guard case is skipped on Windows. Most mutation tests use
isolated backends; these results do not prove real Windows Apply/Restore.

Tests cover compatibility, identity, admin/risk gates, immutable review tickets,
confirmation races, repeated operations, external edits, partial writes,
activation failure, interruption, corrupt snapshots, schema changes and leases.
Broker tests establish protocol/security-policy decisions, not effective UAC or
native protected-storage permissions.

## Successful native Windows build and startup observation

[Windows CI run 42367836867](https://github.com/pantyyy17/CARPATIA-ULTIMATE-/actions/runs/42367836867)
passed at source commit `7f40379701744699f2e35f1973f3328880f7cf6c`:

- All five test suites: 260 passed, one skipped, zero failed.
- Full Release win-x64 WinUI/XAML application and helper build: succeeded,
  zero build warnings and errors.
- Main app and complete one-shot helper payload: required files present.
- Read-only startup observation: process remained alive through the 12-second
  observation window, with no direct helper child observed; only the started
  app process was cleaned up.
- Startup diagnostics, TRX reports and an unsigned development build were retained
  as workflow artifacts.

The observation records the launching token; hosted CI may use an elevated
account. It requests no elevation and invokes no configuration operation.
Process survival does not validate rendering, standard-user behavior,
accessibility, navigation or actual hardware tuning.

The native compiler verified fixes for runtime-template metadata, the generated
busy-state method call and overlapping default asset globs. These were not
established by the earlier Linux C# stub check.

## Linux build and static checks

Linux cannot execute the native Windows XAML compiler. Its direct full-app build
stops at `XamlCompiler.exe: Exec format error`; the Windows run above supplies the
native compilation evidence instead. Engine/platform/control/broker libraries
and the self-contained Windows helper cross-build succeeded on Linux.

App C# and 692 XAML property/binding-source references were checked against real
WinUI/Windows references using temporary generated-member stubs. These stubs are
not shipped and do not replace native XAML compilation or runtime validation.
The final instance-method binding and single asset Content item were also checked.

## Windows handoff runner

`Validate-Windows.ps1` was parsed with official PowerShell 7.6.9 on Linux.
Focused process checks preserved exit codes, stderr, spaces, quotes, empty
arguments and trailing backslashes. Linux execution correctly returned a Blocked
summary and results ZIP. Its native Windows execution remains a separate check.

`Smoke-WindowsStartup.ps1` was parsed and its Linux refusal path checked, then
executed successfully by the Windows CI run above. It is separate from the
normal validation runner, which never launches an executable.

## Still required

- Manual Windows 11 UI, display scaling, contrast, keyboard and Narrator acceptance.
- Standard-user startup and physical hardware/provider accuracy beyond the CI VM.
- Real CPU/DNS/MTU/TCP/RSS Apply/Verify/Restore, including interrupted-write recovery.
- UAC, same-user IPC ACLs, protected journal ownership/pinning and filesystem tests.
- Startup/idle resource measurement, signing, installers and release QA.

Native modification paths remain Implemented, Hardware Test Pending. Full profile
execution, GPU tuning, services/debloat and later milestones are not complete.
The artifact is an unsigned development build, not a signed installer. Follow
[Windows acceptance](windows-validation.md) before release or Phase C expansion.
