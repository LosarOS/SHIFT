using Shift.Domain;

namespace Shift.Application;

public sealed record StudioPresetPlacement(IReadOnlyList<WidgetLayout> Layouts, IReadOnlyList<string> Warnings);

/// <summary>Deterministic logical geometry; it never queries Windows or moves an existing window.</summary>
public static class StudioPresetLayout
{
    private const double DesiredWidth = 300;
    private const double DesiredHeight = 220;
    private const double MinWidth = 160;
    private const double MinHeight = 100;
    private sealed record Grid(int Columns, int Rows, int Width, int Height, double Score, int Balance);

    public static StudioPresetPlacement Arrange(int count, IReadOnlyList<MonitorIdentity>? monitors = null,
        string? preferredMonitorId = null)
    {
        if (count is < 0 or > 64) throw new ArgumentOutOfRangeException(nameof(count));
        if (monitors?.Count > 64) throw new ArgumentOutOfRangeException(nameof(monitors));
        if (count == 0) return new(Array.Empty<WidgetLayout>(), Array.Empty<string>());
        var warnings = new List<string>();
        var available = (monitors ?? []).Where(Usable).ToArray();
        var monitor = available.FirstOrDefault(m => string.Equals(m.DeviceId, preferredMonitorId, StringComparison.OrdinalIgnoreCase));
        if (monitor is null)
        {
            monitor = available.FirstOrDefault(m => m.Primary) ?? available.FirstOrDefault();
            if (monitor is null)
            {
                // Offline creation retains normalized positions. Runtime will resolve the real primary display.
                monitor = new("", "", 0, 0, 1024, 768, 0, 0, 1024, 720, 1, true);
                warnings.Add("Studio.Preset.NoMonitor");
            }
            else if (!string.IsNullOrEmpty(preferredMonitorId)) warnings.Add("Studio.Preset.MonitorFallback");
        }
        var scale = monitor.DpiScale;
        var margin = (int)Math.Floor(Math.Min(24 * scale, Math.Min(monitor.WorkWidth, monitor.WorkHeight) * .04));
        var areaWidth = monitor.WorkWidth - 2 * margin;
        var areaHeight = monitor.WorkHeight - 2 * margin;
        var gap = (int)Math.Floor(Math.Min(16 * scale, Math.Min(areaWidth, areaHeight) * .05));
        var minimumWidth = (int)Math.Ceiling(MinWidth * scale);
        var minimumHeight = (int)Math.Ceiling(MinHeight * scale);
        var desiredWidth = (int)Math.Round(DesiredWidth * scale);
        var desiredHeight = (int)Math.Round(DesiredHeight * scale);
        Grid? best = null;
        for (var columns = 1; columns <= count; columns++)
        {
            var rows = (count + columns - 1) / columns;
            var width = Math.Min(desiredWidth, (areaWidth - (columns - 1) * gap) / columns);
            var height = Math.Min(desiredHeight, (areaHeight - (rows - 1) * gap) / rows);
            if (width < minimumWidth || height < minimumHeight) continue;
            // Preserve useful widget area first, then prefer a balanced grid, with horizontal ties.
            var candidate = new Grid(columns, rows, width, height,
                Math.Min((double)width / desiredWidth, (double)height / desiredHeight), Math.Abs(columns - rows));
            if (best is null || candidate.Score > best.Score + .000001
                || Math.Abs(candidate.Score - best.Score) <= .000001 && (candidate.Balance < best.Balance
                    || candidate.Balance == best.Balance && candidate.Columns > best.Columns)) best = candidate;
        }
        var layouts = new List<WidgetLayout>();
        if (best is not null)
        {
            // Cluster at the right of the work area, leaving the usual left-hand desktop icon column free.
            var groupWidth = best.Columns * best.Width + (best.Columns - 1) * gap;
            var startX = monitor.WorkWidth - margin - groupWidth;
            for (var index = 0; index < count; index++)
            {
                layouts.Add(Layout(monitor, startX + index % best.Columns * (best.Width + gap),
                    margin + index / best.Columns * (best.Height + gap), best.Width, best.Height));
            }
        }
        else
        {
            warnings.Add("Studio.Preset.LayoutConstrained");
            // Keep supported logical sizes and all windows reachable. Overlap is explicit when the screen
            // cannot contain every minimum-size widget; never pretend that an impossible layout fits.
            var columns = Math.Min(count, Math.Max(1, areaWidth / Math.Max(1, minimumWidth + gap)));
            var rows = (count + columns - 1) / columns;
            var width = Math.Min(minimumWidth, monitor.WorkWidth);
            var height = Math.Min(minimumHeight, monitor.WorkHeight);
            for (var index = 0; index < count; index++)
            {
                var x = columns == 1 ? .5 : (double)(index % columns) / (columns - 1);
                var y = rows == 1 ? .5 : (double)(index / columns) / (rows - 1);
                layouts.Add(new WidgetLayout { MonitorId = monitor.DeviceId, X = x, Y = y,
                    Width = Math.Max(MinWidth, width / scale), Height = Math.Max(MinHeight, height / scale) });
            }
        }
        foreach (var layout in layouts) StudioValidator.Validate(layout);
        return new(layouts.AsReadOnly(), warnings.AsReadOnly());
    }

    private static WidgetLayout Layout(MonitorIdentity monitor, int x, int y, int width, int height) => new()
    {
        MonitorId = monitor.DeviceId,
        X = (double)x / Math.Max(1, monitor.WorkWidth - width),
        Y = (double)y / Math.Max(1, monitor.WorkHeight - height),
        Width = width / monitor.DpiScale,
        Height = height / monitor.DpiScale
    };

    private static bool Usable(MonitorIdentity monitor) => monitor is not null
        && !string.IsNullOrWhiteSpace(monitor.DeviceId) && monitor.DeviceId.Length <= 2048 && !monitor.DeviceId.Any(char.IsControl)
        && monitor.WorkWidth > 0 && monitor.WorkHeight > 0
        && (long)monitor.WorkLeft + monitor.WorkWidth <= int.MaxValue
        && (long)monitor.WorkTop + monitor.WorkHeight <= int.MaxValue
        && double.IsFinite(monitor.DpiScale) && monitor.DpiScale is >= .25 and <= 16;
}
