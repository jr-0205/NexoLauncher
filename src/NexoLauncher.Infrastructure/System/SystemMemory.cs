using System.Runtime.InteropServices;

namespace NexoLauncher.Infrastructure.System;

public sealed record SystemMemorySnapshot(long TotalMiB, long AvailableMiB)
{
    public double LoadPercentage => TotalMiB <= 0 ? 0 : Math.Clamp((TotalMiB - AvailableMiB) * 100d / TotalMiB, 0, 100);
}

public static class SystemMemory
{
    public static SystemMemorySnapshot GetSnapshot()
    {
        if (OperatingSystem.IsWindows())
        {
            var status = new MemoryStatusEx();
            if (GlobalMemoryStatusEx(ref status))
            {
                return new SystemMemorySnapshot(
                    ToMiB(status.TotalPhysical),
                    ToMiB(status.AvailablePhysical));
            }
        }

        var bytes = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
        var total = bytes > 0 ? bytes / (1024 * 1024) : 0;
        return new SystemMemorySnapshot(total, total);
    }

    public static long GetTotalMemoryMiB() => GetSnapshot().TotalMiB;

    private static long ToMiB(ulong bytes) => (long)Math.Min(long.MaxValue, bytes / (1024UL * 1024UL));

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MemoryStatusEx
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

        public MemoryStatusEx()
        {
            Length = (uint)Marshal.SizeOf<MemoryStatusEx>();
            MemoryLoad = 0;
            TotalPhysical = 0;
            AvailablePhysical = 0;
            TotalPageFile = 0;
            AvailablePageFile = 0;
            TotalVirtual = 0;
            AvailableVirtual = 0;
            AvailableExtendedVirtual = 0;
        }
    }
}
