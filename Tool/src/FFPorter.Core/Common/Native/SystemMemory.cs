using System.Runtime.InteropServices;

namespace FFPorter.Core.Common.Native;

public static partial class SystemMemory
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Status
    {
        public uint Length;
        public uint Load;
        public ulong TotalPhysical;
        public ulong AvailablePhysical;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GlobalMemoryStatusEx(ref Status status);

    public static long Total => Query(out Status status) && status.TotalPhysical > 0
        ? (long)Math.Min(status.TotalPhysical, long.MaxValue)
        : Math.Max(GC.GetGCMemoryInfo().TotalAvailableMemoryBytes, 8L << 30);

    public static long Available => Query(out Status status) ? (long)Math.Min(status.AvailablePhysical, long.MaxValue) : long.MaxValue;

    private static bool Query(out Status status)
    {
        status = new Status { Length = (uint)Marshal.SizeOf<Status>() };
        return GlobalMemoryStatusEx(ref status);
    }
}
