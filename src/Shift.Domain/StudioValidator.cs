using System.Globalization;
using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
namespace Shift.Domain;

public static class StudioValidator
{
    public static readonly IReadOnlyList<string> SafeFonts = Array.AsReadOnly(new[] { "Segoe UI", "Segoe UI Variable", "Cascadia Mono", "Consolas", "Arial" });
    private static void Require([DoesNotReturnIf(false)] bool valid, string code, string message)
    { if (!valid) throw new ValidationException(new[] { new ValidationIssue("Studio." + code, message) }); }
    private static bool Name(string? value, int limit = 100) => !string.IsNullOrWhiteSpace(value) && value.Length <= limit && !value.Any(char.IsControl);
    private static bool Color(string? value) => value is not null && Regex.IsMatch(value, "^#[0-9a-fA-F]{6}$");
    private static bool Number(double value, double min, double max) => double.IsFinite(value) && value >= min && value <= max;
    public static void Validate(WidgetLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        Require(layout.MonitorId is not null && layout.MonitorId.Length <= 2048 && !layout.MonitorId.Any(char.IsControl), "Monitor", "Invalid display identifier.");
        Require(Number(layout.X, 0, 1) && Number(layout.Y, 0, 1), "Position", "Position must be normalized between 0 and 1.");
        Require(Number(layout.Width, 160, 1600) && Number(layout.Height, 100, 1200), "Size", "Widget size must be 160–1600 by 100–1200 logical pixels.");
    }
    public static void Validate(WidgetDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        Require(definition.SchemaVersion == 1 && definition.Id != Guid.Empty && definition.Revision >= 0 && Enum.IsDefined(definition.Kind), "Definition", "Unsupported widget definition.");
        Require(Name(definition.Title), "Name", "Use a title with 1–100 characters.");
        var style = definition.Style; var content = definition.Content;
        Require(style is not null && content is not null, "Content", "Widget appearance and content are required.");
        Require(Name(style!.TemplateId) && Enum.IsDefined(style.Surface) && Color(style.Background) && Color(style.Foreground) && Color(style.Accent), "Appearance", "Invalid widget appearance.");
        Require(Number(style.Opacity, .25, 1) && Number(style.FontSize, 10, 96) && Number(style.CornerRadius, 0, 64) && Number(style.Padding, 0, 64) && Number(style.BorderWidth, 0, 8), "Appearance", "Appearance values exceed supported bounds.");
        Require(SafeFonts.Contains(style.FontFamily, StringComparer.Ordinal), "Font", "Choose a supported local font.");
        Require(content!.InitialNotes is not null && content.InitialNotes.Length <= 20000 && !content.InitialNotes.Contains('\0'), "Notes", "Notes exceed 20000 characters or contain a null character.");
        Require(content.FocusMinutes is >= 1 and <= 1440 && content.UpdateIntervalSeconds is >= 1 and <= 3600, "Interval", "Invalid timer duration or update interval.");
        Require(Name(content.DateFormat, 80), "DateFormat", "Invalid date format.");
        try { _ = DateTimeOffset.UtcNow.ToString(content.DateFormat, CultureInfo.InvariantCulture); }
        catch (FormatException) { throw new ValidationException(new[] { new ValidationIssue("Studio.DateFormat", "Invalid date format.") }); }
    }
    public static void Validate(DesktopProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        Require(profile.Id != Guid.Empty && profile.Revision >= 0 && profile.WorkspaceId != Guid.Empty && Enum.IsDefined(profile.WallpaperPosition), "Profile", "Invalid profile identity or placement.");
        Require(Name(profile.Name) && profile.Description is not null && profile.Description.Length <= 2000, "Name", "Use a profile name with 1–100 characters and description up to 2000 characters.");
        Require(profile.Wallpapers is not null && profile.Wallpapers.Count <= 32 && profile.Widgets is not null && profile.Widgets.Count <= 64, "Limits", "A profile supports up to 32 displays and 64 widgets.");
        Require(!profile.ApplyOnWorkspaceLaunch || profile.Wallpapers!.Count == 0 && !profile.ChangeWallpaperPosition, "AutomaticWallpaper", "Automatic workspace apply supports widget-only profiles; confirm static wallpaper changes manually.");
        Require(profile.Wallpapers!.All(w => w is not null && Name(w.MonitorId, 2048) && LocalPath(w.ImagePath)) && profile.Wallpapers.Select(w => w.MonitorId).Distinct(StringComparer.OrdinalIgnoreCase).Count() == profile.Wallpapers.Count, "Wallpaper", "Use a unique display and absolute local image path for each wallpaper.");
        Require(profile.Widgets!.All(w => w is not null && w.Id != Guid.Empty && w.DefinitionId != Guid.Empty && Enum.IsDefined(w.Interaction)) && profile.Widgets.Select(w => w.Id).Distinct().Count() == profile.Widgets.Count, "Instances", "Widget instances must have unique valid IDs.");
        foreach (var widget in profile.Widgets) Validate(widget.Layout);
    }
    private static bool LocalPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > 32767 || path.Any(char.IsControl)
            || path.StartsWith("\\\\", StringComparison.Ordinal) || path.StartsWith("//", StringComparison.Ordinal)
            || path.Contains("://", StringComparison.Ordinal) || path.IndexOfAny(['*', '?', '"', '<', '>', '|']) >= 0) return false;
        var windows = path.Length >= 3 && char.IsAsciiLetter(path[0]) && path[1] == ':' && path[2] is '\\' or '/';
        if (!windows && !Path.IsPathFullyQualified(path)) return false;
        var tail = windows ? path[2..] : path;
        return !tail.Contains(':') && tail.Split(['\\', '/']).All(part => part is not ("." or "..") && !part.EndsWith(' ') && !part.EndsWith('.'));
    }
    public static void Validate(WidgetRuntimeState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        Require(state.InstanceId != Guid.Empty && state.Revision >= 0 && state.Notes is not null && state.Notes.Length <= 20000 && !state.Notes.Contains('\0'), "State", "Invalid widget state.");
        Require(state.Timer is not null && Number(state.Timer.RemainingSeconds, 0, 86400), "Timer", "Invalid saved timer duration.");
        Require(!state.Timer!.Running || state.Timer.EndsAtUtc is not null && state.Timer.SavedAtUtc is not null, "Timer", "Running timers require a saved deadline.");
    }
    public static void Validate(StudioSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Require(Name(settings.Palette) && settings.ConsecutiveRuntimeFailures is >= 0 and <= 1000000 && settings.ActiveProfileId != Guid.Empty, "Settings", "Invalid Studio settings.");
    }
    public static void Validate(StudioRecoveryJournal journal)
    {
        ArgumentNullException.ThrowIfNull(journal);
        Require(journal.Id != Guid.Empty && journal.ProfileId != Guid.Empty && journal.PreviousProfileId != Guid.Empty && Enum.IsDefined(journal.Status), "Journal", "Invalid recovery journal.");
        Require(journal.Before is not null && journal.Intended is not null && journal.AttemptedMonitorIds is not null && journal.AppliedMonitorIds is not null && journal.Errors is not null, "Journal", "Incomplete recovery journal.");
        Require(journal.Before!.Wallpapers is not null && journal.Intended!.Wallpapers is not null && journal.Before.Wallpapers.Count <= 32 && journal.Intended.Wallpapers.Count <= 32 && journal.AttemptedMonitorIds!.Count <= 32 && journal.AppliedMonitorIds!.Count <= 32 && journal.Errors!.Count <= 100, "Journal", "Recovery journal exceeds limits.");
        Require(journal.AttemptedMonitorIds!.All(id => Name(id, 2048)) && journal.AppliedMonitorIds!.All(id => journal.AttemptedMonitorIds.Contains(id)), "Journal", "Recovery action IDs are invalid.");
        foreach (var snapshot in new[] { journal.Before, journal.Intended })
        {
            Require(Enum.IsDefined(snapshot!.Position) && snapshot.Wallpapers.All(w => w is not null && Name(w.MonitorId, 2048) && w.ImagePath is not null && w.ImagePath.Length <= 32767 && !w.ImagePath.Contains('\0')), "Journal", "Recovery snapshot is invalid.");
            Require(snapshot.Wallpapers.Select(w => w.MonitorId).Distinct(StringComparer.OrdinalIgnoreCase).Count() == snapshot.Wallpapers.Count, "Journal", "Duplicate snapshot display IDs.");
        }
        Require(journal.Errors!.All(e => e is not null && e.Length <= 2000), "Journal", "Recovery error exceeds limits.");
        Require(journal.RecoveryWallpapers is not null && journal.RecoveryWallpapers.Count <= 32
            && journal.RecoveryWallpapers.All(w => w is not null && Name(w.MonitorId, 2048) && LocalPath(w.ImagePath)
                && journal.Before.Wallpapers.Any(before => before.MonitorId == w.MonitorId))
            && journal.RecoveryWallpapers.Select(w => w.MonitorId).Distinct(StringComparer.OrdinalIgnoreCase).Count() == journal.RecoveryWallpapers.Count,
            "Journal", "Managed recovery copies are invalid.");
        if (journal.PreviousProfileSnapshot is not null) Validate(journal.PreviousProfileSnapshot);
        Require(journal.PreviousDefinitions is not null && journal.PreviousDefinitions.Count <= 128, "Journal", "Recovery definitions exceed limits.");
        foreach (var definition in journal.PreviousDefinitions!) Validate(definition);
    }
}
