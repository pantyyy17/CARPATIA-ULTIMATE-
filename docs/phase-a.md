# Architecture and milestone boundaries

CARPATIA ULTIMATE EDITION 1.0.0 is a new native Windows application. The approved
image supplies the Alpine palette, mountain header, spacing and compact dashboard.
Product naming and actual functionality follow the engineering directive.

Phase A established the shell, diagnostics, typed contracts and recovery engine.
The current Phase B source adds the first CPU/network operations and review/helper
flow. Their Windows execution and recovery acceptance remain pending; this is not
a declaration that Phase B's Windows verification gate has passed.

## Module boundaries

| Module | Responsibility |
| --- | --- |
| `Carpatia.Ultimate.App` | WinUI startup, navigation, XAML, view models and reviewed-operation UI |
| `Carpatia.Ultimate.Core` | Typed tweak definitions, compatibility, profiles and operation contracts |
| `Carpatia.Ultimate.Engine` | Preview, conflict gates, execution, readback verification and durable recovery |
| `Carpatia.Ultimate.Platform.Windows` | Supported CPU power-policy and selected network adapters |
| `Carpatia.Ultimate.Diagnostics` | Read-only hardware discovery and on-demand utilization samples |
| `Carpatia.Ultimate.Control` | Source-defined operation allowlist, request validation and reviewed-plan dispatch |
| `Carpatia.Ultimate.Broker` | Bounded local IPC, UAC client and protected recovery-store boundary |
| `Carpatia.Ultimate.Helper` | One elevated operation with fresh native user confirmation, then exit |
| Five test projects | Engine, control, broker, platform and diagnostic logic/failure simulations |

XAML code-behind handles presentation events; it does not write registry values,
power indices or device settings. Only registered typed operations pass through
the dispatcher to the engine. The network provider uses a fixed embedded
PowerShell script for documented cmdlets; request data travels as JSON, not
inserted executable text. A successful process exit alone never establishes
that a setting was applied.

## Review and execution

1. The standard-user app discovers the selected resource and requests a read-only
   preview of actual state and the intended configuration.
2. The user reviews before/after state. An Apply/Restore request carries the
   reviewed snapshot, operation kind and validated typed inputs.
3. A one-shot helper starts through UAC, revalidates the request and target, rereads
   state and rejects changed, expired or inconsistent reviews.
4. A fresh native confirmation precedes execution. The engine then checks state
   again while holding its leases, persists recovery data and invokes the adapter.
5. Readback establishes configuration status. Activation failures, partial changes,
   unavailable state and restart requirements remain explicit.

This is single-operation execution. Selecting a profile does not execute a batch,
change a Windows plan or imply that an optimized profile is already applied.

## Status vocabulary

- **Working & Tested:** the stated behavior executed successfully in the identified
  environment. Fake-backed tests do not establish native Windows tuning support.
- **Implemented, Hardware Test Pending:** source exists, but its native Windows
  execution or relevant hardware behavior has not passed integration acceptance.
- **In Development:** the feature is not available for modifying the system.
- **Unsupported:** no validated supported configuration interface is supplied for
  that control or detected configuration.

Unknown hardware metrics remain unavailable. GPU utilization, clocks, voltage and
power are not substituted with sample values. No FPS/ping gain or optimization
score is invented.

## Build baseline

The solution uses .NET 10 LTS, SDK 10.0.403 and stable Windows App SDK WinUI 2.6.5.
The app targets Windows 10 build 19041 or newer, with Windows 11 x64 the primary
validation target. Native capability guards can impose stricter requirements.
The app and helper are unpackaged and self-contained development builds.

The helper cross-build succeeds on Linux. WinUI XAML compilation and desktop
execution require Windows; platform-neutral tests and temporary C# compile checks
cannot replace them. The supplied Windows workflow has not been executed remotely.
