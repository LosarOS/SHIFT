using Shift.Domain;
namespace Shift.Application;

public enum CompositionState { Ready, Duplicate, Invalid }
public sealed record CompositionEntry(int Line, string Target, CompositionState State, WorkspaceItem? Item, IReadOnlyList<string> Codes);
public sealed record CompositionPreview(Workspace Source, IReadOnlyList<CompositionEntry> Entries)
{
    public int Ready => Entries.Count(e => e.State == CompositionState.Ready);
    public int Duplicates => Entries.Count(e => e.State == CompositionState.Duplicate);
    public int Invalid => Entries.Count(e => e.State == CompositionState.Invalid);
    public int RemainingSlots => Math.Max(0, 100 - Source.Items.Count);
}

/// <summary>Bounded format-only composition. No file access, clipboard access, commands or dispatch.</summary>
public static class WorkspaceComposer
{
    public const int MaximumInput = 65536;
    public static CompositionPreview Preview(Workspace source, string text, ItemKind kind)
    {
        WorkspaceValidator.Validate(source);
        ArgumentNullException.ThrowIfNull(text);
        if (kind is not (ItemKind.Url or ItemKind.App or ItemKind.File or ItemKind.Folder))
            throw Error("Compose.Kind", "Choose URL, App, File or Folder. Commands cannot be inferred.");
        if (text.Length > MaximumInput || text.Contains('\0'))
            throw Error("Compose.Input", "Use up to 65536 characters without nulls.");
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
        if (lines.Count(l => !string.IsNullOrWhiteSpace(l)) is < 1 or > 100)
            throw Error("Compose.Input", "Paste 1–100 nonempty lines.");
        var seen = new HashSet<string>(source.Items.Where(i => i.Kind != ItemKind.Command).Select(i => TargetKey(i.Kind, i.Target)), StringComparer.Ordinal);
        var entries = new List<CompositionEntry>();
        for (var index = 0; index < lines.Length; index++)
        {
            if (string.IsNullOrWhiteSpace(lines[index])) continue;
            var target = lines[index].Trim();
            if (target.Length >= 2 && target[0] == '"' && target[^1] == '"') target = target[1..^1];
            if (kind != ItemKind.Url) target = Environment.ExpandEnvironmentVariables(target);
            var item = new WorkspaceItem { Name = NameFor(kind, target), Kind = kind, Target = target, Enabled = false };
            try
            {
                WorkspaceValidator.Validate(new Workspace { Name = "Composition", Items = [item] });
                if (kind == ItemKind.Url && new Uri(target, UriKind.Absolute).UserInfo.Length > 0)
                    throw Error("Compose.Credentials", "Do not paste URLs with embedded credentials.");
                var duplicate = !seen.Add(TargetKey(kind, target));
                entries.Add(new(index + 1, target, duplicate ? CompositionState.Duplicate : CompositionState.Ready, item,
                    duplicate ? ["Compose.Duplicate"] : []));
            }
            catch (ValidationException ex)
            {
                entries.Add(new(index + 1, target, CompositionState.Invalid, null, ex.Issues.Select(i => i.Code).Distinct().ToArray()));
            }
        }
        var snapshot = source with { Items = Array.AsReadOnly(source.Items.ToArray()), Tags = Array.AsReadOnly(source.Tags.ToArray()) };
        return new(snapshot, entries.AsReadOnly());
    }

    public static Workspace Apply(Workspace current, CompositionPreview preview, IReadOnlyList<int> selectedLines, int delayMs = 0, bool enabled = false)
    {
        WorkspaceValidator.Validate(current);
        ArgumentNullException.ThrowIfNull(preview);
        ArgumentNullException.ThrowIfNull(selectedLines);
        if (!SameSnapshot(current, preview.Source))
            throw Error("Compose.Stale", "The workspace changed. Reopen composition before saving.");
        if (delayMs is < 0 or > 300000) throw Error("Delay", "Delay must be 0–300000 ms.");
        if (selectedLines.Count == 0 || selectedLines.Distinct().Count() != selectedLines.Count
            || current.Items.Count + selectedLines.Count > 100
            || preview.Entries is null || preview.Entries.Any(e => e is null)
            || preview.Entries.Select(e => e.Line).Distinct().Count() != preview.Entries.Count)
            throw Error("Compose.Selection", "Choose distinct valid rows within the workspace limit.");
        var selected = selectedLines.ToHashSet();
        var additions = preview.Entries.Where(e => selected.Contains(e.Line)).OrderBy(e => e.Line).ToArray();
        if (additions.Length != selected.Count || additions.Any(e => e.State != CompositionState.Ready || e.Item is null))
            throw Error("Compose.Selection", "Only valid, nonduplicate preview rows may be added.");
        var items = current.Items.OrderBy(i => i.Order).ThenBy(i => i.Id)
            .Concat(additions.Select(e => e.Item! with { DelayMs = delayMs, Enabled = enabled }))
            .Select((item, position) => item with { Order = position }).ToArray();
        var updated = current with { Items = items };
        WorkspaceValidator.Validate(updated);
        return updated;
    }

    public static bool SameSnapshot(Workspace left, Workspace right) =>
        left is not null && right is not null && left.Items is not null && right.Items is not null
        && left.Tags is not null && right.Tags is not null
        && (left with { Items = right.Items, Tags = right.Tags }) == right
        && left.Items.SequenceEqual(right.Items) && left.Tags.SequenceEqual(right.Tags);

    internal static string TargetKey(ItemKind kind, string target)
    {
        if (kind == ItemKind.Url && Uri.TryCreate(target, UriKind.Absolute, out var uri))
            return "url:" + uri.GetComponents(UriComponents.SchemeAndServer, UriFormat.UriEscaped).ToUpperInvariant()
                + "|" + uri.GetComponents(UriComponents.PathAndQuery | UriComponents.Fragment, UriFormat.UriEscaped);
        return "local:" + Environment.ExpandEnvironmentVariables(target).Replace('/', '\\').TrimEnd('\\').ToUpperInvariant();
    }
    private static string NameFor(ItemKind kind, string target)
    {
        var name = kind == ItemKind.Url && Uri.TryCreate(target, UriKind.Absolute, out var uri) && uri.Host.Length > 0
            ? uri.Host + uri.AbsolutePath : target.Replace('\\', '/').TrimEnd('/').Split('/').Last();
        if (name.Length == 0) name = "Item";
        if (name.Length > 100) name = name[..(char.IsHighSurrogate(name[99]) ? 99 : 100)];
        return name;
    }
    private static ValidationException Error(string code, string message) => new([new ValidationIssue(code, message)]);
}
