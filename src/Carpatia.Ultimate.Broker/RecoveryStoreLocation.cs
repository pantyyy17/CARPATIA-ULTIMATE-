using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using Carpatia.Ultimate.Engine;
using Microsoft.Win32.SafeHandles;

namespace Carpatia.Ultimate.Broker;

/// <summary>
/// Machine-protected recovery snapshots, readable by the corresponding standard
/// user. No request or caller can choose this path. Phase A per-user records are
/// deliberately not imported into this elevated trust boundary.
/// </summary>
public static class RecoveryStoreLocation
{
    public static string DirectoryPath
    {
        get
        {
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Protected recovery storage requires Windows.");
            return GetLocation().Path;
        }
    }

    public static void ValidateReadAccess()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Protected recovery storage requires Windows.");
        using var inspection = InspectHierarchy(GetLocation(), create: false, allowAbsent: true);
    }

    public static IOperationJournal CreateProtectedJournal()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Protected recovery storage requires Windows.");
        using var identity = WindowsIdentity.GetCurrent();
        if (!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
            throw new UnauthorizedAccessException("Only the one-shot elevated helper can create a writable recovery journal.");
        var location = GetLocation();
        // Ownership is as important as the DACL. A user-owned file can have its
        // DACL changed by that user even if the initial ACL grants only reading.
        RecoveryStorageNative.SetDefaultOwnerToAdministrators();
        using (InspectHierarchy(location, create: true, allowAbsent: false)) { }
        return new ProtectedJournal(location);
    }

    [SupportedOSPlatform("windows")]
    private static Location GetLocation()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var sid = identity.User?.Value ?? throw new UnauthorizedAccessException("A Windows user SID is required.");
        var common = Path.GetFullPath(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData));
        if (common.StartsWith(@"\\", StringComparison.Ordinal) || string.IsNullOrWhiteSpace(Path.GetPathRoot(common)))
            throw new UnauthorizedAccessException("Recovery storage requires a local Windows filesystem.");
        return new Location(common, sid, Path.Combine(common, "Carpatia", "Ultimate", "Recovery", sid));
    }

    [SupportedOSPlatform("windows")]
    private static DirectoryPins InspectHierarchy(Location location, bool create, bool allowAbsent)
    {
        var pins = new DirectoryPins();
        try
        {
            // Pin every existing ancestor without delete sharing so it cannot
            // be replaced while a journal call or execution lease is in flight.
            var ancestors = new Stack<DirectoryInfo>();
            for (var current = new DirectoryInfo(location.CommonPath); current is not null; current = current.Parent)
                ancestors.Push(current);
            foreach (var ancestor in ancestors)
            {
                pins.Add(RecoveryStorageNative.PinDirectory(ancestor.FullName));
                RejectReparse(ancestor.FullName);
                var security = ancestor.GetAccessControl(AccessControlSections.Owner | AccessControlSections.Access);
                if (!RecoverySecurityPolicy.IsSafeSystemAncestor(Owner(security), Entries(security)))
                    throw new UnauthorizedAccessException("A system recovery-storage ancestor has unsafe ownership or replacement permissions.");
            }

            var components = new[] { "Carpatia", "Ultimate", "Recovery", location.UserSid };
            var path = location.CommonPath;
            for (var index = 0; index < components.Length; index++)
            {
                path = Path.Combine(path, components[index]);
                var isLeaf = index == components.Length - 1;
                var reader = isLeaf ? location.UserSid : RecoverySecurityPolicy.Users;
                if (!Exists(path))
                {
                    if (!create)
                    {
                        if (allowAbsent) return pins;
                        throw new DirectoryNotFoundException("The protected recovery directory is missing.");
                    }
                    // Security is provided at creation, not applied afterwards.
                    // If another process wins creation, its actual ACL is checked.
                    new DirectoryInfo(path).Create(CreateDirectorySecurity(reader));
                }
                pins.Add(RecoveryStorageNative.PinDirectory(path));
                RejectReparse(path);
                var directory = new DirectoryInfo(path);
                var acl = directory.GetAccessControl(AccessControlSections.Owner | AccessControlSections.Access);
                if (!RecoverySecurityPolicy.IsExactAcl(Owner(acl), acl.AreAccessRulesProtected, Entries(acl), reader, isDirectory: true))
                    throw new UnauthorizedAccessException("An existing recovery directory is untrusted. No ownership or ACL was changed.");
            }
            ValidateFiles(location);
            return pins;
        }
        catch { pins.Dispose(); throw; }
    }

    [SupportedOSPlatform("windows")]
    private static void ValidateFiles(Location location)
    {
        foreach (var path in Directory.EnumerateFileSystemEntries(location.Path))
        {
            var attributes = File.GetAttributes(path);
            if ((attributes & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0)
                throw new UnauthorizedAccessException("Recovery storage contains a redirected or unexpected directory entry.");
            using var handle = RecoveryStorageNative.OpenMetadata(path, shareDelete: true);
            if (RecoveryStorageNative.LinkCount(handle) != 1)
                throw new UnauthorizedAccessException("Hard-linked recovery entries are not accepted.");
            var file = new FileInfo(path);
            var acl = file.GetAccessControl(AccessControlSections.Owner | AccessControlSections.Access);
            if (!RecoverySecurityPolicy.IsExactAcl(Owner(acl), acl.AreAccessRulesProtected, Entries(acl), location.UserSid, isDirectory: false))
                throw new UnauthorizedAccessException("A recovery file has unsafe ownership or permissions.");
        }
    }

    [SupportedOSPlatform("windows")]
    private static DirectorySecurity CreateDirectorySecurity(string readerSid)
    {
        var result = new DirectorySecurity();
        result.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        result.SetOwner(new SecurityIdentifier(RecoverySecurityPolicy.Administrators));
        const InheritanceFlags inheritance = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
        foreach (var sid in new[] { RecoverySecurityPolicy.Administrators, RecoverySecurityPolicy.System })
            result.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(sid), FileSystemRights.FullControl,
                inheritance, PropagationFlags.None, AccessControlType.Allow));
        result.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(readerSid), FileSystemRights.ReadAndExecute,
            inheritance, PropagationFlags.None, AccessControlType.Allow));
        return result;
    }

    [SupportedOSPlatform("windows")]
    private static string? Owner(FileSystemSecurity security)
        => (security.GetOwner(typeof(SecurityIdentifier)) as SecurityIdentifier)?.Value;

    [SupportedOSPlatform("windows")]
    private static RecoveryAccessEntry[] Entries(FileSystemSecurity security)
        => security.GetAccessRules(includeExplicit: true, includeInherited: true, typeof(SecurityIdentifier))
            .Cast<FileSystemAccessRule>().Select(x => new RecoveryAccessEntry(x.IdentityReference.Value,
                x.FileSystemRights, x.AccessControlType, x.InheritanceFlags, x.PropagationFlags, x.IsInherited)).ToArray();

    private static bool Exists(string path)
    {
        try { return (File.GetAttributes(path) & FileAttributes.Directory) != 0; }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
    }

    private static void RejectReparse(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new UnauthorizedAccessException("Recovery storage cannot use symbolic links, junctions or redirected directories.");
    }

    private sealed record Location(string CommonPath, string UserSid, string Path);

    private sealed class DirectoryPins : IDisposable
    {
        private readonly List<SafeFileHandle> _handles = new();
        public void Add(SafeFileHandle handle) => _handles.Add(handle);
        public void Dispose() { for (var i = _handles.Count - 1; i >= 0; i--) _handles[i].Dispose(); _handles.Clear(); }
    }

    [SupportedOSPlatform("windows")]
    private sealed class ProtectedJournal(Location location) : IOperationJournal
    {
        private readonly FileOperationJournal _inner = new(location.Path);
        public async ValueTask<IReadOnlyList<OperationRecord>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            using var pins = InspectHierarchy(location, create: false, allowAbsent: false);
            return await _inner.GetAllAsync(cancellationToken);
        }
        public async ValueTask SaveAsync(OperationRecord record, CancellationToken cancellationToken = default)
        {
            using var pins = InspectHierarchy(location, create: false, allowAbsent: false);
            await _inner.SaveAsync(record, cancellationToken);
            ValidateFiles(location);
        }
        public async ValueTask<IAsyncDisposable> AcquireExecutionLeaseAsync(CancellationToken cancellationToken = default)
            => await AcquireAsync(null, cancellationToken);
        public async ValueTask<IAsyncDisposable> AcquireResourceLeaseAsync(string resourceId, CancellationToken cancellationToken = default)
            => await AcquireAsync(resourceId, cancellationToken);
        private async ValueTask<IAsyncDisposable> AcquireAsync(string? resourceId, CancellationToken cancellationToken)
        {
            var pins = InspectHierarchy(location, create: false, allowAbsent: false);
            IAsyncDisposable? lease = null;
            try
            {
                lease = resourceId is null ? await _inner.AcquireExecutionLeaseAsync(cancellationToken)
                    : await _inner.AcquireResourceLeaseAsync(resourceId, cancellationToken);
                ValidateFiles(location);
                return new PinnedLease(lease, pins);
            }
            catch
            {
                if (lease is not null) await lease.DisposeAsync();
                pins.Dispose();
                throw;
            }
        }
    }

    private sealed class PinnedLease(IAsyncDisposable lease, DirectoryPins pins) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            try { await lease.DisposeAsync(); }
            finally { pins.Dispose(); }
        }
    }
}
