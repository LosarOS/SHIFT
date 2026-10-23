using Shift.Domain;
namespace Shift.Application;

public enum ApplicationSource { UserStartMenu, SharedStartMenu, Desktop }
public sealed record ApplicationCandidate(string Id, string Name, string ShortcutPath, ApplicationSource Source, string Group);
public sealed record ApplicationDiscovery(IReadOnlyList<ApplicationCandidate> Candidates, int UnavailableLocations, int InvalidShortcuts, bool Limited);
public interface IApplicationDiscovery
{
    Task<ApplicationDiscovery> DiscoverAsync(CancellationToken token = default);
}

/// <summary>Builds a draft from explicitly selected catalog entries; never launches it.</summary>
public static class QuickWorkspaceSetup
{
    public const int MaximumSelection = 100;
    public static Workspace CreateDraft(string name, IReadOnlyList<ApplicationCandidate> catalog, IReadOnlyList<string> selectedIds)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(selectedIds);
        if (selectedIds.Count is < 1 or > MaximumSelection || selectedIds.Distinct(StringComparer.Ordinal).Count() != selectedIds.Count)
            throw new ValidationException([new ValidationIssue("QuickSelection", "Choose 1–100 distinct applications.")]);
        var indexed = catalog.ToDictionary(item => item.Id, StringComparer.Ordinal);
        if (selectedIds.Any(id => !indexed.ContainsKey(id)))
            throw new ValidationException([new ValidationIssue("QuickSelection", "The selected application is no longer in this discovery result.")]);
        var items = selectedIds.Select((id, position) => new WorkspaceItem
        {
            Name = indexed[id].Name,
            Kind = ItemKind.App,
            Target = indexed[id].ShortcutPath,
            Order = position,
            // Deliberately do not resolve or copy any embedded shortcut arguments.
            Arguments = "",
            WorkingDirectory = "",
            Enabled = true
        }).ToArray();
        var workspace = new Workspace { Name = (name ?? "").Trim(), Items = items };
        WorkspaceValidator.Validate(workspace);
        return workspace;
    }
}
