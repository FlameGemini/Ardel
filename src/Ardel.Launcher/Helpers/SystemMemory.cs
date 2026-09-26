using System.Runtime.InteropServices;

namespace Ardel.Launcher.Helpers;

/// <summary>Windows physical memory snapshot for the RAM preview bar.</summary>
internal static class SystemMemory
{
    public static bool TryGet(out long totalBytes, out long availableBytes)
    {
        totalBytes = 0;
        availableBytes = 0;
        var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        if (!GlobalMemoryStatusEx(ref status))
            return false;

        totalBytes = (long)status.TotalPhys;
        availableBytes = (long)status.AvailPhys;
        return totalBytes > 0;
    }

    /// <summary>Single-shot total/used MiB so preview labels cannot disagree.</summary>
    public static bool TryGetMegabytes(out int totalMb, out int usedMb)
    {
        totalMb = 0;
        usedMb = 0;
        if (!TryGet(out var totalBytes, out var availableBytes))
            return false;

        totalMb = BytesToMb(totalBytes);
        if (totalMb <= 0)
            return false;

        var usedBytes = Math.Max(0L, totalBytes - Math.Min(availableBytes, totalBytes));
        usedMb = Math.Clamp(BytesToMb(usedBytes), 0, totalMb);
        return true;
    }

    public static int TotalMegabytes
    {
        get
        {
            if (TryGetMegabytes(out var total, out _))
                return Math.Clamp(total, 1024, 262144);
            return 16384;
        }
    }

    public static int UsedMegabytes
    {
        get
        {
            if (TryGetMegabytes(out _, out var used))
                return used;
            return 0;
        }
    }

    private static int BytesToMb(long bytes) =>
        (int)Math.Clamp(bytes / (1024L * 1024L), 0, 262144);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);
}
