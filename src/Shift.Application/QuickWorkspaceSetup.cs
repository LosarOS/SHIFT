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
    public const int MaximumCatalog = 500;

    /// <summary>Search the current snapshot; filtering never changes a user's explicit selection.</summary>
    public static IReadOnlyList<ApplicationCandidate> Filter(IReadOnlyList<ApplicationCandidate> catalog, string? query)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        var terms = (query ?? "").Split((char[]?)null, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return catalog.Where(c => terms.All(term => c.Name.Contains(term, StringComparison.CurrentCultureIgnoreCase)
            || c.Group.Contains(term, StringComparison.CurrentCultureIgnoreCase))).ToArray();
    }
    public static Workspace CreateDraft(string name, IReadOnlyList<ApplicationCandidate> catalog, IReadOnlyList<string> selectedIds)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(selectedIds);
        if (selectedIds.Count is < 1 or > MaximumSelection || selectedIds.Any(string.IsNullOrWhiteSpace) || selectedIds.Distinct(StringComparer.Ordinal).Count() != selectedIds.Count)
            throw new ValidationException([new ValidationIssue("QuickSelection", "Choose 1–100 distinct applications.")]);
        if (catalog.Count > MaximumCatalog || catalog.Any(c => c is null || string.IsNullOrWhiteSpace(c.Id)
            || string.IsNullOrWhiteSpace(c.Name) || c.ShortcutPath is null || c.Group is null || !Enum.IsDefined(c.Source))
            || catalog.Select(c => c.Id).Distinct(StringComparer.Ordinal).Count() != catalog.Count)
            throw new ValidationException([new ValidationIssue("QuickSelection", "The application catalog is invalid or exceeds discovery limits.")]);
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
