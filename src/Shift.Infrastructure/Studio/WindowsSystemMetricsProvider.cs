using System.Runtime.InteropServices;
using Shift.Application;
using Shift.Domain;
namespace Shift.Infrastructure;

/// <summary>Busy time of the processor group reported by GetSystemTimes; available RAM from GlobalMemoryStatusEx.</summary>
public sealed class WindowsSystemMetricsProvider : ISystemMetricsProvider
{
    private readonly object gate = new();
    private (ulong Idle, ulong Total)? previous;
    public SystemMetrics Sample()
    {
        if (!OperatingSystem.IsWindows()) return new(null, null, null);
        lock (gate)
        {
            double? cpu = null;
            if (GetSystemTimes(out var idle, out var kernel, out var user))
            {
                var total = kernel.Value + user.Value;
                if (previous is { } before && total > before.Total && idle.Value >= before.Idle)
                {
                    var elapsed = total - before.Total;
                    var idleElapsed = idle.Value - before.Idle;
                    if (idleElapsed <= elapsed) cpu = Math.Clamp(100.0 * (elapsed - idleElapsed) / elapsed, 0, 100);
                }
                previous = (idle.Value, total);
            }
            else previous = null;
            var memory = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
            var available = GlobalMemoryStatusEx(ref memory);
            return new(cpu, available ? memory.TotalPhysical : null, available ? memory.AvailablePhysical : null);
        }
    }
    [StructLayout(LayoutKind.Sequential)] private struct NativeFileTime
    { public uint Low, High; public readonly ulong Value => ((ulong)High << 32) | Low; }
    [StructLayout(LayoutKind.Sequential)] private struct MemoryStatus
    {
        public uint Length, Load;
        public ulong TotalPhysical, AvailablePhysical, TotalPageFile, AvailablePageFile, TotalVirtual, AvailableVirtual, AvailableExtendedVirtual;
    }
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out NativeFileTime idle, out NativeFileTime kernel, out NativeFileTime user);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatus memory);
}
