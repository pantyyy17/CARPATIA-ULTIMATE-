# Verified dependency baseline

Checked during implementation on 2026-11-06:

| Component | Selected version | Evidence |
| --- | --- | --- |
| .NET LTS | .NET 10; SDK 10.0.403 | Official installer downloaded and `dotnet --info` executed |
| Windows App SDK | Stable 2.6.5 | Microsoft stable-channel table and NuGet package metadata; restore succeeded |
| Windows target | 10.0.19041.0 | .NET Windows reference assemblies restored; Windows 11 primary target |
| System.Management | 10.0.0 | NuGet restore and library/test builds succeeded |
| xUnit | 2.9.3 | NuGet restore; tests executed |
| Test SDK | 17.14.1 | NuGet restore; tests executed |
| Visual Studio test adapter | 3.1.5 | NuGet restore; tests executed |

The WinUI application uses .NET 10's Windows TFM and the stable Windows App SDK's
`Microsoft.WindowsAppSDK.WinUI` component package. It brings Base, Foundation
and InteractiveExperiences, excluding unused AI, ML, Search and Widgets packages.
The SDK Base targets support component-only references in self-contained mode.
Temporary compile checks against the restored real WinUI and Windows reference
assemblies confirm its C# API references. This is not a replacement for the
Windows-only XAML compilation step or runtime validation.

Direct references are pinned. The app uses the official Windows App SDK WinUI package,
System.Management through the diagnostics project, and no third-party MVVM,
dependency-injection or desktop web framework. Test dependencies are not shipped
as application dependencies. No additional app networking library is used.

Phase B adds no third-party runtime package: CPU policies use PowrProf, DNS uses
IP Helper, and MTU/TCP/RSS use a fixed Windows PowerShell provider with typed JSON
on standard input. The one-shot helper and broker use the .NET Windows security,
named-pipe and filesystem APIs. Official cmdlet syntax was checked for
`Set-NetAdapterRss -InputObject -Enabled -NoRestart` and
`Set-NetTCPSetting -InputObject -AutoTuningLevelLocal`. Actual provider execution
still requires Windows integration testing.

Sources:

- https://learn.microsoft.com/windows/apps/windows-app-sdk/stable-channel
- https://learn.microsoft.com/windows/apps/windows-app-sdk/system-requirements
- https://api.nuget.org/v3-flatcontainer/microsoft.windowsappsdk/2.6.5/microsoft.windowsappsdk.nuspec
- https://api.nuget.org/v3-flatcontainer/microsoft.windowsappsdk.winui/2.6.5/microsoft.windowsappsdk.winui.nuspec
- https://dotnet.microsoft.com/platform/support/policy/dotnet-core
- https://learn.microsoft.com/dotnet/core/install/
