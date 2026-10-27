using System.Globalization;
using Shift.App.Studio;
using Shift.Domain;
using Xunit;

namespace Shift.Tests;

public class WidgetPresentationTests
{
    [Fact] public void RussianReadingsUseChosenLanguageInsteadOfHostCulture()
    {
        var old = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            var reading = WidgetPresentation.Metrics(new(37.1, 8UL * 1073741824, 3UL * 1073741824 / 2), "ru");
            Assert.Equal("ЦП  37%", reading.Cpu);
            Assert.Equal("ОЗУ  6,5 / 8,0 ГиБ", reading.UsedMemory);
            Assert.Equal("Свободно 1,5 ГиБ", reading.FreeMemory);
            Assert.Equal("SHIFT Виджет — Мои часы", WidgetPresentation.WindowTitle("Мои часы", "ru"));
            Assert.Equal("14:03:09", WidgetPresentation.Clock(new(2026, 10, 27, 14, 3, 9), new() { Use24Hour = true, ShowSeconds = true }, "ru"));
        }
        finally { CultureInfo.CurrentCulture = old; }
    }
    [Fact] public void EnglishReadingsRemainEnglishOnRussianHost()
    {
        var old = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ru-RU");
            var reading = WidgetPresentation.Metrics(new(0, 8UL * 1073741824, 3UL * 1073741824 / 2), "en");
            Assert.Equal("CPU  0%", reading.Cpu);
            Assert.Equal("RAM  6.5 / 8.0 GiB", reading.UsedMemory);
            Assert.Equal("Free 1.5 GiB", reading.FreeMemory);
            Assert.Equal("SHIFT Widget — My clock", WidgetPresentation.WindowTitle("My clock", "en"));
            Assert.Equal("2:03 PM", WidgetPresentation.Clock(new(2026, 10, 27, 14, 3, 9), new() { Use24Hour = false, ShowSeconds = false }, "en"));
        }
        finally { CultureInfo.CurrentCulture = old; }
    }
    [Fact] public void MissingMetricsAreUnavailableWhileMeasuredZeroIsPreserved()
    {
        Assert.Equal(("ЦП  Недоступно", "ОЗУ  Недоступно", ""), WidgetPresentation.Metrics(null, "ru"));
        Assert.Equal("CPU  Unavailable", WidgetPresentation.Metrics(new(double.NaN, null, null), "en").Cpu);
        Assert.Equal("RAM  Unavailable", WidgetPresentation.Metrics(new(0, 0, 0), "en").UsedMemory);
        Assert.Equal(("CPU  0%", "RAM  0.0 / 1.0 GiB", "Free 1.0 GiB"), WidgetPresentation.Metrics(new(0, 1073741824, ulong.MaxValue), "en"));
    }
    [Fact] public void DragOrModeChangeDuringDatabaseAwaitRemainsDirtyUntilThatExactEditIsSaved()
    {
        var saved = new WidgetInstance { Layout = new() { X = .2, Y = .3 }, Interaction = WidgetInteraction.Edit };
        var movedDuringAwait = saved.Layout with { X = .7 };
        Assert.False(WidgetPresentation.LayoutWasSaved(saved, movedDuringAwait, WidgetInteraction.Edit));
        Assert.False(WidgetPresentation.LayoutWasSaved(saved, saved.Layout, WidgetInteraction.Desktop));
        Assert.True(WidgetPresentation.LayoutWasSaved(saved with { Layout = movedDuringAwait, Interaction = WidgetInteraction.Desktop }, movedDuringAwait, WidgetInteraction.Desktop));
    }
    [Fact] public void ModeChangePreservesUnflushedGeometryAndConcurrentProfileEdits()
    {
        var moved = new WidgetInstance(); var hidden = new WidgetInstance { Visible = false, Layout = new() { X = .8 } };
        var latest = new DesktopProfile { Name = "External edit", Description = "Must survive", Revision = 7, Widgets = [moved, hidden] };
        var live = moved.Layout with { X = .31, Y = .42, Width = 432, Height = 222, AlwaysOnTop = true };
        var merged = WidgetPresentation.MergeInteraction(latest, WidgetInteraction.Desktop, new Dictionary<Guid, WidgetLayout> { [moved.Id] = live, [Guid.NewGuid()] = new() });
        Assert.Equal(latest.Name, merged.Name); Assert.Equal(latest.Description, merged.Description); Assert.Equal(7, merged.Revision);
        Assert.Equal(2, merged.Widgets.Count); Assert.Equal(live, merged.Widgets[0].Layout); Assert.Equal(hidden.Layout, merged.Widgets[1].Layout);
        Assert.False(merged.Widgets[1].Visible); Assert.All(merged.Widgets, w => Assert.Equal(WidgetInteraction.Desktop, w.Interaction));
        Assert.NotEqual(live, latest.Widgets[0].Layout); // Never mutates the caller's record.
    }
}
