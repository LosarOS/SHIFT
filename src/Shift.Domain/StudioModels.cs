namespace Shift.Domain;

public enum WidgetKind { Clock, SystemMonitor, FocusTimer, Notes }
public enum WidgetSurface { Solid, Translucent, Acrylic, Mica }
public enum WidgetInteraction { Edit, Desktop, ClickThrough }
public enum WallpaperPlacement { Center, Tile, Stretch, Fit, Fill, Span }
public enum StudioApplyStatus { Preparing, Applying, Applied, PartiallyApplied, Failed, Interrupted, Restored, Kept }

public sealed record MonitorIdentity(string DeviceId, string Name, int Left, int Top, int Width, int Height,
    int WorkLeft, int WorkTop, int WorkWidth, int WorkHeight, double DpiScale = 1, bool Primary = false);
public sealed record WallpaperAssignment(string MonitorId, string ImagePath);
public sealed record WallpaperSnapshot(IReadOnlyList<WallpaperAssignment> Wallpapers, WallpaperPlacement Position,
    bool CanRestore, bool SlideshowActive = false, string? Limitation = null);
public sealed record WidgetLayout
{
    public string MonitorId { get; init; } = "";
    public double X { get; init; } = .7;
    public double Y { get; init; } = .1;
    public double Width { get; init; } = 320;
    public double Height { get; init; } = 180;
    public bool AlwaysOnTop { get; init; }
}
public sealed record WidgetStyle
{
    public string TemplateId { get; init; } = "Midnight";
    public WidgetSurface Surface { get; init; } = WidgetSurface.Solid;
    public string Background { get; init; } = "#20212B";
    public string Foreground { get; init; } = "#F5F5FA";
    public string Accent { get; init; } = "#A99BFF";
    public double Opacity { get; init; } = 1;
    public string FontFamily { get; init; } = "Segoe UI";
    public double FontSize { get; init; } = 20;
    public double CornerRadius { get; init; } = 16;
    public double Padding { get; init; } = 16;
    public double BorderWidth { get; init; } = 1;
    public bool Shadow { get; init; } = true;
}
public sealed record WidgetContent
{
    public bool Use24Hour { get; init; } = true;
    public bool ShowSeconds { get; init; }
    public string DateFormat { get; init; } = "ddd, dd MMM yyyy";
    public int FocusMinutes { get; init; } = 25;
    public int UpdateIntervalSeconds { get; init; } = 2;
    public string InitialNotes { get; init; } = "";
}
public sealed record WidgetDefinition
{
    public int SchemaVersion { get; init; } = 1;
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Title { get; init; } = "Widget";
    public WidgetKind Kind { get; init; }
    public WidgetStyle Style { get; init; } = new();
    public WidgetContent Content { get; init; } = new();
    public long Revision { get; init; }
}
public sealed record WidgetInstance
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid DefinitionId { get; init; }
    public WidgetLayout Layout { get; init; } = new();
    public bool Visible { get; init; } = true;
    public WidgetInteraction Interaction { get; init; } = WidgetInteraction.Edit;
}
public sealed record FocusTimerState
{
    public bool Running { get; init; }
    public double RemainingSeconds { get; init; } = 1500;
    public DateTimeOffset? SavedAtUtc { get; init; }
    public DateTimeOffset? EndsAtUtc { get; init; }
    public bool CompletionNotified { get; init; }
}
public sealed record WidgetRuntimeState
{
    public Guid InstanceId { get; init; }
    public string Notes { get; init; } = "";
    public FocusTimerState Timer { get; init; } = new();
    public long Revision { get; init; }
}
public sealed record DesktopProfile
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; init; } = "Desktop profile";
    public string Description { get; init; } = "";
    public Guid? WorkspaceId { get; init; }
    public bool ApplyOnWorkspaceLaunch { get; init; }
    public bool Enabled { get; init; } = true;
    public bool ChangeWallpaperPosition { get; init; }
    public WallpaperPlacement WallpaperPosition { get; init; } = WallpaperPlacement.Fill;
    public IReadOnlyList<WallpaperAssignment> Wallpapers { get; init; } = [];
    public IReadOnlyList<WidgetInstance> Widgets { get; init; } = [];
    public DateTimeOffset? LastAppliedAt { get; init; }
    public long Revision { get; init; }
    public DesktopProfile Duplicate(string name) => this with { Id = Guid.NewGuid(), Name = name, WorkspaceId = null,
        ApplyOnWorkspaceLaunch = false, LastAppliedAt = null, Revision = 0,
        Wallpapers = Wallpapers.ToArray(), Widgets = Widgets.Select(w => w with { Id = Guid.NewGuid() }).ToArray() };
}
public sealed record StudioSettings
{
    public bool Enabled { get; init; }
    public Guid? ActiveProfileId { get; init; }
    public bool WallpaperConsentGranted { get; init; }
    public bool RuntimeWasRunning { get; init; }
    public int ConsecutiveRuntimeFailures { get; init; }
    public string Palette { get; init; } = "Midnight";
}
public sealed record StudioRecoveryJournal
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid ProfileId { get; init; }
    public Guid? PreviousProfileId { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public StudioApplyStatus Status { get; init; } = StudioApplyStatus.Preparing;
    public WallpaperSnapshot Before { get; init; } = new([], WallpaperPlacement.Fill, true);
    public WallpaperSnapshot Intended { get; init; } = new([], WallpaperPlacement.Fill, true);
    // Persist intent before each external operation; a crash between OS mutation and acknowledgement is ambiguous.
    public IReadOnlyList<string> AttemptedMonitorIds { get; init; } = [];
    public IReadOnlyList<string> AppliedMonitorIds { get; init; } = [];
    public bool PositionAttempted { get; init; }
    public bool PositionApplied { get; init; }
    public bool WidgetsAttempted { get; init; }
    public IReadOnlyList<string> Errors { get; init; } = [];
}
public sealed record StudioApplyResult(Guid JournalId, StudioApplyStatus Status, IReadOnlyList<string> Errors);
public sealed record StudioRestoreResult(bool Restored, IReadOnlyList<string> Conflicts, IReadOnlyList<string> Errors);
public sealed record SystemMetrics(double? CpuPercent, ulong? TotalMemoryBytes, ulong? AvailableMemoryBytes);
public sealed record WidgetBounds(int X, int Y, int Width, int Height, string MonitorId);
