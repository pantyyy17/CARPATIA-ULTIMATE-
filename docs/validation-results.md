# Actual validation results — Phase B source

Date: 2026-11-06. Environment: Linux x64, Debian 13, .NET SDK 10.0.403. No Windows
desktop or physical tuning hardware was available. No live Windows configuration
write, UAC elevation or effective recovery-ACL test was executed.

## Executed automated tests

| Test project | Passed | Skipped | Failed |
| --- | ---: | ---: | ---: |
| `Carpatia.Ultimate.Tests` — engine/recovery | 48 | 0 | 0 |
| `Carpatia.Ultimate.Control.Tests` — typed review flow | 73 | 0 | 0 |
| `Carpatia.Ultimate.Broker.Tests` — protocol/security-policy logic | 50 | 0 | 0 |
| `Carpatia.Ultimate.Platform.Tests` — CPU and network | 71 | 3 | 0 |
| `Carpatia.Ultimate.Diagnostics.Tests` | 14 | 2 | 0 |
| **Total** | **256** | **5** | **0** |

The CPU subset passed 28 cases and skipped one native read-only Windows case.
The network subset passed 43 and skipped two Windows-specific cases. These
platform skips concern native CPU reads, Windows PowerShell parsing and the
unsupported IPv6 DNS guard; the diagnostic skips concern actual Windows hardware
reads/samples. Windows may skip different platform-guard cases.

Tests execute real planning, control dispatch and persistent journal code with
isolated typed adapters/backends for writes. They cover compatibility, value and
resource identity checks, admin/risk gates, immutable reviewed state, stale/future
or altered tickets, confirmation cancellation, repeated actions, conflict
symmetry, external edits, partial writes, activation failure, interruption,
corrupt state, nullability, schema changes and cooperating engine instances.
Broker tests cover framing and security-policy rejection decisions, not real UAC,
named-pipe ACL enforcement or protected filesystem behavior.

## Build and static checks

- Engine, Control, Broker and platform projects compile as dependencies of their
  passing Release test suites.
- The self-contained `win-x64` Windows helper cross-build succeeded with zero
  warnings/errors. Its executable was not run.
- Direct execution of the app's `CopyOneShotHelper` MSBuild target succeeded and
  placed `Helper/Carpatia.Ultimate.Helper.exe` under the app target directory.
  This is payload-copy verification, not a complete WinUI build.
- App dependency restore, including stable Windows App SDK WinUI 2.6.5, passed.
- Phase B app C# compiled against actual restored WinUI/Windows reference
  assemblies using temporary XAML-generated-member stubs. A companion static
  check examined 692 XAML property/binding paths. The final rerun, including
  unloaded-view and concurrent-dialog guards, passed with zero warnings/errors.
  Temporary stubs are not shipped. Generated XAML binding code and runtime
  resource resolution still require Windows.
- Project/manifest/theme/view XML and workflow YAML are checked as syntax, not
  evidence of native XAML semantic validity or a hosted CI run.

## Full WinUI build limitation

The Linux app build reaches the Windows-only XAML compiler and cannot execute it:

```text
XamlCompiler.exe: Exec format error
XamlCompiler output file .../output.json was not created.
```

This host limitation does not establish that the WinUI build passes on Windows.
The supplied Windows workflow builds the complete app/helper, asserts the packaged
helper payload and runs all five test projects. No remote workflow was invoked.

## Windows handoff runner

The Windows handoff script `scripts/Validate-Windows.ps1` was parsed with official
PowerShell 7.6.9 on Linux. Focused process checks preserved a nonzero native exit
code, stderr and arguments containing spaces, quotes, empty strings and trailing
backslashes. Its actual Linux invocation refused native validation, returned
exit code 2 and still created a valid results ZIP with a Blocked summary. These
checks do not validate Windows PowerShell 5.1, WinUI compilation or Windows
process execution. The existing 256-pass/5-skip test baseline is unchanged.

## Unexecuted and required

- Native Windows XAML compilation, launch, visual/accessibility review and display scaling.
- Live Windows hardware/provider accuracy and the Windows-only test cases.
- Actual CPU/DNS/MTU/TCP/RSS writes, Apply/Verify/Restore and interrupted-write recovery.
- UAC, same-user pipe ACLs, protected journal ownership/DACL/pinning and filesystem attacks.
- App startup/idle resource measurement, signed install/upgrade/uninstall and release review.

The first eight native operation families are Implemented, Hardware Test Pending.
Full profile execution, GPU tuning, services/debloat and later milestones are not
claimed complete. The helper cross-build is a development artifact, not a
Windows-validated installer. Follow [Windows acceptance](windows-validation.md)
before release or Phase C expansion.
