using System.Security.AccessControl;
using Carpatia.Ultimate.Broker;
using Xunit;

// Tests deliberately compare Windows ACL enum masks on Linux; no ACL API is invoked.
#pragma warning disable CA1416

namespace Carpatia.Ultimate.Broker.Tests;

public sealed class RecoverySecurityTests
{
    private const string User = "S-1-5-21-111-222-333-1001";

    private static RecoveryAccessEntry[] Entries(bool directory = true, string reader = User)
    {
        var inheritance = directory ? InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit : InheritanceFlags.None;
        return new[]
        {
            new RecoveryAccessEntry(RecoverySecurityPolicy.Administrators, FileSystemRights.FullControl,
                AccessControlType.Allow, inheritance, PropagationFlags.None, !directory),
            new RecoveryAccessEntry(RecoverySecurityPolicy.System, FileSystemRights.FullControl,
                AccessControlType.Allow, inheritance, PropagationFlags.None, !directory),
            new RecoveryAccessEntry(reader, RecoverySecurityPolicy.ReadRights,
                AccessControlType.Allow, inheritance, PropagationFlags.None, !directory)
        };
    }

    [Fact]
    public void AcceptsProtectedAdministratorOwnedDirectoryWithOnlyCorrespondingUserReadAccess()
        => Assert.True(RecoverySecurityPolicy.IsExactAcl(RecoverySecurityPolicy.Administrators, true, Entries(), User, true));

    [Fact]
    public void AcceptsSystemOwnedDirectoryWithSameExactRules()
        => Assert.True(RecoverySecurityPolicy.IsExactAcl(RecoverySecurityPolicy.System, true, Entries(), User, true));

    [Theory]
    [InlineData(null)]
    [InlineData(User)]
    [InlineData(RecoverySecurityPolicy.Users)]
    public void RejectsUserOrUnknownOwnershipEvenIfInitialDaclOnlyAllowsReading(string? owner)
        => Assert.False(RecoverySecurityPolicy.IsExactAcl(owner, true, Entries(), User, true));

    [Fact]
    public void RejectsUnprotectedDirectoryDacl()
        => Assert.False(RecoverySecurityPolicy.IsExactAcl(RecoverySecurityPolicy.Administrators, false, Entries(), User, true));

    [Theory]
    [InlineData(FileSystemRights.Write)]
    [InlineData(FileSystemRights.Modify)]
    [InlineData(FileSystemRights.FullControl)]
    [InlineData(FileSystemRights.ChangePermissions)]
    [InlineData(FileSystemRights.TakeOwnership)]
    public void RejectsAnyUserWriteOrOwnershipRight(FileSystemRights right)
    {
        var entries = Entries();
        entries[2] = entries[2] with { Rights = entries[2].Rights | right };
        Assert.False(RecoverySecurityPolicy.IsExactAcl(RecoverySecurityPolicy.Administrators, true, entries, User, true));
    }

    [Fact]
    public void RejectsBroadReadAccessForAnotherUsersSnapshots()
        => Assert.False(RecoverySecurityPolicy.IsExactAcl(RecoverySecurityPolicy.Administrators, true,
            Entries(reader: RecoverySecurityPolicy.Users), User, true));

    [Fact]
    public void SharedParentsHaveExplicitBuiltinUsersReadAccess()
        => Assert.True(RecoverySecurityPolicy.IsExactAcl(RecoverySecurityPolicy.Administrators, true,
            Entries(reader: RecoverySecurityPolicy.Users), RecoverySecurityPolicy.Users, true));

    [Fact]
    public void RejectsUnexpectedOrInheritedDirectoryRules()
    {
        var inherited = Entries();
        inherited[2] = inherited[2] with { IsInherited = true };
        Assert.False(RecoverySecurityPolicy.IsExactAcl(RecoverySecurityPolicy.Administrators, true, inherited, User, true));
        var extra = Entries().Append(Entries()[2] with { Sid = "S-1-1-0" }).ToArray();
        Assert.False(RecoverySecurityPolicy.IsExactAcl(RecoverySecurityPolicy.Administrators, true, extra, User, true));
    }

    [Fact]
    public void RejectsDenyOrInheritOnlyRulesInsteadOfGuessingEffectivePermissions()
    {
        var deny = Entries();
        deny[2] = deny[2] with { Type = AccessControlType.Deny };
        Assert.False(RecoverySecurityPolicy.IsExactAcl(RecoverySecurityPolicy.Administrators, true, deny, User, true));
        var inheritOnly = Entries();
        inheritOnly[2] = inheritOnly[2] with { Propagation = PropagationFlags.InheritOnly };
        Assert.False(RecoverySecurityPolicy.IsExactAcl(RecoverySecurityPolicy.Administrators, true, inheritOnly, User, true));
    }

    [Fact]
    public void FilesMustHaveOnlyExpectedInheritedRulesAndTrustedOwnership()
    {
        Assert.True(RecoverySecurityPolicy.IsExactAcl(RecoverySecurityPolicy.Administrators, false, Entries(false), User, false));
        var explicitUser = Entries(false);
        explicitUser[2] = explicitUser[2] with { IsInherited = false };
        Assert.False(RecoverySecurityPolicy.IsExactAcl(RecoverySecurityPolicy.Administrators, false, explicitUser, User, false));
        Assert.False(RecoverySecurityPolicy.IsExactAcl(User, false, Entries(false), User, false));
    }

    [Fact]
    public void AncestorMayAllowCreatingChildrenButNeverDeletingOrReplacingProtectedChildren()
    {
        var reader = new RecoveryAccessEntry(RecoverySecurityPolicy.Users,
            FileSystemRights.ReadAndExecute | FileSystemRights.CreateDirectories | FileSystemRights.CreateFiles,
            AccessControlType.Allow, InheritanceFlags.None, PropagationFlags.None, false);
        Assert.True(RecoverySecurityPolicy.IsSafeSystemAncestor(RecoverySecurityPolicy.Administrators, new[] { reader }));
        foreach (var right in new[] { FileSystemRights.DeleteSubdirectoriesAndFiles, FileSystemRights.ChangePermissions,
                     FileSystemRights.TakeOwnership, FileSystemRights.WriteAttributes })
            Assert.False(RecoverySecurityPolicy.IsSafeSystemAncestor(RecoverySecurityPolicy.Administrators,
                new[] { reader with { Rights = reader.Rights | right } }));
    }

    [Fact]
    public void NoProtectedStoreAccessIsPretendedOnNonWindows()
    {
        if (OperatingSystem.IsWindows()) return;
        Assert.Throws<PlatformNotSupportedException>(() => RecoveryStoreLocation.DirectoryPath);
        Assert.Throws<PlatformNotSupportedException>(RecoveryStoreLocation.ValidateReadAccess);
        Assert.Throws<PlatformNotSupportedException>(() => RecoveryStoreLocation.CreateProtectedJournal());
    }
}
