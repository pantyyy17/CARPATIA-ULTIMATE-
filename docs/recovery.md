# Configuration journal and reviewed restoration

The engine restores only resources an operation actually modified. This journal
is not a full-system image, restore point, boot recovery tool or guarantee against
unstable hardware tuning. Native Windows recovery integration is still pending.

## Protected production location

The standard-user app reads records at
`%PROGRAMDATA%\Carpatia\Ultimate\Recovery\<Windows user SID>`. The one-shot
helper creates/writes this location with Administrators ownership and a protected
DACL: Administrators/SYSTEM have full control; the corresponding standard account
has reading access. Requests cannot select a filesystem path. An absent journal
is an empty history, not a backup.

The wrapper rejects untrusted existing owners/permissions, reparse points,
untrusted ancestor replacement rights and unexpected file links. It pins directory
ancestors during guarded operations and leases. It does not take over an existing
untrusted directory or import earlier `%LOCALAPPDATA%` recovery JSON. Effective
Windows ownership, ACL and pinning behavior requires Windows integration tests;
portable security-policy tests establish only the policy decisions.

See the [broker boundary](../src/Carpatia.Ultimate.Broker/README.md). Unsigned app
and helper binaries must eventually be installed in a protected location and
signed; a protected journal does not solve installed-binary tampering.

## Persistence and schema

`FileOperationJournal` stores one schema-1 `operation-{id}.json` record per active
operation history. Records include resource/tweak identity, original, before and
expected state, status, revisions, conflict IDs, timestamps, result detail,
restart/activation requirements and the last successfully observed state.
Adapter state must include every value the operation can modify and its identity.
Required members, nullability and unknown typed state fields are checked rather
than silently defaulted.

Before a system write, the engine persists original state through a same-directory
temporary file, write-through I/O, explicit disk flushing and destination
replacement. If this fails, the adapter is not called. Directory metadata is not
explicitly synchronized, so universal sudden-power-loss durability is not promised.

## Leases, repeated actions and review binding

Engine execution acquires an execution lease and a resource lease. Exclusive file
sharing coordinates cooperating processes using the same journal; lock files stay
present and their handles release the locks. Repeated Apply retains the first
original snapshot. A no-op Apply creates no false backup. Competing tweaks cannot
replace another operation's original state for the same resource.

Apply binds the reviewed adapter definition, before/expected state and projected
change. Restore binds the exact reviewed current state and effective original
configuration again under the leases. A changed resource, stale review or changed
journal target blocks execution rather than substituting a different plan.

Repeated Restore does not rewrite a completed operation. In the reviewed UI flow,
a later external change prevents claiming that the current resource is restored.
The older direct engine API can report historical restoration without touching
later configuration; it is not the privileged UI execution path.

These locks cannot prevent third-party applications from changing Windows. There
is no universal atomic compare-and-swap for these configuration APIs. CPU plan
switches and other external edits can still race a native call; adapters recheck
what their supported API permits and report observable conflicts.

## Partial changes, activation and interruption

A matching command exit alone is insufficient. The engine rereads actual state.
A mismatched write is Failed or Partially Applied; unreadable state is Recovery
Required. A known partial readback is persisted and may be restored only while
current state still matches an owned known state. Later unknown external changes
block automatic restoration.

CPU policy writes additionally require successful plan reapplication. Matching
stored state cannot hide an activation error. A failed Restore activation must be
retried through the adapter even when the stored value already equals the original.
Activation and restart requirements survive later metadata changes. RSS reports
restart requirements after both Apply and Restore.

An interrupted Applying/Restoring record is not success. If a process dies before
partial state can be reread and persisted, that intermediate state may be unknown
and automatic Restore can be blocked. Unknown schema versions, malformed JSON or
invalid recovery data stop further modifications rather than discard history.

## Recovery procedure

1. Stop additional modifications and preserve the journal and independent VM/system
   backup. Do not remove records or lock files to bypass a conflict.
2. Identify the affected resource and original/before/expected/last-observed values.
3. Read current state with the supported Windows interface for that resource.
4. Use Review restoration only when the matching adapter remains compatible.
   Resolve external changes explicitly; do not force a stale snapshot over them.
5. Verify restored state independently and perform a reported Windows restart when
   required. If state is unknown or the system cannot boot, use the independent
   recovery environment rather than assume the settings journal can repair it.

Actual Apply/Restore, interrupted writes, UAC and protected storage must pass
[disposable Windows VM acceptance](windows-validation.md) before release or Phase C.
