namespace Shift.Domain;

public enum ItemKind
{
    App, Folder, File, Url, Command
}
public enum CommandShell
{
    PowerShell, Cmd
}
public enum ItemStatus
{
    Pending, Running, Success, Failed, Skipped
}
public enum RunStatus
{
    Running, Completed, PartialFailure, Cancelled, Interrupted, Declined
}
public enum AppTheme
{
    Dark, Light, Midnight, Forest
}

public sealed record WorkspaceItem
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; init; } = "";
    public ItemKind Kind
    {
        get; init;
    }
    public string Target { get; init; } = "";
    public string Arguments { get; init; } = "";
    public string WorkingDirectory { get; init; } = "";
    public CommandShell Shell
    {
        get; init;
    }
    public int Order
    {
        get; init;
    }
    public int DelayMs
    {
        get; init;
    }
    public bool Enabled { get; init; } = true;
    public string Note { get; init; } = "";
    // A Fluent glyph identifier; never loads executable or remote icon resources.
    public string Icon { get; init; } = "";
}

public sealed record Workspace
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public string Color { get; init; } = "#A99BFF";
    public bool IsFavorite
    {
        get; init;
    }
    public IReadOnlyList<string> Tags { get; init; } = [];
    public IReadOnlyList<WorkspaceItem> Items { get; init; } = [];
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.UtcNow;

    public Workspace Duplicate(string name) => this with
    {
        Id = Guid.NewGuid(),
        Name = name,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
        Items = Items.Select(i => i with { Id = Guid.NewGuid() }).ToArray(),
        Tags = Tags.ToArray()
    };
}

public sealed record LaunchRunItem(Guid ItemId, string Name, ItemKind Kind, int Order,
    ItemStatus Status = ItemStatus.Pending, string? Error = null,
    DateTimeOffset? StartedAt = null, DateTimeOffset? FinishedAt = null);

public sealed record LaunchRun(Guid Id, Guid WorkspaceId, string WorkspaceName,
    DateTimeOffset StartedAt, DateTimeOffset? FinishedAt, RunStatus Status,
    IReadOnlyList<LaunchRunItem> Items);

public sealed record AppSettings
{
    public AppTheme Theme { get; init; } = AppTheme.Dark;
    public string Language { get; init; } = "ru";
    public bool StartWithWindows
    {
        get; init;
    }
    public bool MinimizeToTray { get; init; } = true;
    public bool Notifications { get; init; } = true;
    public string BackupDirectory { get; init; } = "";
    public bool GlobalHotkeyEnabled
    {
        get; init;
    }
    public bool OnboardingComplete
    {
        get; init;
    }
}
