using System.Security.AccessControl;

// These enum constants are portable integer masks; this file invokes no Windows API.
#pragma warning disable CA1416

namespace Carpatia.Ultimate.Broker;

internal sealed record RecoveryAccessEntry(string Sid, FileSystemRights Rights,
    AccessControlType Type, InheritanceFlags Inheritance, PropagationFlags Propagation, bool IsInherited);

/// <summary>Pure representation permits testing the ACL rules without invoking Windows APIs.</summary>
internal static class RecoverySecurityPolicy
{
    internal const string Administrators = "S-1-5-32-544";
    internal const string System = "S-1-5-18";
    internal const string Users = "S-1-5-32-545";
    internal const string TrustedInstaller = "S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464";
    internal const FileSystemRights ReadRights = FileSystemRights.ReadAndExecute | FileSystemRights.Synchronize;
    private const InheritanceFlags DirectoryInheritance = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;

    internal static bool IsTrustedOwner(string? sid) => sid is Administrators or System;

    internal static bool IsExactAcl(string? owner, bool isProtected,
        IReadOnlyList<RecoveryAccessEntry> entries, string readerSid, bool isDirectory)
    {
        if (!IsTrustedOwner(owner) || isDirectory && !isProtected || entries.Count != 3) return false;
        var expected = new Dictionary<string, FileSystemRights>(StringComparer.Ordinal)
        {
            [Administrators] = FileSystemRights.FullControl,
            [System] = FileSystemRights.FullControl,
            [readerSid] = ReadRights
        };
        if (expected.Count != 3) return false;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            if (!seen.Add(entry.Sid) || !expected.TryGetValue(entry.Sid, out var rights)
                || entry.Type != AccessControlType.Allow || entry.Rights != rights
                || entry.Propagation != PropagationFlags.None
                || entry.Inheritance != (isDirectory ? DirectoryInheritance : InheritanceFlags.None)
                || entry.IsInherited == isDirectory)
                return false;
        }
        return true;
    }

    internal static bool IsSafeSystemAncestor(string? owner, IReadOnlyList<RecoveryAccessEntry> entries)
    {
        if (owner is not (Administrators or System or TrustedInstaller)) return false;
        const FileSystemRights destructive = FileSystemRights.DeleteSubdirectoriesAndFiles
            | FileSystemRights.ChangePermissions | FileSystemRights.TakeOwnership | FileSystemRights.WriteAttributes;
        return entries.All(entry => entry.Type != AccessControlType.Allow
            || entry.Propagation.HasFlag(PropagationFlags.InheritOnly)
            || entry.Sid is Administrators or System or TrustedInstaller
            || (entry.Rights & destructive) == 0);
    }
}
