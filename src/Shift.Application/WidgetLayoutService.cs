using Shift.Domain;
namespace Shift.Application;

public static class WidgetLayoutService
{
    private static MonitorIdentity Choose(string id, IReadOnlyList<MonitorIdentity> monitors)
        => monitors.FirstOrDefault(m => m.DeviceId == id) ?? monitors.FirstOrDefault(m => m.Primary) ?? monitors.FirstOrDefault() ?? throw new InvalidOperationException("No display is available.");
    public static WidgetBounds Resolve(WidgetLayout layout, IReadOnlyList<MonitorIdentity> monitors)
    {
        StudioValidator.Validate(layout);
        var monitor = Choose(layout.MonitorId, monitors);
        if (monitor.WorkWidth <= 0 || monitor.WorkHeight <= 0 || !double.IsFinite(monitor.DpiScale) || monitor.DpiScale <= 0) throw new InvalidOperationException("Invalid monitor geometry.");
        var width = Math.Clamp((int)Math.Round(layout.Width * monitor.DpiScale), Math.Min(80, monitor.WorkWidth), monitor.WorkWidth);
        var height = Math.Clamp((int)Math.Round(layout.Height * monitor.DpiScale), Math.Min(60, monitor.WorkHeight), monitor.WorkHeight);
        return new(monitor.WorkLeft + (int)Math.Round(layout.X * (monitor.WorkWidth - width)), monitor.WorkTop + (int)Math.Round(layout.Y * (monitor.WorkHeight - height)), width, height, monitor.DeviceId);
    }
    public static WidgetLayout Capture(WidgetBounds bounds, IReadOnlyList<MonitorIdentity> monitors)
    {
        var monitor = Choose(bounds.MonitorId, monitors);
        if (monitor.DpiScale <= 0 || !double.IsFinite(monitor.DpiScale) || monitor.WorkWidth <= 0 || monitor.WorkHeight <= 0) throw new InvalidOperationException("Invalid monitor geometry.");
        return new() { MonitorId = monitor.DeviceId, X = Math.Clamp((double)(bounds.X - monitor.WorkLeft) / Math.Max(1, monitor.WorkWidth - bounds.Width), 0, 1), Y = Math.Clamp((double)(bounds.Y - monitor.WorkTop) / Math.Max(1, monitor.WorkHeight - bounds.Height), 0, 1), Width = Math.Clamp(bounds.Width / monitor.DpiScale, 160, 1600), Height = Math.Clamp(bounds.Height / monitor.DpiScale, 100, 1200) };
    }
}
