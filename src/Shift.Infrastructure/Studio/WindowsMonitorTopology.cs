using System.Runtime.InteropServices;
using Shift.Application;
using Shift.Domain;
namespace Shift.Infrastructure;

/// <summary>Physical work areas plus QueryDisplayConfig target paths; no persistent hardware identity is guaranteed.</summary>
public sealed class WindowsMonitorTopology : IMonitorTopology
{
    public IReadOnlyList<string> Warnings { get; private set; } = [];
    public Task<IReadOnlyList<MonitorIdentity>> GetMonitorsAsync(CancellationToken token = default)
    {
        if (!OperatingSystem.IsWindows()) return Task.FromException<IReadOnlyList<MonitorIdentity>>(new PlatformNotSupportedException("Monitor topology requires Windows."));
        return Task.Run<IReadOnlyList<MonitorIdentity>>(() =>
        {
            token.ThrowIfCancellationRequested();
            var warnings = new List<string>();
            var targets = ReadDisplayTargets(warnings);
            var monitors = new List<MonitorIdentity>();
            MonitorEnum callback = (nint handle, nint dc, ref Rect rect, nint data) =>
            {
                var info = new MonitorInfo { Size = (uint)Marshal.SizeOf<MonitorInfo>() };
                if (!GetMonitorInfo(handle, ref info)) return true;
                var scale = 1.0;
                if (GetScaleFactorForMonitor(handle, out var factor) == 0 && factor is >= 100 and <= 500) scale = factor / 100.0;
                else warnings.Add($"DPI unavailable for {info.Device}; using a 100% placement fallback, not a measured scale.");
                var sourceTargets = targets.Where(t => string.Equals(t.Source, info.Device, StringComparison.OrdinalIgnoreCase)).ToArray();
                if (sourceTargets.Length == 0)
                {
                    warnings.Add($"No stable target path for {info.Device}; display-name identity is a temporary fallback.");
                    sourceTargets = [new(info.Device, info.Device, info.Device)];
                }
                foreach (var target in sourceTargets)
                    monitors.Add(new(target.Path, string.IsNullOrWhiteSpace(target.Name) ? info.Device : target.Name,
                        info.Monitor.Left, info.Monitor.Top, info.Monitor.Right - info.Monitor.Left, info.Monitor.Bottom - info.Monitor.Top,
                        info.Work.Left, info.Work.Top, info.Work.Right - info.Work.Left, info.Work.Bottom - info.Work.Top,
                        scale, (info.Flags & 1) != 0));
                return monitors.Count < 64;
            };
            if (!EnumDisplayMonitors(0, 0, callback, 0) && monitors.Count == 0)
                throw new IOException("Windows monitor enumeration failed.");
            token.ThrowIfCancellationRequested();
            if (monitors.Count == 0) throw new IOException("No available desktop monitor was found.");
            Warnings = warnings.ToArray();
            return monitors;
        }, token);
    }
    private sealed record DisplayTarget(string Source, string Path, string Name);
    private static IReadOnlyList<DisplayTarget> ReadDisplayTargets(List<string> warnings)
    {
        const uint activePaths = 2;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var result = GetDisplayConfigBufferSizes(activePaths, out var pathCount, out var modeCount);
            if (result != 0 || pathCount == 0 || pathCount > 128 || modeCount > 512) break;
            var paths = new PathInfo[pathCount];
            var modes = Marshal.AllocHGlobal(checked((int)Math.Max(1, modeCount) * 64)); // DISPLAYCONFIG_MODE_INFO: SDK union48 + header16.
            try
            {
                result = QueryDisplayConfig(activePaths, ref pathCount, paths, ref modeCount, modes, 0);
                if (result == 122) continue; // Topology changed between sizing and query; retry boundedly.
                if (result != 0) break;
                var targets = new List<DisplayTarget>();
                foreach (var path in paths.Take((int)pathCount))
                {
                    var source = new SourceDeviceName { Header = new() { Type = 1, Size = (uint)Marshal.SizeOf<SourceDeviceName>(), AdapterId = path.Source.AdapterId, Id = path.Source.Id } };
                    var target = new TargetDeviceName { Header = new() { Type = 2, Size = (uint)Marshal.SizeOf<TargetDeviceName>(), AdapterId = path.Target.AdapterId, Id = path.Target.Id } };
                    if (GetSourceName(ref source) == 0 && GetTargetName(ref target) == 0 && !string.IsNullOrWhiteSpace(target.DevicePath))
                        targets.Add(new(source.Device, target.DevicePath, target.FriendlyName));
                }
                if (targets.Count != 0) return targets;
                break;
            }
            finally { Marshal.FreeHGlobal(modes); }
        }
        warnings.Add("QueryDisplayConfig target identity is unavailable (including some RDP sessions); placement uses display-name fallback.");
        return [];
    }
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct MonitorInfo
    { public uint Size; public Rect Monitor, Work; public uint Flags; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Device; }
    [StructLayout(LayoutKind.Sequential)] private struct Luid { public uint Low; public int High; }
    [StructLayout(LayoutKind.Sequential)] private struct SourceInfo { public Luid AdapterId; public uint Id, ModeIndex, Status; }
    [StructLayout(LayoutKind.Sequential)] private struct Rational { public uint Numerator, Denominator; }
    [StructLayout(LayoutKind.Sequential)] private struct TargetInfo
    { public Luid AdapterId; public uint Id, ModeIndex, Technology, Rotation, Scaling; public Rational Refresh; public uint Scanline; [MarshalAs(UnmanagedType.Bool)] public bool Available; public uint Status; }
    [StructLayout(LayoutKind.Sequential)] private struct PathInfo { public SourceInfo Source; public TargetInfo Target; public uint Flags; }
    [StructLayout(LayoutKind.Sequential)] private struct DeviceHeader { public uint Type, Size; public Luid AdapterId; public uint Id; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct SourceDeviceName
    { public DeviceHeader Header; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Device; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct TargetDeviceName
    { public DeviceHeader Header; public uint Flags, Technology; public ushort Manufacturer, Product; public uint Connector; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string FriendlyName; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DevicePath; }
    private delegate bool MonitorEnum(nint monitor, nint dc, ref Rect rect, nint data);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool EnumDisplayMonitors(nint dc, nint clip, MonitorEnum callback, nint data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetMonitorInfoW")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
    [DllImport("shcore.dll")] private static extern int GetScaleFactorForMonitor(nint monitor, out int factor);
    [DllImport("user32.dll")] private static extern int GetDisplayConfigBufferSizes(uint flags, out uint paths, out uint modes);
    [DllImport("user32.dll")] private static extern int QueryDisplayConfig(uint flags, ref uint paths, [In, Out] PathInfo[] pathInfo, ref uint modes, nint modeInfo, nint topology);
    [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo")] private static extern int GetSourceName(ref SourceDeviceName source);
    [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo")] private static extern int GetTargetName(ref TargetDeviceName target);
}
