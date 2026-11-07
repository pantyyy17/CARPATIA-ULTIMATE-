# CARPATIA ULTIMATE EDITION
## MASTER ENGINEERING DIRECTIVE — BUILD FROM ZERO

**Product:** CARPATIA ULTIMATE EDITION
**Version:** 1.0.0
**Category:** Advanced Windows Optimization & Tuning Suite
**Target:** International Windows users
**Language:** English
**Technology:** C# / .NET / WinUI 3
**Design:** Alpine Edition — Premium Minimal
**Development approach:** Clean-room rebuild

---

# YOUR ROLE

Act as a principal Windows systems engineer, senior C# architect, WinUI 3 designer, performance engineer, security reviewer and QA lead.

You are responsible for implementing a real, maintainable, testable Windows desktop application.

Do not simply generate a beautiful interface.

Do not generate enormous amounts of untested code.

Do not pretend that a tweak works because a command executed without errors.

Every implemented feature must be functional, technically justified and verifiable.

Your priority order is:

1. Correctness
2. System safety
3. Reliable restoration
4. Performance
5. Clean architecture
6. Premium user experience
7. Advanced functionality

**Our philosophy: Minimal interface. Maximum control. Zero unnecessary bloat.**

# 1. COMPLETELY NEW APPLICATION

Create an entirely new project.

Do not migrate the previous application's source code.

Do not copy previous tweak implementations, scripts, frameworks, components or dependencies.

Do not reuse the old architecture.

The old project is considered legacy and must remain untouched.

Create a separate workspace and new Git repository.

Use the old application only as conceptual background if necessary.

Do not inherit its technical debt.

Brand the new application exclusively as:

**CARPATIA ULTIMATE EDITION**

Start versioning from 1.0.0.

# 2. TECHNOLOGY STACK

Use:

- C#.
- A supported .NET LTS release.
- WinUI 3.
- Stable Windows App SDK.
- XAML.
- MVVM.
- Native Windows APIs where appropriate.
- Minimal, established NuGet dependencies.
- Structured dependency injection when it adds clear value.
- Asynchronous operations for expensive work.

Verify package versions and framework compatibility before creating the solution.

Windows 11 is the primary target.

Support compatible Windows 10 configurations where practical, and document OS limitations clearly.

Avoid Electron, web-based desktop frameworks and unnecessary JavaScript runtimes.

Do not install a large framework when a standard Windows API or WinUI control already provides the required functionality.

# 3. PROJECT ARCHITECTURE

Create a maintainable modular solution.

Recommended logical structure:

Carpatia.Ultimate.App
- Application startup
- Windows and navigation
- XAML views
- ViewModels
- Themes
- Reusable controls
- UI resources

Carpatia.Ultimate.Core
- Domain models
- Tweak contracts
- Profile contracts
- Compatibility models
- Operation results
- Validation rules

Carpatia.Ultimate.Engine
- Tweak registry
- Planning engine
- Execution engine
- Conflict management
- State verification
- Profile execution

Carpatia.Ultimate.Platform.Windows
- Hardware detection
- Windows power management
- Registry adapters
- Network adapters
- Windows services
- Graphics configuration
- Operating system integration

Carpatia.Ultimate.Recovery
- Original-state snapshots
- Persistent operation journal
- Backup management
- Restore transactions
- Recovery validation

Carpatia.Ultimate.Diagnostics
- CPU/GPU/RAM information
- Network diagnostics
- Performance measurements
- Structured logging

Carpatia.Ultimate.Tests
- Unit tests
- Integration tests
- Failure simulation
- Recovery tests

Use fewer physical projects if the separation above can be maintained without unnecessary complexity.

Keep the UI independent from the system-modification code.

Do not allow XAML code-behind to directly execute registry changes, PowerShell commands or device modifications.

# 4. OFFICIAL UI — ALPINE EDITION

The attached reference image is the approved design.

Reproduce its visual identity as closely as possible.

This interface must feel like an exceptionally polished premium consumer application.

Design inspiration:

- Apple's minimalist design philosophy.
- Modern native Windows applications.
- Refined spacing and typography.
- Subtle visual depth.
- Elegant alpine scenery.

Do not copy Apple's logos, trademarks or proprietary artwork.

## Appearance

Use:

