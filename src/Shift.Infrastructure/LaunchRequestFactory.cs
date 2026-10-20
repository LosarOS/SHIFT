using System.Diagnostics;
using Shift.Domain;
namespace Shift.Infrastructure;

/// <summary>Builds explicit Windows requests without starting a process; independently testable.</summary>
public static class LaunchRequestFactory
{
    public static ProcessStartInfo Create(WorkspaceItem item, string windowsDirectory,
        Func<string, bool>? fileExists = null, Func<string, bool>? directoryExists = null)
    {
        WorkspaceValidator.Validate(new()
        {
            Name = "Launch validation",
            Items = [item]
        });
        fileExists ??= File.Exists;
        directoryExists ??= Directory.Exists;
        ProcessStartInfo start;
        if (item.Kind == ItemKind.Command)
        {
            var executable = item.Shell == CommandShell.PowerShell
                ? Path.Combine(windowsDirectory, "System32", "WindowsPowerShell", "v1.0", "powershell.exe")
                : Path.Combine(windowsDirectory, "System32", "cmd.exe");
            start = new(executable)
            {
                UseShellExecute = false,
                CreateNoWindow = false
            };
            if (item.Shell == CommandShell.PowerShell)
                foreach (var argument in new[] { "-NoLogo", "-NoProfile", "-Command", item.Target })
                    start.ArgumentList.Add(argument);
            else
                start.Arguments = "/d /s /c \"" + item.Target + "\"";
        }
        else
        {
            // Never expand environment variables in URLs. Canonical URI escaping prevents raw
            // quotes/control characters reaching a browser's registered shell command template.
            var target = item.Kind == ItemKind.Url ? new Uri(item.Target, UriKind.Absolute).AbsoluteUri : Environment.ExpandEnvironmentVariables(item.Target);
            if (item.Kind == ItemKind.Folder && !directoryExists(target))
                throw new DirectoryNotFoundException("Folder not found. Edit the item and check its local path.");
            if (item.Kind is ItemKind.File or ItemKind.App && !fileExists(target))
                throw new FileNotFoundException("File or application not found. Edit the item and check its local path.");
            start = new(target)
            {
                UseShellExecute = true
            };
            if (item.Kind == ItemKind.App)
                start.Arguments = item.Arguments;
        }
        if (!string.IsNullOrWhiteSpace(item.WorkingDirectory))
        {
            var directory = Environment.ExpandEnvironmentVariables(item.WorkingDirectory);
            if (!directoryExists(directory))
                throw new DirectoryNotFoundException("Working directory not found. Edit the item and check its local path.");
            start.WorkingDirectory = directory;
        }
        return start;
    }
}
