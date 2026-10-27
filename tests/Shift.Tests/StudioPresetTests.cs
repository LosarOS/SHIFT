using Shift.Application;
using Shift.Domain;
using Xunit;

namespace Shift.Tests;

public sealed class StudioPresetTests
{
    private static readonly IReadOnlyDictionary<WidgetKind, string> Titles = new Dictionary<WidgetKind, string>
    {
        [WidgetKind.Clock] = "Часы", [WidgetKind.SystemMonitor] = "Системный монитор",
        [WidgetKind.FocusTimer] = "Фокус", [WidgetKind.Notes] = "Заметки"
    };
    private static MonitorIdentity Display(string id = "primary", int width = 1024, int height = 720,
        double scale = 1, int left = 0, int top = 0, bool primary = true) =>
        new(id, id, left, top, width, height, left, top, width, height, scale, primary);

    [Theory]
    [InlineData("Development", "Midnight", WidgetKind.Notes)]
    [InlineData("Streaming", "Neon", WidgetKind.FocusTimer)]
    [InlineData("Focus", "Forest", WidgetKind.Notes)]
    public void DraftIsCompleteLocalizedAndInert(string preset, string style, WidgetKind expectedKind)
    {
        var draft = StudioPresetCatalog.CreateDraft(preset, "Мой профиль 💜", Titles, [Display()]);
        StudioValidator.Validate(draft.Profile);
        Assert.Equal("Мой профиль 💜", draft.Profile.Name);
        Assert.Null(draft.Profile.WorkspaceId); Assert.False(draft.Profile.ApplyOnWorkspaceLaunch);
        Assert.Empty(draft.Profile.Wallpapers); Assert.False(draft.Profile.ChangeWallpaperPosition);
        Assert.Null(draft.Profile.LastAppliedAt); Assert.Equal(0, draft.Profile.Revision);
        Assert.Equal(3, draft.Definitions.Count); Assert.Equal(3, draft.Profile.Widgets.Count);
        Assert.Contains(draft.Definitions, definition => definition.Kind == expectedKind);
        Assert.All(draft.Definitions, definition =>
        {
            StudioValidator.Validate(definition);
            Assert.Equal(Titles[definition.Kind], definition.Title); Assert.Equal(style, definition.Style.TemplateId);
            Assert.Equal(0, definition.Revision); Assert.Empty(definition.Content.InitialNotes);
        });
        Assert.All(draft.Profile.Widgets, instance =>
        {
            Assert.Single(draft.Definitions, definition => definition.Id == instance.DefinitionId);
            Assert.False(instance.Layout.AlwaysOnTop); Assert.True(instance.Visible);
            Assert.Equal(WidgetInteraction.Edit, instance.Interaction);
        });
        Assert.Empty(draft.Warnings);
    }

    [Fact] public void RepeatedDraftsNeverReuseProfileDefinitionOrInstanceIdentities()
    {
        var drafts = Enumerable.Range(0, 20).Select(_ => StudioPresetCatalog.CreateDraft("Focus", "Focus", Titles)).ToArray();
        var ids = drafts.SelectMany(d => new[] { d.Profile.Id }.Concat(d.Definitions.Select(w => w.Id)).Concat(d.Profile.Widgets.Select(w => w.Id))).ToArray();
        Assert.DoesNotContain(Guid.Empty, ids); Assert.Equal(ids.Length, ids.Distinct().Count());
        Assert.All(drafts, draft => Assert.Equal(["Studio.Preset.NoMonitor"], draft.Warnings));
    }