- White and ice-blue backgrounds.
- Soft gray surfaces.
- Refined blue accents.
- Subtle Carpathian mountain imagery.
- Carefully controlled translucency.
- Soft shadows.
- Rounded corners.
- Native-looking buttons.
- Consistent icons.
- Clean Segoe UI typography.
- Generous but efficient spacing.

Avoid:

- Neon effects.
- Aggressive RGB colors.
- Giant performance circles.
- Huge BOOST buttons.
- Fake speedometers.
- Decorative charts without real data.
- Repetitive feature cards.
- Unnecessary navigation items.
- Excessive glass effects.
- Continuous decorative animation.

The interface should communicate trust, quality, precision and simplicity.

Use a professional responsive desktop layout.

Respect Windows display scaling and accessibility settings.

## Main navigation

Implement:

1. Dashboard
2. Optimization
3. System Tweaks
4. Debloat
5. Windows Services
6. Backup & Restore
7. Settings

Inside Optimization:

- CPU
- GPU
- Network

Keep Advanced and Experimental controls within their respective pages.

Do not create an oversized sidebar.

## Dashboard

Include:

- Alpine mountain header.
- Real hardware overview.
- Current optimization profile.
- Compact CPU/GPU/RAM metrics.
- Optimization summary.
- Main action: Review Optimizations.
- Backup availability.
- Recent activity.

Do not duplicate sidebar navigation using large unnecessary cards.

Never show fabricated FPS increases, fake optimization scores or misleading security messages.

A clean and trustworthy dashboard is more important than filling every available pixel.

# 5. PROFESSIONAL TWEAK ENGINE

This is the most important technical component.

Build one unified tweak execution engine.

Each tweak must define:

- Unique stable ID.
- Human-readable name.
- Detailed explanation.
- Category.
- Supported operating systems.
- Required hardware capabilities.
- Required privileges.
- Supported values.
- Default recommendation, if justified.
- Risk classification.
- Current-state detection.
- Apply implementation.
- Verification implementation.
- Restoration implementation, if technically possible.
- Restart requirements.
- Conflicting operations.
- Source documentation.
- Expected performance implications.

Use strongly typed models instead of arbitrary string-based command execution.

## Execution lifecycle

Every tweak must follow this sequence:

1. Check compatibility.
2. Read the current configuration.
3. Validate the requested configuration.
4. Detect conflicts.
5. Preview the intended changes.
6. Save the original values.
7. Persist a recovery record.
8. Apply the requested changes.
9. Read the actual system state again.
10. Verify that the requested change took effect.
11. Record the result.
12. Provide restoration where supported.

The engine must distinguish between:

- Available
- Unsupported
- Already Applied
- Applying
- Verified
- Pending Restart
- Failed
- Partially Applied
- Restore Available
- Restored
- Recovery Required

Never report success without verification.

# 6. CPU OPTIMIZATION

Create an advanced CPU tuning interface.

## Windows processor power policies

Implement supported controls for:

- Minimum Processor State — AC.
- Maximum Processor State — AC.
- Minimum Processor State — DC.
- Maximum Processor State — DC.
- Energy Performance Preference.
- Processor Boost Mode.
- Processor Core Parking.
- Processor Performance Increase Policy.
- Processor Performance Decrease Policy.
- Processor performance thresholds.
- Supported processor idle policies.
- PCIe Link State Power Management.

Allow expert users to configure minimum processor state to 100% while plugged in.

Do not enable this automatically.

Explain that this does not mean 100% CPU utilization and can increase power consumption and heat.

## Hardware-dependent CPU controls

Investigate and implement only where supported through validated vendor interfaces:

- Intel PL1.
- Intel PL2.
- CPU power limits.
- AMD PPT.
- AMD TDC.
- AMD EDC.
- AMD Precision Boost Overdrive.
- AMD Curve Optimizer.
- CPU voltage offsets.
- Supported frequency adjustments.
- CPU thermal and power-limit monitoring.

Do not manipulate arbitrary model-specific registers.

Do not bypass Undervolt Protection, BIOS locks or firmware security restrictions.

If voltage or power tuning cannot be implemented safely using an available supported interface, explicitly mark the control unsupported.

Never fake hardware tuning.

# 7. GPU OPTIMIZATION

Detect NVIDIA, AMD and Intel hardware.

