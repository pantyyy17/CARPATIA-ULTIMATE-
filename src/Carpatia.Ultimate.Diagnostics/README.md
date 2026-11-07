# Read-only diagnostics

`IHardwareDiagnostics` is the UI boundary. `WindowsHardwareDiagnostics` uses:

- WMI `Win32_Processor`, `Win32_VideoController`, `Win32_OperatingSystem` for device names, driver versions and OS build. The CPU registry description is a fallback if WMI is unavailable.
- `GetActiveProcessorCount` for the machine's active logical processor count.
- `GetSystemTimes` for an on-demand 300 ms aggregate CPU utilization sample. Its kernel counter includes idle time. Aggregate utilization is deliberately unavailable above 64 logical processors because that API is scoped to the caller's processor group on those machines.
- `GlobalMemoryStatusEx` for actual physical memory total and availability.
- `DriveInfo` for the Windows system drive's total and free capacity.

All operations are read-only and run under the caller's normal Windows account. WMI runs on a worker thread; no timers, persistent service, shell command or network request is created. Cancellation is observed between WMI calls and rows; it cannot interrupt an already blocked native WMI provider. WMI enumeration uses a four-second provider timeout.

Missing data remains nullable and includes explanatory notices. GPU utilization, temperature, clocks and power are not implemented; device discovery does not imply access to vendor telemetry or tuning interfaces. CPU/GPU hardware writes are explicitly unsupported.

The library targets `net10.0` so pure logic tests can run on Linux. Windows APIs are guarded by platform checks. The WinUI application provides the Windows minimum version requirement.

## Validation

On Linux with .NET SDK 10.0.403, `dotnet test tests/Carpatia.Ultimate.Diagnostics.Tests` passed 14 tests; two Windows read-only integration tests were skipped. Tests cover utilization calculations, invalid/reset counter handling, cancellation and truthful unavailable states on non-Windows hosts.

Run the same command on Windows to execute both integration tests. WMI and Win32 execution has not been validated on actual Windows hardware in this environment. Hardware detection is **Implemented, Hardware Test Pending**; tested arithmetic and platform guards are **Working & Tested**.

References: [GetSystemTimes](https://learn.microsoft.com/windows/win32/api/processthreadsapi/nf-processthreadsapi-getsystemtimes), [GlobalMemoryStatusEx](https://learn.microsoft.com/windows/win32/api/sysinfoapi/nf-sysinfoapi-globalmemorystatusex), [GetActiveProcessorCount](https://learn.microsoft.com/windows/win32/api/winbase/nf-winbase-getactiveprocessorcount), [Win32_Processor](https://learn.microsoft.com/windows/win32/cimwin32prov/win32-processor), [Win32_VideoController](https://learn.microsoft.com/windows/win32/cimwin32prov/win32-videocontroller).
