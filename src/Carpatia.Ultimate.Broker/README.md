# One-shot privilege boundary

`ControlRequest` is a fixed typed allowlist. It carries no executable, script,
registry path, file path or journal directory. The platform dispatcher must reject
fields unrelated to the selected operation, re-read the exact target, validate
the reviewed snapshot and ask for native confirmation before executing a write.
No confirmation callback means no write authorization.

`ElevatedControlClient` starts only `Helper/Carpatia.Ultimate.Helper.exe` beside
the application using Windows `runas`. One random first-instance local named pipe
carries one bounded request and one result. Its protected ACL grants Read/Write
only to the caller's Windows user SID; the parent verifies the connecting child
PID. The helper requires an elevated token and verifies that the pipe owner is
its same Windows user. UAC with different administrator credentials therefore
fails closed rather than accessing a different user's recovery journal.

`PipeOptions.CurrentUserOnly` is deliberately avoided: on Windows .NET checks
the elevation level as well as the account, rejecting this intended connection
between standard and elevated processes. Explicit SID ACLs preserve the account
boundary across elevation. All this Windows process/ACL behavior still requires
Windows integration testing; portable tests cover only framing and validation.

Frames use a four-byte big-endian length and at most 64 KiB of strict JSON.
Unknown members, duplicate fields, undefined enums, numeric enums, missing
required fields and truncated/oversized frames are rejected. The response cannot
implicitly default into verified success.

Cancellation before sending means no change was submitted. After a message may
have reached the helper, the client stops observing caller cancellation, waits
for its bounded result and never kills the privileged process. A missing result
is Recovery Required, not success. The engine still owns durable pre-write
snapshots and post-write readback.

The recovery journal is mutable configuration data, not authorization. No
privileged filesystem path comes from a message. Production distribution must
install and sign the app/helper in a protected location; this source build is
unsigned, and installed-binary tampering is not solved by the pipe protocol.

`RecoveryStoreLocation.DirectoryPath` derives the journal directory only from
Windows CommonApplicationData and the current account SID:
`%PROGRAMDATA%/Carpatia/Ultimate/Recovery/<SID>`. `CreateProtectedJournal()` is
administrator-only and creates each component with its final protected DACL and
Administrators ownership. Shared parents grant Builtin Users reading/traversal;
the SID leaf grants only that corresponding account read access. Administrators
and SYSTEM retain full control. Existing untrusted ownership, rules or reparse
points are rejected; no directory is taken over and no Phase A user-writable
JSON is automatically imported.

The one-shot helper establishes Administrators as its process token's default
file owner before creating journal/lock files. Otherwise a same-user standard
token could change permissions on user-owned files regardless of their initial
read-only DACL. Directory handles pin the complete ancestor chain without delete
sharing for guarded calls and the lifetime of execution/resource leases.
Existing files must have trusted ownership, the exact inherited read-only ACL,
no reparse attributes and one hard link. System ancestors are checked for
untrusted replacement/ACL rights. Every read, write and lease through the
protected wrapper revalidates storage and fails closed.

These ACL/native-owner/pinning paths are **Implemented, Hardware Test Pending**.
The portable security-policy tests prove only mask/identity rejection behavior,
not effective Windows ACL, UAC or crash-recovery integration. Unusual enterprise
ProgramData ACLs can be deliberately refused instead of silently weakened.

References: [PipeOptions](https://learn.microsoft.com/dotnet/api/system.io.pipes.pipeoptions),
[NamedPipeServerStreamAcl](https://learn.microsoft.com/dotnet/api/system.io.pipes.namedpipeserverstreamacl),
[GetNamedPipeClientProcessId](https://learn.microsoft.com/windows/win32/api/winbase/nf-winbase-getnamedpipeclientprocessid).