Implement supported capabilities for:

- GPU device identification.
- Driver identification.
- Graphics performance preference.
- Per-application GPU preference.
- Hardware Accelerated GPU Scheduling.
- Supported Windows graphics presentation settings.
- GPU utilization.
- GPU clock monitoring.
- Temperature monitoring.
- Power monitoring where available.

## Advanced GPU controls

Investigate:

- GPU Power Limit.
- GPU performance policy.
- Supported GPU Clock configuration.
- Supported memory clock configuration.
- Voltage/frequency adjustment through officially supported interfaces.

Do not assume all NVIDIA, AMD or Intel GPUs allow these operations.

A vendor's monitoring API does not automatically provide permission to change voltages or clocks.

Do not use unsupported driver hooks or hardware bypasses.

# 8. NETWORK OPTIMIZATION — HIGH PRIORITY

Create a powerful Network tuning engine.

## DNS

Support:

Cloudflare
- 1.1.1.1
- 1.0.0.1

Quad9
- 9.9.9.9
- 149.112.112.112

Google
- 8.8.8.8
- 8.8.4.4

Also support:

- Automatic DNS.
- Custom DNS.
- IPv6 configurations.
- DNS-over-HTTPS where available.
- DNS configuration verification.
- Original configuration restoration.

Ensure that the correct network adapter is selected.

## MTU

Implement:

- Automatic detection.
- Current MTU reading.
- MTU 1500.
- MTU 1492.
- Custom MTU.
- Path MTU diagnostics.
- Interface-specific validation.
- Restoration of the original MTU.

Never automatically assume 1500 is best for every connection.

## TCP / NIC

Implement supported controls for:

- TCP Auto-Tuning.
- Receive Side Scaling.
- Receive Segment Coalescing.
- Supported congestion control configuration.
- TCPNoDelay.
- TcpAckFrequency.
- Interrupt Moderation.
- Energy Efficient Ethernet.
- Network power saving.
- Large Send Offload.
- Checksum Offload.
- Receive Buffers.
- Transmit Buffers.
- Flow Control.

Some settings are vendor-specific, deprecated, unsupported or workload-dependent.

Do not implement undocumented registry modifications without validated evidence.

Experimental TCP settings must be disabled by default.

## Network diagnostics

Include:

- Latency measurement.
- Jitter measurement.
- Packet loss.
- DNS response-time comparison.
- Adapter capability detection.
- Before/after comparison.

Do not claim that changing DNS automatically reduces in-game latency.

# 9. ADVANCED SYSTEM TWEAKS

Implement selected advanced Windows configuration controls.

Categories:

### Power & Performance
- Processor power management.
- PCIe power saving.
- USB power management.
- Supported performance policies.

### Windows Behavior
- Supported visual-effects settings.
- Startup configuration.
- Background activity management.
- Supported process priorities.
- Per-application CPU affinity.

### Graphics
- Hardware GPU Scheduling.
- Supported windowed game optimizations.
- Per-application graphics preferences.

### Memory & Storage
- Memory pressure diagnostics.
- Pagefile diagnostics.
- SSD TRIM state.
- Storage health diagnostics.
- Supported storage configuration.

### Advanced Interrupt Configuration
- Interrupt Moderation.
- Driver compatibility checks.
- MSI/MSI-X diagnostics.

Treat interrupt-mode modification as a high-risk experimental operation; only expose supported, validated configuration paths.

Do not apply arbitrary MSI registry changes universally.

Do not implement generic registry "performance hacks" with no technical justification.

# 10. EXPERIMENTAL TWEAKS

Implement three risk classes:

**Advanced**
Documented changes with predictable configuration behavior.

**Experimental**
Changes whose performance impact varies by hardware, driver or workload.

**Extreme**
Supported hardware power, voltage or frequency tuning with increased stability or hardware risk.

For Experimental and Extreme operations:

- Disabled by default.
- Explicit user confirmation.
- Hardware compatibility checks.
- Clear risk explanations.
- Original-state recording.
- Apply verification.
- Recovery instructions.
- No automatic startup application.

Never silently combine experimental hardware changes.

An ordinary backup may not recover a system that cannot boot after unstable tuning. Explain this limitation.

# 11. OPTIMIZATION PROFILES

Implement:

- Balanced
- Performance
- Ultra
- Custom

