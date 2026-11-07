using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Carpatia.Ultimate.Diagnostics;

[SupportedOSPlatform("windows")]
internal static class NativeMethods
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct FileTime
    {
        public uint Low;
        public uint High;
        public readonly ulong Value => ((ulong)High << 32) | Low;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MemoryStatus
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhysical;
        public ulong AvailablePhysical;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetSystemTimes(out FileTime idle, out FileTime kernel, out FileTime user);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern uint GetActiveProcessorCount(ushort groupNumber);

    internal static int LogicalProcessorCount
    {
        get
        {
            const ushort AllProcessorGroups = 0xffff;
            var count = GetActiveProcessorCount(AllProcessorGroups);
            return count is > 0 and <= int.MaxValue ? (int)count : Environment.ProcessorCount;
        }
    }

    internal static CpuTimes? ReadCpuTimes() => GetSystemTimes(out var idle, out var kernel, out var user)
        ? new CpuTimes(idle.Value, kernel.Value, user.Value) : null;

    internal static (ulong Total, ulong Available)? ReadMemory()
    {
        var status = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        return GlobalMemoryStatusEx(ref status) && status.AvailablePhysical <= status.TotalPhysical
            ? (status.TotalPhysical, status.AvailablePhysical) : null;
    }
}
