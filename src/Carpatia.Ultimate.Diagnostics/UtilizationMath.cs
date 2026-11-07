namespace Carpatia.Ultimate.Diagnostics;

internal readonly record struct CpuTimes(ulong Idle, ulong Kernel, ulong User);

internal static class UtilizationMath
{
    // GetSystemTimes' kernel value includes idle time. Counters are cumulative 100 ns units.
    internal static double? CpuPercent(CpuTimes before, CpuTimes after)
    {
        if (after.Idle < before.Idle || after.Kernel < before.Kernel || after.User < before.User)
            return null;

        var idle = after.Idle - before.Idle;
        var kernel = after.Kernel - before.Kernel;
        var user = after.User - before.User;
        var total = (double)kernel + user;
        if (total <= 0 || idle > total)
            return null;

        return 100d * (total - idle) / total;
    }

    internal static double? MemoryPercent(ulong total, ulong available) =>
        total == 0 || available > total ? null : 100d * (total - available) / total;
}