Each profile must use actual registered tweaks.

Balanced:
Conservative, generally safe settings.

Performance:
Validated performance-oriented configuration.

Ultra:
More aggressive supported policies, with clear warnings and review.

Custom:
Individual tweak selection and configuration.

Ultra must not automatically apply undervolting, overclocking or unvalidated experimental changes.

Before applying a profile, show exactly which settings will change.

Profiles must support conflict detection and restoration.

# 12. BACKUP & RESTORE ENGINE

This subsystem must be exceptionally reliable.

Implement a persistent operation journal.

Before modifying a resource, record the original state durably.

Use resource-specific snapshots.

Ensure:

- Repeated Apply does not overwrite the original snapshot.
- Repeated Restore does not damage configuration.
- Concurrent operations cannot corrupt the same resource.
- A crash cannot silently lose the recovery record.
- External changes made by the user or another program are detected.
- Failed operations receive appropriate recovery handling.
- Partial application is reported accurately.
- Restoration is verified.
- Restart-required operations are tracked.

Do not restore unrelated Windows settings.

Do not blindly overwrite values that have changed outside Carpatia.

Use safe, understandable conflict resolution.

For operations that cannot be fully reversed, explicitly explain the limitation before execution.

Do not falsely promise universal rollback.

# 13. DEBLOAT

Implement a conservative, professional application-removal interface.

- Detect real installed applications.
- Identify publishers and package information.
- Differentiate optional applications from Windows system components.
- Support individual selection.
- Preview removals.
- Respect dependencies.
- Never uninstall essential Windows components automatically.
- Explain whether reinstatement is supported.

Never run universal aggressive debloat scripts.

Do not falsely present application uninstallation as a guaranteed reversible operation.

# 14. WINDOWS SERVICES

Build a functional service manager with:

- Real service detection.
- Current service state.
- Startup configuration.
- Dependency information.
- Change preview.
- Apply operation.
- Verification.
- Resource-specific configuration restoration.

Do not blindly disable services for performance.

Avoid disabling Windows security, update, networking and recovery dependencies.

# 15. SECURITY & PRIVILEGES

The application must run without administrator privileges by default.

Only elevated operations should require administrator permission.

Prefer a narrowly scoped privileged helper with explicitly allowed operations.

Do not execute arbitrary commands from user-controlled strings.

Use validated inputs.

Do not disable:

- Microsoft Defender.
- Windows Firewall.
- Windows Update.
- Secure Boot.
- Memory Integrity.
- Other system security mechanisms.

No telemetry.

No advertisements.

No unnecessary network access.

No hidden system changes.

No permanent elevated background service.

# 16. PERFORMANCE & CODE QUALITY

Keep the application lightweight.

Requirements:

- Fast startup.
- Low idle memory usage.
- Low idle CPU usage.
- Responsive navigation.
- Efficient Windows API calls.
- Minimal dependencies.
- No unnecessary background polling.
- No continuous PowerShell execution.
- No permanently running optimizer.
- No blocking operations on the UI thread.

Follow clean code practices.

Avoid overengineering.

Do not create enormous service classes or duplicate implementations.

Use structured logging and meaningful error messages.

Measure application resource usage rather than relying on assumptions.

# 17. REAL PERFORMANCE VALIDATION

Implement a measurement architecture that can compare system behavior before and after a tweak.

Measurements may include:

- CPU performance.
- GPU performance.
- Frametime consistency.
- Network latency.
- Network jitter.
- Packet loss.
- Storage latency.
- Resource usage.

Only compare metrics under reasonably repeatable test conditions.

Distinguish measured improvements from estimated or theoretical benefits.

Do not promise universal FPS gains or ping reductions.

# 18. TESTING STRATEGY

Implement automated unit tests and Windows integration tests.

Test:

- Compatibility detection.
- Unsupported hardware.
- Apply.
- Verification.
- Restore.
- Repeated Apply.
- Repeated Restore.
- Conflicting tweaks.
- Interrupted operations.
- Missing administrator rights.
- Corrupted recovery state.
- External configuration changes.
- DNS switching.
- MTU changes.
- CPU power policies.
- Windows service dependencies.

Use mocks for logic tests.

Use isolated Windows test environments for system modifications.

Do not claim testing on hardware that was not actually available.

