# Windows build and Phase B acceptance

Windows 11 x64 is the primary target. Use .NET SDK 10.0.403, the required Windows
SDK/build tools and Windows application development tooling. The app and helper
bundle their runtimes. No native modification test runs automatically by default.

## Reproducible build and automated tests

The convenience runner `scripts/Validate-Windows.ps1` performs the checks below
from a normal PowerShell session. It captures SDK details, separate native exit
codes, logs, TRX files and payload checks in a timestamped `Windows-results-*.zip`
under `artifacts/`, including failures. It does not install tooling, change
execution policy, elevate, launch the application/helper or execute live writes.
Windows test skip counts can differ from the Linux baseline. The script's
native Windows execution still needs to be verified.

From a fresh checkout:

```powershell
dotnet --info
dotnet test tests/Carpatia.Ultimate.Tests/Carpatia.Ultimate.Tests.csproj -c Release
dotnet test tests/Carpatia.Ultimate.Control.Tests/Carpatia.Ultimate.Control.Tests.csproj -c Release
dotnet test tests/Carpatia.Ultimate.Broker.Tests/Carpatia.Ultimate.Broker.Tests.csproj -c Release
dotnet test tests/Carpatia.Ultimate.Platform.Tests/Carpatia.Ultimate.Platform.Tests.csproj -c Release
dotnet test tests/Carpatia.Ultimate.Diagnostics.Tests/Carpatia.Ultimate.Diagnostics.Tests.csproj -c Release
dotnet build src/Carpatia.Ultimate.Helper/Carpatia.Ultimate.Helper.csproj -c Release -p:Platform=x64
dotnet build src/Carpatia.Ultimate.App/Carpatia.Ultimate.App.csproj -c Release -r win-x64 -p:Platform=x64 -p:WindowsPackageType=None
```

The app build must include `Helper/Carpatia.Ultimate.Helper.exe`, its DLL,
`.deps.json`, `.runtimeconfig.json` and runtime dependencies beneath the app output
folder. The CI workflow tests all five projects, builds the app/helper, asserts
that payload and retains TRX reports plus the unsigned build. It has not been run
remotely. Tests use fake backends for writes; Windows-specific checks are read-only
or parse-only and do not replace the manual cases below.

## Read-only desktop acceptance

1. Launch `Carpatia.Ultimate.exe` without elevation. Startup must not request UAC
   or modify settings. Check every navigation destination, CPU/GPU/Network tabs,
   light/dark/system appearance, resizing, 100/150/200% scaling, keyboard access,
   Narrator and High Contrast.
2. Compare OS, CPU, GPU/driver, RAM and drive data with Windows Settings, Task
   Manager and Device Manager. Refresh on demand; missing metrics remain unavailable.
3. Select a CPU policy or network resource and read a preview without elevation.
   Check exact current/expected values and scope. Unavailable capabilities must
   remain unavailable. Profiles must not claim to be applied.
4. Verify local operation without internet access. Copying diagnostics should
   include actual data only. Close during a refresh or review dialog and verify
   clean cancellation without an unsolicited write.

## Explicit native-write acceptance in a disposable VM

Take a VM snapshot first and retain independent administrator/console access.
Use a local administrator account running the app with its normal filtered token,
so UAC can elevate the same SID. Different-account administrator credentials are
intentionally rejected by the IPC/recovery boundary. Use lab network adapters
that do not carry a remote-management connection. A VM may lack CPU-policy or RSS
capabilities; record Unsupported and use a separate compatible test machine rather
than bypass guards.

For every supported operation, record the OS/build, driver, resource GUID,
capability evidence, exact original value(s), requested value, preview, helper
result, journal record and independent readback. Test one operation at a time:

| Operation | Native test and independent verification |
| --- | --- |
| CPU minimum | Explicit active plan GUID and AC/DC source; choose a detected increment within the current maximum. Check `powercfg /qh` stored index, then Restore and compare the original. Test AC and DC independently. |
| CPU maximum | Choose a supported value no lower than the current minimum; check the exact selected index and Restore without changing its companion. |
| EPP | Change only where native bounds/increments are exposed; compare selected plan/source readback before/after Restore. |
| Boost policy | Select another detected documented mode; verify stored policy and successful plan reapplication. This does not establish a frequency or performance gain. |
| IPv4 DNS | Select the stable GUID of the intended lab adapter. Test a canonical static list and Automatic separately; compare configured override source/order using the supported native interface. Restore the original source, not a captured DHCP effective list. |
| MTU | Capture IPv4/IPv6 ActiveStore and PersistentStore independently. Choose a valid explicit value within link limits, including 1492/1500 only where supported. Compare both stores with `Get-NetIPInterface` after Apply and Restore. |
| TCP auto-tuning | Review the experimental risk and selected Internet template. On unmanaged supported state, compare `Get-NetTCPSetting` local/effective/policy values after Apply and Restore. |
| RSS | Use a capable adapter; compare `Get-NetAdapterRss` Enabled and unchanged profile/queue/processor context. Verify the restart-required result and repeat readback after an intentional restart, then Restore and restart as reported. |

For each Apply and Restore, inspect the in-app before/after review, approve UAC
explicitly and approve the helper's fresh native confirmation. Decline each
confirmation in a separate case and verify that no configuration write occurs.
Check that the helper exits after its single response and leaves no elevated
service. IPv6 DNS, encrypted/profile-managed DNS and unsupported metadata must
fail closed without changing the interface.

## Recovery, conflict and security acceptance

- Repeat Apply with the same value and then another value for the same resource.
  Confirm that the first original snapshot survives. Repeat Restore and verify
  no unrelated setting is overwritten.
- Preview in one instance, change state in another instance or a supported Windows
  tool, and attempt the stale action. Expect a conflict and no write. Change state
  while native confirmation is open; both Apply and Restore must reject a different
  reviewed state. A later edit after completed restoration must not be overwritten.
- Change the active power scheme between review and execution. Expect an observable
  conflict and no deliberate switch back. Test min/max restoration ordering when
  companion values changed; the adapter must refuse an invalid pair.
- In a dedicated fault-injection VM session, interrupt the helper after durable
  pre-write persistence, after one MTU store changes, and after a write but before
  final result persistence. Use debugger breakpoints/instrumentation, not normal
  automated tests. Relaunch and inspect the unfinished record. Verify restoration
  of known state or an explicit conflict for unrecorded/unknown partial state.
- Simulate a disk/journal failure before a write and confirm no native mutation.
  Simulate readback and CPU reactivation failure; neither matching storage nor a
  successful process exit may hide failed activation or recovery requirements.
- Inspect ProgramData ownership/DACLs with Windows ACL tools. In the disposable
  security test, attempt standard-token file/directory modification: access must
  be denied. Test an untrusted pre-existing directory, reparse point and linked
  file; the helper must refuse them rather than take ownership or weaken ACLs.
- Verify the standard app can read only its SID-scoped records, another user cannot
  access that leaf, different-account UAC fails closed, malformed IPC is rejected,
  and legacy LocalAppData JSON is never imported into protected recovery storage.

Preserve failure evidence and the VM snapshot. Do not delete recovery records to
make an error disappear. A crash before partial readback can require manual
resource-specific recovery; an unbootable system needs the independent recovery
environment. Phase C and release remain gated on these Windows results.
