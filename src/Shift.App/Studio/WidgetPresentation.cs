using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Shift.Domain;

namespace Shift.App.Studio;

/// <summary>Pure text and edit merging; native windows remain responsible for rendering and persistence.</summary>
internal static class WidgetPresentation
{
    public static async Task AwaitHostOperationAsync(Task operation, Task hostExited)
    {
        await Task.WhenAny(operation, hostExited).ConfigureAwait(false);
        // Prefer the operation result if shutdown and acknowledgement arrived together.
        if (!operation.IsCompleted) throw new InvalidOperationException("Widget host stopped before the operation completed.");
        await operation.ConfigureAwait(false);
    }

    // WinUI can return CR-only text; a Win32 multiline edit needs CRLF to render
    // those breaks. Persist LF so native CRLF expansion cannot exceed the model's
    // 20,000-character bound. Other Unicode whitespace remains unchanged.
    public static string FromNativeNotesText(string text)
        => text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
    public static string ToNativeNotesText(string text)
        => FromNativeNotesText(text).Replace("\n", "\r\n", StringComparison.Ordinal);

    public static CultureInfo Culture(string language) => CultureInfo.GetCultureInfo(language == "en" ? "en-US" : "ru-RU");
    public static string WindowTitle(string title, string language) => (language == "en" ? "SHIFT Widget — " : "SHIFT Виджет — ") + title;
    public static string Clock(DateTime local, WidgetContent content, string language)
        => local.ToString(content.Use24Hour ? content.ShowSeconds ? "HH:mm:ss" : "HH:mm" : content.ShowSeconds ? "h:mm:ss tt" : "h:mm tt", Culture(language));
    public static (string Cpu, string UsedMemory, string FreeMemory) Metrics(SystemMetrics? metrics, string language)
    {
        var en = language == "en"; var culture = Culture(language);
        var unavailable = en ? "Unavailable" : "Недоступно";
        var cpu = (en ? "CPU  " : "ЦП  ") + (metrics?.CpuPercent is double value && double.IsFinite(value) ? Math.Clamp(value, 0, 100).ToString("F0", culture) + "%" : unavailable);
        if (metrics?.TotalMemoryBytes is not ulong total || metrics.AvailableMemoryBytes is not ulong free || total == 0)
            return (cpu, (en ? "RAM  " : "ОЗУ  ") + unavailable, "");
        free = Math.Min(total, free); const double gib = 1073741824d;
        // Windows memory is reported as bytes. These units explicitly mean GiB (2^30 bytes).
        var unit = en ? " GiB" : " ГиБ";
        return (cpu, (en ? "RAM  " : "ОЗУ  ") + ((total - free) / gib).ToString("F1", culture) + " / " + (total / gib).ToString("F1", culture) + unit,
            (en ? "Free " : "Свободно ") + (free / gib).ToString("F1", culture) + unit);
    }

    public static bool LayoutWasSaved(WidgetInstance saved, WidgetLayout current, WidgetInteraction interaction)
        => saved.Layout == current && saved.Interaction == interaction;

    public static IReadOnlyList<WidgetInstance> WrittenInstances(DesktopProfile saved, IEnumerable<Guid> capturedIds)
    {
        var written = capturedIds.ToHashSet();
        return saved.Widgets.Where(w => written.Contains(w.Id)).ToArray();
    }

    public static DesktopProfile MergeLayoutEdits(DesktopProfile latest, IReadOnlyDictionary<Guid, WidgetInstance> edits)
        => latest with { Widgets = latest.Widgets.Select(w => edits.TryGetValue(w.Id, out var edit) ? w with { Layout = edit.Layout, Interaction = edit.Interaction } : w).ToArray() };

    public static DesktopProfile MergeInteraction(DesktopProfile latest, WidgetInteraction interaction, IReadOnlyDictionary<Guid, WidgetLayout> dirtyLayouts)
        => latest with { Widgets = latest.Widgets.Select(w => w with { Interaction = interaction, Layout = dirtyLayouts.TryGetValue(w.Id, out var layout) ? layout : w.Layout }).ToArray() };
}
