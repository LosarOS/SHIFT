namespace Shift.Domain;

public enum WidgetQuickActionKind { Workspace, Folder, File, Url }

/// <summary>Array-shaped JSON with value equality, including legacy empty content.</summary>
public sealed class WidgetQuickActionList : List<WidgetQuickAction>, IEquatable<WidgetQuickActionList>
{
    public WidgetQuickActionList() { }
    public WidgetQuickActionList(IEnumerable<WidgetQuickAction> actions) : base(actions) { }
    public bool Equals(WidgetQuickActionList? other) => other is not null && this.SequenceEqual(other);
    public override bool Equals(object? obj) => obj is WidgetQuickActionList other && Equals(other);
    public override int GetHashCode() { var hash = new HashCode(); foreach (var action in this) hash.Add(action); return hash.ToHashCode(); }
}

/// <summary>Declarative destinations only. Execution belongs to the main shell consent/launch flow.</summary>
public sealed record WidgetQuickAction
{
    public string Title { get; init; } = "";
    public WidgetQuickActionKind Kind { get; init; }
    public Guid? WorkspaceId { get; init; }
    public string Target { get; init; } = "";
}

public static class WidgetQuickActions
{
    public static void Validate(WidgetQuickAction action)
    {
        if (action is null || !Enum.IsDefined(action.Kind) || string.IsNullOrWhiteSpace(action.Title)
            || action.Title.Length > 100 || action.Title.Any(char.IsControl) || action.Target is null)
            throw new ValidationException([new ValidationIssue("Studio.Actions", "Invalid quick action.")]);
        if (action.Kind == WidgetQuickActionKind.Workspace)
        {
            if (action.WorkspaceId is null || action.WorkspaceId == Guid.Empty || action.Target.Length != 0)
                throw new ValidationException([new ValidationIssue("Studio.Actions", "Choose an existing workspace; commands and paths are not supported here.")]);
            return;
        }
        if (action.WorkspaceId is not null)
            throw new ValidationException([new ValidationIssue("Studio.Actions", "Only workspace actions can reference a workspace.")]);
        WorkspaceValidator.Validate(new Workspace { Name = "Quick action", Items = [ToItem(action)] });
    }

    public static WorkspaceItem ToItem(WidgetQuickAction action) => new()
    {
        Name = action.Title, Target = action.Target,
        Kind = action.Kind switch
        {
            WidgetQuickActionKind.Folder => ItemKind.Folder,
            WidgetQuickActionKind.File => ItemKind.File,
            WidgetQuickActionKind.Url => ItemKind.Url,
            _ => throw new InvalidOperationException("Workspace actions must use the saved workspace launch flow.")
        }
    };
}