Where Windows execution is unavailable, configure an appropriate Windows CI build, provide reproducible test commands and clearly report which tests remain unexecuted.

# 19. APPLICATION DISTRIBUTION

Prepare the architecture for future commercial release.

Plan for:

- Signed installers.
- Clean installation and uninstallation.
- Versioned releases.
- Release notes.
- Secure updates.
- Privacy documentation.
- Future licensing integration.
- International distribution.

Evaluate MSIX and traditional Windows installer options.

Do not implement a licensing server or storefront until the application core is stable.

# 20. DEVELOPMENT ROADMAP

You must build the application incrementally.

## PHASE A — FOUNDATION

Create:

- Brand-new solution.
- Clean project structure.
- Alpine Edition UI.
- Fully functional navigation.
- Basic hardware detection.
- Central tweak contracts.
- Profile model.
- Operation journal infrastructure.
- Recovery architecture.
- Automated foundational tests.

The application must build successfully in an appropriate Windows environment.

## PHASE B — FIRST REAL OPERATIONS

Implement a small set of complete, verified tweaks:

1. CPU Minimum Processor State.
2. CPU Maximum Processor State.
3. CPU Energy Performance Preference.
4. CPU Boost Policy where supported.
5. DNS configuration.
6. MTU configuration.
7. TCP Auto-Tuning where supported.
8. One supported advanced NIC setting.

These eight operations are the initial proof that the new engine is reliable.

Each must support compatibility checks, state detection, preview, Apply, Verify and Restore.

Do not move forward while these operations contain unresolved critical restoration defects.

## PHASE C — ADVANCED OPTIMIZATION

Expand the supported CPU, GPU and Network tweak catalog.

Add Windows Services and Debloat.

Implement operational profile management.

Keep every tweak independently testable.

## PHASE D — EXPERIMENTAL FEATURES

Integrate validated experimental controls.

Investigate official vendor-specific CPU/GPU tuning interfaces.

Implement only verified, compatible functionality.

Include warnings, stability validation and documented recovery procedures.

## PHASE E — COMMERCIAL QUALITY

Finalize:

- UI polish.
- Application performance.
- Integration testing.
- Recovery verification.
- Installer.
- Documentation.
- Release readiness.
- Security review.

# 21. DEFINITION OF DONE

A feature is complete only when:

1. Its compatibility conditions are understood.
2. Its real system state can be read.
3. Its requested modification can be applied.
4. The resulting state can be verified.
5. Restoration works where supported.
6. Error and conflict cases are handled.
7. Tests exist and have been executed where possible.
8. The UI accurately reflects its implementation status.

Use one of these labels:

- Working & Tested
- Implemented, Hardware Test Pending
- In Development
- Unsupported

Never pretend a placeholder is a finished feature.

# 22. DEVELOPMENT EXECUTION RULES

Do not stop after producing a plan.

Start writing real code.

Before implementation, briefly inspect the available environment and verify the required SDKs.

Then:

1. Initialize the new solution.
2. Create the architecture.
3. Implement the Alpine Edition application shell.
4. Create the tweak engine foundations.
5. Add tests.
6. Build the solution.
7. Correct errors.
8. Report actual results.

Do not make unrelated changes.

Do not rewrite working modules unnecessarily.

Do not implement hundreds of tweaks simultaneously.

Do not invent unsupported APIs.

Do not add functions merely to increase feature count.

If a requirement is technically unsupported, document it and implement a truthful unavailable state.

For each milestone, provide:

- Files changed.
- Features implemented.
- Build result.
- Test results.
- Known limitations.
- Remaining tasks.

Continue implementation within the current execution as far as the available environment and tools allow.

Do not claim completion of unimplemented phases.

# FINAL PRODUCT STANDARD

CARPATIA ULTIMATE EDITION must be:

- Beautiful.
- Lightweight.
- Technically advanced.
- Reliable.
- Reversible where technically possible.
- Honest about performance.
- Safe by default.
- Powerful for experienced users.
- Simple for everyday customers.
- Maintainable over years of development.

The final objective is not to have the largest number of tweaks.

**The objective is to build one of the most trustworthy and capable Windows optimization applications possible.**

Begin Phase A implementation now.

Create the real project.

Write production-quality code.

Build and test what the environment supports.

Deliver working results, not promises.
