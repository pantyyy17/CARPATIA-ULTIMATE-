# CARPATIA ULTIMATE EDITION

**1.0.0 · Alpine Edition · Phase B source milestone**

Open source under the [MIT license](LICENSE), including commercial use.
Third-party imagery, fonts and dependencies retain their own terms; see
[third-party notices](THIRD-PARTY-NOTICES.md).

[Download the source ZIP](https://github.com/pantyyy17/CARPATIA-ULTIMATE-/archive/refs/heads/main.zip),
extract it, open normal PowerShell in the extracted project folder, and run:

```powershell
.\scripts\Validate-Windows.ps1
```

The script builds and tests the source, then produces a results ZIP under
`artifacts/`. It does not apply system optimizations. This is a development
milestone; no Windows-validated release or installer is supplied yet.

A new native Windows optimization application in C#, .NET 10 LTS and WinUI 3.
The supplied image guides the mountain header, ice-blue palette, compact metrics
and restrained navigation. The application uses the product name and functional
requirements in the [engineering directive](docs/engineering-directive.md).

The source now includes the first eight CPU/network operation families, a typed
review flow and a one-shot elevated helper. **Live Windows writes, UAC, recovery
ACLs and manual interface acceptance remain unverified.** These
integrations are **Implemented, Hardware Test Pending**, not a release-ready
optimizer or a claim of measured performance improvement.

## Implemented source

- Alpine WinUI shell, seven navigation destinations and CPU/GPU/Network tabs.
- Real Windows hardware discovery and on-demand CPU/RAM samples; unavailable
  telemetry remains unavailable.
- Minimum/maximum processor state, Energy Performance Preference and supported
  boost policy, with explicit active scheme and AC/DC selection.
- Adapter-specific IPv4 DNS, IPv4/IPv6 MTU, TCP auto-tuning and Receive Side
  Scaling. Compatibility and policy restrictions can make an operation unavailable.
- Exact before/after review, fresh helper confirmation, typed input validation,
  durable original snapshots, readback verification and reviewed restoration.
- SID-scoped protected recovery storage, bounded local IPC and a helper that
  exits after one operation. The main app starts without elevation.
- Automated engine, control, broker, platform and diagnostics tests; Windows CI
  builds the app and checks that its helper payload is present.

See the [operation matrix and limitations](docs/phase-b.md). Full profile
execution, GPU tuning, service modification and application removal remain in
Development. Vendor voltage/clock controls are Unsupported. Balanced,
Performance, Ultra and Custom are review selections; they do not apply a profile.

## Build on Windows

For a single standard-user validation run, execute
`./scripts/Validate-Windows.ps1` from the checkout. It runs all five suites,
builds the app/helper, checks the payload and writes a timestamped results ZIP
under `artifacts/`, including when a check fails. It never launches the app,
elevates or performs native configuration writes. See the
[Romanian Windows quickstart](docs/windows-quickstart.ro.md).

Install .NET SDK **10.0.403** and Windows application build tooling. Windows 11
x64 is the primary target; the app's minimum build is Windows 10 19041. That
minimum is a build/API floor, not a claim of completed Windows 10 testing.

```powershell
dotnet test tests/Carpatia.Ultimate.Tests/Carpatia.Ultimate.Tests.csproj -c Release
dotnet test tests/Carpatia.Ultimate.Control.Tests/Carpatia.Ultimate.Control.Tests.csproj -c Release
dotnet test tests/Carpatia.Ultimate.Broker.Tests/Carpatia.Ultimate.Broker.Tests.csproj -c Release
dotnet test tests/Carpatia.Ultimate.Platform.Tests/Carpatia.Ultimate.Platform.Tests.csproj -c Release
dotnet test tests/Carpatia.Ultimate.Diagnostics.Tests/Carpatia.Ultimate.Diagnostics.Tests.csproj -c Release
dotnet build src/Carpatia.Ultimate.App/Carpatia.Ultimate.App.csproj -c Release -r win-x64 -p:Platform=x64
```

Open `Carpatia.Ultimate.slnx` in current Visual Studio tooling. Run
`Carpatia.Ultimate.exe` from the app's
`bin/x64/Release/net10.0-windows10.0.19041.0/win-x64/` directory and retain all
sibling runtime files and the complete `Helper/` subdirectory. The app build's
`CopyOneShotHelper` target copies the helper payload. Both executables are
self-contained, unpackaged and unsigned development artifacts; no installer or
commercial update mechanism is supplied.

Apply/Restore presents the reviewed change, requests UAC for the one-shot helper
and then requires fresh native confirmation. Cancelling either review leaves the
operation unsubmitted. Recovery is stored at
`%PROGRAMDATA%\Carpatia\Ultimate\Recovery\<Windows user SID>`; the helper owns
writes and the corresponding standard user can read. Earlier user-writable
LocalAppData records are not imported. See [recovery](docs/recovery.md).

## Actual validation

Executed with .NET SDK 10.0.403 on Linux x64 and hosted Windows CI:

| Suite | Linux | Windows |
| --- | --- | --- |
| Engine and recovery | 48 passed | 48 passed |
| Typed control/review flow | 73 passed | 73 passed |
| Broker protocol/security-policy logic | 50 passed | 50 passed |
| CPU/network adapters | 71 passed, 3 skipped | 73 passed, 1 skipped |
| Diagnostics | 14 passed, 2 skipped | 16 passed |
| **Total** | **256 passed, 5 skipped** | **260 passed, 1 skipped** |

The [Windows workflow](https://github.com/pantyyy17/CARPATIA-ULTIMATE-/actions/workflows/windows.yml)
builds the full app/helper, checks the payload and observes read-only startup.
Its live status records the current native build/startup result. Startup survival
does not establish visual quality or standard-user behavior; hosted CI may use
an elevated token. No native configuration write is tested by this workflow.

These suites validate isolated logic, fake-backed writes and selected read-only
Windows integrations. [Validation results](docs/validation-results.md) separate
executed checks from required [Windows acceptance](docs/windows-validation.md).

Do not expand into Phase C until Windows Apply/Verify/Restore and failure
recovery have passed on the first operations. No telemetry, advertisements,
permanent elevated service or arbitrary command runner is included. A settings
journal is not a full-system backup or a promise of universal rollback.

## Documentation

- [Architecture and milestone boundaries](docs/phase-a.md)
- [Phase B operations](docs/phase-b.md)
- [Journal and restoration semantics](docs/recovery.md)
- [Dependency baseline and sources](docs/dependency-baseline.md)
- [Release readiness, distribution and privacy](docs/release-readiness.md)
- [Asset source and license](docs/asset-credits.md)
