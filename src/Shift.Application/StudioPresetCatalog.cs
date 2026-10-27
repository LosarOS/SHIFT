using Shift.Domain;

namespace Shift.Application;

/// <summary>A built-in recipe. Keys are resolved by presentation; no user language is stored here.</summary>
public sealed record StudioPresetDescriptor(string Id, string NameKey, string DescriptionKey,
    string StyleTemplateId, IReadOnlyList<WidgetKind> WidgetKinds);

/// <summary>A validated, inert draft. Creating it never reads files, persists data or changes Windows.</summary>
public sealed record StudioPresetDraft(DesktopProfile Profile, IReadOnlyList<WidgetDefinition> Definitions,
    IReadOnlyList<string> Warnings);

/// <summary>Stores the complete new bundle atomically. Implementations must reject existing/reused identities.</summary>
public interface IStudioPresetStore
{
    Task<DesktopProfile> SavePresetAsync(StudioPresetDraft draft, CancellationToken token = default);
}

public static class StudioPresetCatalog
{
    public static IReadOnlyList<StudioPresetDescriptor> All { get; } = Array.AsReadOnly(new[]
    {
        Recipe("Development", "Midnight", WidgetKind.Clock, WidgetKind.SystemMonitor, WidgetKind.Notes),
        Recipe("Streaming", "Neon", WidgetKind.Clock, WidgetKind.SystemMonitor, WidgetKind.FocusTimer),
        Recipe("Focus", "Forest", WidgetKind.Clock, WidgetKind.Notes, WidgetKind.FocusTimer)
    });

    private static StudioPresetDescriptor Recipe(string id, string style, params WidgetKind[] kinds) =>
        new(id, "Studio.Preset." + id, "Studio.Preset." + id + "Description", style, Array.AsReadOnly(kinds));

    public static StudioPresetDraft CreateDraft(string presetId, string profileName,
        IReadOnlyDictionary<WidgetKind, string> widgetTitles, IReadOnlyList<MonitorIdentity>? monitors = null,
        string? preferredMonitorId = null, string? styleTemplateId = null, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(widgetTitles);
        var recipe = All.FirstOrDefault(p => string.Equals(p.Id, presetId, StringComparison.Ordinal))
            ?? throw new ArgumentException("Unknown Studio preset.", nameof(presetId));
        var style = StudioTemplates.For(styleTemplateId ?? recipe.StyleTemplateId);
        var placement = StudioPresetLayout.Arrange(recipe.WidgetKinds.Count, monitors, preferredMonitorId);
        var definitions = new List<WidgetDefinition>();
        var instances = new List<WidgetInstance>();
        for (var index = 0; index < recipe.WidgetKinds.Count; index++)
        {
            token.ThrowIfCancellationRequested();
            var kind = recipe.WidgetKinds[index];
            if (!widgetTitles.TryGetValue(kind, out var title))
                throw new ArgumentException("A localized title is required for each preset widget.", nameof(widgetTitles));
            var definition = new WidgetDefinition
            {
                Title = title,
                Kind = kind,
                Style = style,
                Content = new WidgetContent
                {
                    FocusMinutes = recipe.Id == "Streaming" ? 60 : 25,
                    ShowSeconds = false,
                    UpdateIntervalSeconds = 2,
                    InitialNotes = ""
                }
            };
            StudioValidator.Validate(definition);
            definitions.Add(definition);
            instances.Add(new WidgetInstance { DefinitionId = definition.Id, Layout = placement.Layouts[index] });
        }
        var profile = new DesktopProfile
        {
            Name = profileName,
            Widgets = instances.AsReadOnly()
        };
        StudioValidator.Validate(profile);
        token.ThrowIfCancellationRequested();
        return new(profile, definitions.AsReadOnly(), placement.Warnings);
    }
}
