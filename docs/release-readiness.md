# Remaining milestones and distribution

## Phase B verification gate

The first CPU/network operation families now have typed adapters, UI review,
one-shot helper routing, durable snapshots, verification and reviewed Restore.
Their isolated tests and selected read-only Windows checks pass. The complete
Windows build and bounded process-startup observation also pass. Native writes
and failure recovery remain pending. Do not expand into Phase C until disposable Windows tests
verify the first operations and resolve any critical restoration defect.

The immediate gate is interactive Windows acceptance, followed by real
CPU/network Apply/Verify/Restore, explicit confirmation, protected
storage/UAC tests, partial-write recovery and documented hardware compatibility.
No performance benefit is established by matching configuration readback.

## Later phases

- **C:** Expand independently verified adapters; operational profile planning and
  execution; service dependency management; conservative application removal with
  explicit reinstatement limitations.
- **D:** Investigate official vendor APIs. Unsupported controls stay unavailable;
  experimental hardware tuning remains off by default with explicit risk review.
- **E:** Windows accessibility, measured startup/idle resources, installer/update
  design, signing, clean install/upgrade/uninstall, security review and release QA.

The app does not provide universal rollback, boot recovery, a licensing server,
storefront, background optimizer or permanent elevated service.

## Installer and binary boundary

MSIX offers package identity, managed installation and uninstall. Evaluate its
interaction with the one-shot elevated helper and protected recovery data before
choosing it. An unpackaged app with a signed traditional installer offers more
flexible deployment but owns prerequisite installation, trusted binary paths,
update handling and cleanup.

The current app/helper are self-contained unsigned development builds. The main
app expects the complete helper payload in its adjacent `Helper/` subdirectory.
Production must install and sign both in a protected location; the pipe protocol
and journal ACLs do not protect a user-writable installed executable from tampering.
Test helper resolution and payload completeness in install/upgrade scenarios.

Preserve original recovery records until configuration is restored or retention
is explicitly resolved. Do not silently delete the protected journal on uninstall
or import old user-writable JSON into its trust boundary. Document retained data
and a supported cleanup procedure before releasing an installer.

Updates must validate trusted signatures, version metadata and rollback behavior.
There is no commercial updater or signed installer in this milestone.

## Privacy

The application works locally and includes no telemetry, advertisements or
licensing traffic. Hardware details and recovery records remain local. The UI
can copy diagnostic data at the user's request; review such data before sharing.
No automatic upload is implemented.

SDK/NuGet restore and CI require development-time network access. Runtime network
configuration uses local supported Windows interfaces and a fixed system
PowerShell provider. Choosing DNS changes the machine's resolver configuration;
normal Windows resolver traffic then follows that configuration. It is not app
telemetry. Any future online update feature needs documented network behavior
and an updated privacy statement before release.
