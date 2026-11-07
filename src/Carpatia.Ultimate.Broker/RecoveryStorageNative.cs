using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

namespace Carpatia.Ultimate.Broker;

[SupportedOSPlatform("windows")]
internal static class RecoveryStorageNative
{
    internal static SafeFileHandle PinDirectory(string path) => OpenMetadata(path, shareDelete: false);

    internal static SafeFileHandle OpenMetadata(string path, bool shareDelete)
    {
        const uint readControl = 0x00020000, readAttributes = 0x80, shareRead = 1, shareWrite = 2,
            shareDeleteFlag = 4, openExisting = 3, backupSemantics = 0x02000000, openReparsePoint = 0x00200000;
        var handle = CreateFileW(path, readControl | readAttributes,
            shareRead | shareWrite | (shareDelete ? shareDeleteFlag : 0), IntPtr.Zero,
            openExisting, backupSemantics | openReparsePoint, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            var error = Marshal.GetLastWin32Error();
            handle.Dispose();
            throw new Win32Exception(error, "Recovery entry could not be inspected safely.");
        }
        return handle;
    }

    internal static uint LinkCount(SafeFileHandle file)
    {
        if (!GetFileInformationByHandle(file, out var info)) throw new Win32Exception(Marshal.GetLastWin32Error());
        return info.NumberOfLinks;
    }

    internal static void SetDefaultOwnerToAdministrators()
    {
        const uint query = 0x0008, adjustDefault = 0x0080;
        if (!OpenProcessToken(new IntPtr(-1), query | adjustDefault, out var token))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "The helper token cannot establish protected file ownership.");
        using (token)
        {
            var sid = new SecurityIdentifier(RecoverySecurityPolicy.Administrators);
            var bytes = new byte[sid.BinaryLength];
            sid.GetBinaryForm(bytes, 0);
            var pinned = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            var owner = Marshal.AllocHGlobal(IntPtr.Size);
            try
            {
                Marshal.WriteIntPtr(owner, pinned.AddrOfPinnedObject());
                if (!SetTokenInformation(token, 4, owner, (uint)IntPtr.Size))
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Protected recovery-file ownership could not be established.");
                using var updated = WindowsIdentity.GetCurrent();
                if (updated.Owner?.Value != RecoverySecurityPolicy.Administrators)
                    throw new UnauthorizedAccessException("The helper's default file owner is not Administrators.");
            }
            finally { Marshal.FreeHGlobal(owner); pinned.Free(); }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileInformation
    {
        public uint Attributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime, AccessTime, WriteTime;
        public uint VolumeSerialNumber, FileSizeHigh, FileSizeLow, NumberOfLinks, FileIndexHigh, FileIndexLow;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    private static extern SafeFileHandle CreateFileW(string path, uint access, uint share, IntPtr security,
        uint disposition, uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out FileInformation information);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenProcessToken(IntPtr process, uint access, out SafeAccessTokenHandle token);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetTokenInformation(SafeAccessTokenHandle token, int informationClass, IntPtr information, uint length);
}
