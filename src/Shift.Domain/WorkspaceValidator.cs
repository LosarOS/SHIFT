namespace Shift.Domain;

public sealed record ValidationIssue(string Code, string Message, string? Subject = null);
public sealed class ValidationException : Exception
{
    public IReadOnlyList<ValidationIssue> Issues
    {
        get;
    }
    public ValidationException(IEnumerable<string> errors) : this(errors.Select(e => new ValidationIssue("General", e))) { }
    public ValidationException(IEnumerable<ValidationIssue> issues) : this(issues.ToArray()) { }
    private ValidationException(ValidationIssue[] issues) : base(string.Join(Environment.NewLine, issues.Select(i => (string.IsNullOrWhiteSpace(i.Subject) ? "" : i.Subject + ": ") + i.Message))) => Issues = issues;
}

public static class WorkspaceValidator
{
    private static readonly HashSet<string> ScriptExtensions = new(StringComparer.OrdinalIgnoreCase)
    { ".exe", ".com", ".bat", ".cmd", ".ps1", ".vbs", ".vbe", ".js", ".jse", ".wsf", ".wsh", ".msi", ".msp", ".scr", ".cpl", ".hta", ".lnk", ".url", ".reg", ".pif", ".scf", ".sct", ".inf", ".chm", ".application", ".appref-ms", ".diagcab", ".settingcontent-ms" };
    private static readonly HashSet<string> ShellApps = new(StringComparer.OrdinalIgnoreCase)
    { "cmd.exe", "powershell.exe", "pwsh.exe", "wscript.exe", "cscript.exe", "mshta.exe", "rundll32.exe", "regsvr32.exe" };
    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    { "CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" };

    public static void Validate(Workspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        var errors = new List<ValidationIssue>();
        void Add(string code, string message, string? subject = null) => errors.Add(new(code, message, subject));
        if (workspace.Id == Guid.Empty)
            Add("WorkspaceId", "Workspace ID is required.");
        if (!ValidName(workspace.Name))
            Add("WorkspaceName", "Workspace name must contain 1–100 characters without control characters.");
        if (workspace.Color is null || !System.Text.RegularExpressions.Regex.IsMatch(workspace.Color, "^#[0-9A-Fa-f]{6}$"))
            Add("Color", "Accent color must be #RRGGBB.");
        if (workspace.Description is null || workspace.Description.Length > 2000)
            Add("Description", "Description must not exceed 2000 characters.");
        if (workspace.Tags is null || workspace.Tags.Count > 20 || workspace.Tags.Any(t => string.IsNullOrWhiteSpace(t) || t.Length > 40 || t.Any(char.IsControl)))
            Add("Tags", "Use up to 20 tags, each 1–40 characters.");
        if (workspace.Items is null)
            Add("Items", "Launch item collection is required.");
        else
        {
            if (workspace.Items.Count > 100)
                Add("Items", "A workspace supports up to 100 launch items.");
            var nonNull = workspace.Items.Where(i => i is not null).ToArray();
            if (nonNull.Length != workspace.Items.Count)
                Add("ItemNull", "Launch items must not be null.");
            if (nonNull.Select(i => i.Id).Distinct().Count() != nonNull.Length)
                Add("DuplicateItems", "Duplicate item IDs.");
            foreach (var item in nonNull)
            {
                var subject = item.Name;
                if (item.Id == Guid.Empty || !ValidName(item.Name))
                    Add("ItemName", "Item needs a name (1–100 characters) and ID.", subject);
                if (!Enum.IsDefined(item.Kind) || !Enum.IsDefined(item.Shell))
                    Add("ItemType", "Unknown item type or shell.", subject);
                if (item.DelayMs is < 0 or > 300000)
                    Add("Delay", "Delay must be between 0 and 300000 ms.", subject);
                if (item.Order < 0)
                    Add("Order", "Order cannot be negative.", subject);
                if (item.Target is null || string.IsNullOrWhiteSpace(item.Target) || item.Target.Length > 8192 || item.Target.Contains('\0'))
                    Add("Target", "Target is empty or invalid.", subject);
                if (item.Arguments is null || item.Arguments.Length > 8192 || item.Arguments.Contains('\0') || item.Note is null || item.Note.Length > 2000 || item.WorkingDirectory is null || item.WorkingDirectory.Length > 8192 || item.WorkingDirectory.Contains('\0') || item.Icon is null || item.Icon.Length > 100)
                    Add("Arguments", "Arguments, note, icon or working directory are invalid.", subject);
                if (item.Target is null || item.Arguments is null || item.WorkingDirectory is null)
                    continue;
                if (item.Kind == ItemKind.Url && (!Uri.TryCreate(item.Target, UriKind.Absolute, out var uri) || (uri.Scheme != "https" && uri.Scheme != "http") || string.IsNullOrEmpty(uri.Host) || item.Target.Any(char.IsControl)))
                    Add("Url", "Only absolute HTTP/HTTPS URLs without control characters are allowed.", subject);
                var expandedTarget = item.Kind is ItemKind.App or ItemKind.File or ItemKind.Folder ? Environment.ExpandEnvironmentVariables(item.Target) : item.Target;
                var extension = Path.GetExtension(expandedTarget.TrimEnd(' ', '.'));
                var fileName = expandedTarget.Replace('\\', '/').Split('/').Last();
                if (item.Kind == ItemKind.App && (extension.ToLowerInvariant() is not (".exe" or ".lnk") || ShellApps.Contains(fileName)))
                    Add("AppType", "Use an .exe/.lnk application; shell interpreters must use Command.", subject);
                if (item.Kind == ItemKind.File && ScriptExtensions.Contains(extension))
                    Add("FileType", "Executable files require App or Command type.", subject);
                if (!string.IsNullOrWhiteSpace(item.WorkingDirectory) && !IsLocalWindowsPath(Environment.ExpandEnvironmentVariables(item.WorkingDirectory)))
                    Add("WorkingDirectory", "Working directory must be an absolute local Windows folder.", subject);
                if (item.Kind != ItemKind.App && item.Arguments.Length != 0)
                    Add("AppArguments", "Arguments are supported only for App; write the complete command for Command items.", subject);
                if (item.Kind is ItemKind.App or ItemKind.File or ItemKind.Folder && !IsLocalWindowsPath(expandedTarget))
                    Add("LocalPath", "Use an absolute local Windows path without reserved names, network paths or alternate streams.", subject);
            }
        }
        if (errors.Count > 0)
            throw new ValidationException(errors);
    }
    private static bool ValidName(string? name) => !string.IsNullOrWhiteSpace(name) && name.Length <= 100 && !name.Any(char.IsControl);
    private static bool IsLocalWindowsPath(string target)
    {
        if (target.Length < 3 || !char.IsAsciiLetter(target[0]) || target[1] != ':' || (target[2] != '\\' && target[2] != '/'))
            return false;
        if (target[2..].IndexOfAny([':', '*', '?', '"', '<', '>', '|']) >= 0 || target.Any(char.IsControl))
            return false;
        return target[3..].Split(['\\', '/']).All(part => part.Length == 0 ||
            (part != "." && part != ".." && !part.EndsWith(' ') && !part.EndsWith('.') && !ReservedNames.Contains(part.Split('.')[0])));
    }
}