    [Fact] public void DraftCollectionsDoNotExposeWritableListsOrMutateTheBuiltInRecipe()
    {
        var draft = StudioPresetCatalog.CreateDraft("Focus", "Focus", Titles);
        var original = StudioPresetCatalog.All.Single(p => p.Id == "Focus");
        Assert.Throws<NotSupportedException>(() => ((IList<WidgetDefinition>)draft.Definitions).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<WidgetInstance>)draft.Profile.Widgets).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<WidgetKind>)original.WidgetKinds).Clear());
        var customized = draft.Definitions[0] with { Style = draft.Definitions[0].Style with { Accent = "#123456" } };
        Assert.NotEqual(customized.Style.Accent, StudioTemplates.For(original.StyleTemplateId).Accent);
    }

    [Fact] public void EveryExistingStyleCanBeExplicitlySelectedWithoutChangingThePreset()
    {
        foreach (var style in StudioTemplates.All)
        {
            var draft = StudioPresetCatalog.CreateDraft("Development", "Разработка", Titles, styleTemplateId: style.TemplateId);
            Assert.All(draft.Definitions, definition => Assert.Equal(style, definition.Style));
        }
        Assert.Equal("Midnight", StudioPresetCatalog.All.Single(p => p.Id == "Development").StyleTemplateId);
    }

    [Fact] public void RecipeCatalogCoversAllFourRealWidgetTypesAndHasStableLocalizationKeys()
    {
        Assert.Equal(3, StudioPresetCatalog.All.Count);
        Assert.Equal(Enum.GetValues<WidgetKind>().Order(), StudioPresetCatalog.All.SelectMany(p => p.WidgetKinds).Distinct().Order());
        Assert.All(StudioPresetCatalog.All, recipe =>
        {
            Assert.Equal("Studio.Preset." + recipe.Id, recipe.NameKey);
            Assert.Equal("Studio.Preset." + recipe.Id + "Description", recipe.DescriptionKey);
        });
        Assert.Equal(60, StudioPresetCatalog.CreateDraft("Streaming", "Stream", Titles).Definitions.Single(d => d.Kind == WidgetKind.FocusTimer).Content.FocusMinutes);
        Assert.Equal(25, StudioPresetCatalog.CreateDraft("Focus", "Focus", Titles).Definitions.Single(d => d.Kind == WidgetKind.FocusTimer).Content.FocusMinutes);
    }

    [Fact] public void InvalidNamesMissingTitlesAndUnknownRecipeOrStyleAreRejectedBeforeReturningABundle()
    {
        Assert.Throws<ArgumentException>(() => StudioPresetCatalog.CreateDraft("unknown", "Name", Titles));
        Assert.Throws<ArgumentException>(() => StudioPresetCatalog.CreateDraft("Focus", "Name", Titles, styleTemplateId: "unknown"));
        Assert.Throws<ArgumentException>(() => StudioPresetCatalog.CreateDraft("Focus", "Name", new Dictionary<WidgetKind, string>()));
        Assert.Throws<ValidationException>(() => StudioPresetCatalog.CreateDraft("Focus", "", Titles));
        var invalid = new Dictionary<WidgetKind, string>(Titles) { [WidgetKind.Notes] = "\0" };
        Assert.Throws<ValidationException>(() => StudioPresetCatalog.CreateDraft("Focus", "Name", invalid));
    }

    [Fact] public void CancelledFactoryNeverReturnsAPartialDraft()
    {
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => StudioPresetCatalog.CreateDraft("Focus", "Name", Titles, token: cancellation.Token));
    }

    [Theory]
    [InlineData(1024, 720, 1)]
    [InlineData(1024, 720, 1.25)]
    [InlineData(1024, 720, 1.5)]
    [InlineData(1024, 720, 2)]
    [InlineData(1920, 1040, 2)]
    [InlineData(3840, 2080, 4)]
    [InlineData(600, 800, 1)]
    public void ThreeWidgetPresetFitsTheWorkAreaWithoutOverlap(int width, int height, double scale)
    {
        var monitor = Display(width: width, height: height, scale: scale, left: -width, top: -120);
        var placement = StudioPresetLayout.Arrange(3, [monitor]);
        Assert.Empty(placement.Warnings);
        var bounds = placement.Layouts.Select(layout => WidgetLayoutService.Resolve(layout, [monitor])).ToArray();
        AssertOnscreen(bounds, monitor); AssertNoOverlap(bounds);
        Assert.Equal(placement.Layouts, StudioPresetLayout.Arrange(3, [monitor]).Layouts);
    }

    [Fact] public void PreferredDisplayAndPrimaryFallbackUseRealWorkAreaNotDesktopOrigin()
    {
        var secondary = Display("secondary", 1920, 1040, 1.5, -1920, 40, false);
        var primary = Display("primary", 1280, 680, 1, 0, 40);
        var chosen = StudioPresetLayout.Arrange(3, [secondary, primary], "SECONDARY");
        Assert.All(chosen.Layouts, layout => Assert.Equal("secondary", layout.MonitorId)); Assert.Empty(chosen.Warnings);
        AssertOnscreen(chosen.Layouts.Select(layout => WidgetLayoutService.Resolve(layout, [secondary, primary])), secondary);
        var fallback = StudioPresetLayout.Arrange(3, [secondary, primary], "unplugged");
        Assert.All(fallback.Layouts, layout => Assert.Equal("primary", layout.MonitorId));
        Assert.Equal(["Studio.Preset.MonitorFallback"], fallback.Warnings);
    }

    [Fact] public void OfflineOrInvalidMonitorGeometryUsesAnExplicitPortableFallback()
    {
        foreach (var monitors in new IReadOnlyList<MonitorIdentity>[] { [], [Display(scale: double.NaN)], [Display(width: 0)], [Display(id: "\0")] })
        {
            var placement = StudioPresetLayout.Arrange(3, monitors);
            Assert.Equal(["Studio.Preset.NoMonitor"], placement.Warnings);
            Assert.All(placement.Layouts, layout => { StudioValidator.Validate(layout); Assert.Equal("", layout.MonitorId); });
        }
    }

    [Theory]
    [InlineData(320, 240, 1, 3)]
    [InlineData(1024, 720, 3, 3)]
    [InlineData(120, 80, 1, 1)]
    [InlineData(1024, 720, 2, 64)]
    public void ImpossibleFitWarnsAndKeepsEveryWidgetReachable(int width, int height, double scale, int count)
    {
        var monitor = Display(width: width, height: height, scale: scale);
        var placement = StudioPresetLayout.Arrange(count, [monitor]);
        Assert.Contains("Studio.Preset.LayoutConstrained", placement.Warnings); Assert.Equal(count, placement.Layouts.Count);
        Assert.All(placement.Layouts, StudioValidator.Validate);
        AssertOnscreen(placement.Layouts.Select(layout => WidgetLayoutService.Resolve(layout, [monitor])), monitor);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(10)]
    public void GenericArrangementPacksSupportedCountsWhenRoomExists(int count)
    {
        var monitor = Display(width: 1920, height: 1040);
        var placement = StudioPresetLayout.Arrange(count, [monitor]);
        Assert.Empty(placement.Warnings);
        AssertNoOverlap(placement.Layouts.Select(layout => WidgetLayoutService.Resolve(layout, [monitor])).ToArray());
    }

    [Fact] public void ArrangementBoundsUntrustedCountsAndDoesNotWarnForAnEmptyProfile()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => StudioPresetLayout.Arrange(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => StudioPresetLayout.Arrange(65));
        Assert.Throws<ArgumentOutOfRangeException>(() => StudioPresetLayout.Arrange(3, Enumerable.Range(0, 65).Select(i => Display(i.ToString())).ToArray()));
        var empty = StudioPresetLayout.Arrange(0); Assert.Empty(empty.Layouts); Assert.Empty(empty.Warnings);
    }

    private static void AssertOnscreen(IEnumerable<WidgetBounds> bounds, MonitorIdentity monitor)
    {
        foreach (var rectangle in bounds)
        {
            Assert.True(rectangle.X >= monitor.WorkLeft && rectangle.Y >= monitor.WorkTop);
            Assert.True(rectangle.X + rectangle.Width <= monitor.WorkLeft + monitor.WorkWidth);
            Assert.True(rectangle.Y + rectangle.Height <= monitor.WorkTop + monitor.WorkHeight);
        }
    }
    private static void AssertNoOverlap(IReadOnlyList<WidgetBounds> bounds)
    {
        for (var first = 0; first < bounds.Count; first++)
        for (var second = first + 1; second < bounds.Count; second++)
        {
            var a = bounds[first]; var b = bounds[second];
            Assert.False(a.X < b.X + b.Width && a.X + a.Width > b.X && a.Y < b.Y + b.Height && a.Y + a.Height > b.Y);
        }
    }
}
