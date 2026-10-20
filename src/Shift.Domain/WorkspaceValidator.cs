namespace Shift.Domain;

public sealed class ValidationException(IEnumerable<string> errors) : Exception(string.Join(Environment.NewLine, errors));

public static class WorkspaceValidator
{
    private static readonly HashSet<string> ScriptExtensions = new(StringComparer.OrdinalIgnoreCase)
    { ".exe", ".com", ".bat", ".cmd", ".ps1", ".vbs", ".vbe", ".js", ".jse", ".wsf", ".wsh", ".msi", ".msp", ".scr", ".cpl", ".hta", ".lnk", ".url", ".reg", ".pif", ".scf", ".sct", ".inf", ".chm", ".application", ".appref-ms", ".diagcab", ".settingcontent-ms" };
    private static readonly HashSet<string> ShellApps = new(StringComparer.OrdinalIgnoreCase)
    { "cmd.exe", "powershell.exe", "pwsh.exe", "wscript.exe", "cscript.exe", "mshta.exe", "rundll32.exe", "regsvr32.exe" };

    public static void Validate(Workspace workspace)
    {
        var errors = new List<string>();
        if (workspace.Id == Guid.Empty)
            errors.Add("Workspace ID is required.");
        if (string.IsNullOrWhiteSpace(workspace.Name) || workspace.Name.Length > 100)
            errors.Add("Workspace name must contain 1–100 characters.");
        if (!System.Text.RegularExpressions.Regex.IsMatch(workspace.Color, "^#[0-9A-Fa-f]{6}$"))
            errors.Add("Accent color must be #RRGGBB.");
        if (workspace.Description.Length > 2000)
            errors.Add("Description exceeds 2000 characters.");
        if (workspace.Tags.Count > 20 || workspace.Tags.Any(t => string.IsNullOrWhiteSpace(t) || t.Length > 40))
            errors.Add("Use up to 20 tags, each 1–40 characters.");
        if (workspace.Items.Count > 100)
            errors.Add("A workspace supports up to 100 launch items.");
        if (workspace.Items.Select(i => i.Id).Distinct().Count() != workspace.Items.Count)
            errors.Add("Duplicate item IDs.");
        foreach (var item in workspace.Items)
        {
            var prefix = $"{item.Name}: ";
            if (item.Id == Guid.Empty || string.IsNullOrWhiteSpace(item.Name) || item.Name.Length > 100)
                errors.Add(prefix + "Item needs a name (1–100 characters) and ID.");
            if (!Enum.IsDefined(item.Kind) || !Enum.IsDefined(item.Shell))
                errors.Add(prefix + "Unknown item type or shell.");
            if (item.DelayMs is < 0 or > 300000)
                errors.Add(prefix + "Delay must be between 0 and 300000 ms.");
            if (item.Order < 0)
                errors.Add(prefix + "Order cannot be negative.");
            if (string.IsNullOrWhiteSpace(item.Target) || item.Target.Length > 8192 || item.Target.Contains('\0'))
                errors.Add(prefix + "Target is empty or invalid.");
            if (item.Arguments.Length > 8192 || item.Arguments.Contains('\0') || item.Note.Length > 2000 || item.WorkingDirectory.Contains('\0'))
                errors.Add(prefix + "Arguments, note or working directory are invalid.");
            if (item.Kind == ItemKind.Url && (!Uri.TryCreate(item.Target, UriKind.Absolute, out var uri) || (uri.Scheme != "https" && uri.Scheme != "http") || string.IsNullOrEmpty(uri.Host)))
                errors.Add(prefix + "Only absolute HTTP/HTTPS URLs are allowed.");
            var expandedTarget = item.Kind is ItemKind.App or ItemKind.File or ItemKind.Folder
                ? Environment.ExpandEnvironmentVariables(item.Target) : item.Target;
            var extension = Path.GetExtension(expandedTarget.TrimEnd(' ', '.'));
            var fileName = expandedTarget.Replace('\\', '/').Split('/').Last();
            if (item.Kind == ItemKind.App && (extension.ToLowerInvariant() is not (".exe" or ".lnk") || ShellApps.Contains(fileName)))
                errors.Add(prefix + "Use an .exe/.lnk application; shell interpreters must use Command.");
            if (item.Kind == ItemKind.File && ScriptExtensions.Contains(extension))
                errors.Add(prefix + "Executable files require App or Command type.");
            if (!string.IsNullOrWhiteSpace(item.WorkingDirectory) && !IsLocalWindowsPath(Environment.ExpandEnvironmentVariables(item.WorkingDirectory)))
                errors.Add(prefix + "Working directory must be an absolute local Windows folder.");
            if (item.Kind != ItemKind.App && item.Arguments.Length != 0)
                errors.Add(prefix + "Arguments are supported only for App items; write the complete command for Command items.");
            if (item.Kind is ItemKind.App or ItemKind.File or ItemKind.Folder)
            {
                var target = Environment.ExpandEnvironmentVariables(item.Target);
                if (!IsLocalWindowsPath(target))
                    errors.Add(prefix + "Use an absolute local Windows path; relative, network, device and alternate stream paths are not supported.");
            }
        }
        if (errors.Count > 0)
            throw new ValidationException(errors);
    }

    private static bool IsLocalWindowsPath(string target)
    {
        if (target.Length < 3 || !char.IsAsciiLetter(target[0]) || target[1] != ':' || (target[2] != '\\' && target[2] != '/'))
            return false;
        if (target[2..].IndexOfAny([':', '*', '?', '"', '<', '>', '|', '\0']) >= 0)
            return false;
        // Reject Win32 path normalization ambiguities, including script.cmd<space> and ADS.
        return target[3..].Split(['\\', '/']).All(part => part.Length == 0 || (part != "." && part != ".." && !part.EndsWith(' ') && !part.EndsWith('.')));
    }
}
