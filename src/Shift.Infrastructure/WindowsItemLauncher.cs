using System.Diagnostics;
using Shift.Application;
using Shift.Domain;
namespace Shift.Infrastructure;

public sealed class WindowsItemLauncher : IItemLauncher
{
    public Task LaunchAsync(WorkspaceItem item, CancellationToken token)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Launching requires Windows.");
        WorkspaceValidator.Validate(new Workspace { Name = "Launch validation", Items = [item] });
        token.ThrowIfCancellationRequested();
        var target = Environment.ExpandEnvironmentVariables(item.Target);
        ProcessStartInfo start;
        if (item.Kind == ItemKind.Command)
        {
            // Consent is enforced by LaunchCoordinator. Never invoke this adapter from the UI directly.
            var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            var shell = item.Shell == CommandShell.PowerShell ? Path.Combine(windows, "System32", "WindowsPowerShell", "v1.0", "powershell.exe") : Path.Combine(windows, "System32", "cmd.exe");
            start = new ProcessStartInfo(shell) { UseShellExecute = false, CreateNoWindow = false };
            if (item.Shell == CommandShell.PowerShell)
            {
                start.ArgumentList.Add("-NoLogo");
                start.ArgumentList.Add("-NoProfile");
                start.ArgumentList.Add("-Command");
                start.ArgumentList.Add(item.Target);
            }
            else
            {
                start.Arguments = "/d /s /c \"" + item.Target + "\"";
            }
        }
        else
        {
            if (item.Kind == ItemKind.Folder && !Directory.Exists(target))
                throw new DirectoryNotFoundException("Folder not found. Edit the item and choose an existing local folder.");
            if (item.Kind is ItemKind.File or ItemKind.App && !File.Exists(target))
                throw new FileNotFoundException("File or application not found. Edit the item and check its path.");
            // URLs use the user's registered browser; no shell command string is constructed.
            start = new ProcessStartInfo(target) { UseShellExecute = true };
            if (item.Kind == ItemKind.App)
                start.Arguments = item.Arguments;
        }
        if (!string.IsNullOrWhiteSpace(item.WorkingDirectory))
        {
            var directory = Environment.ExpandEnvironmentVariables(item.WorkingDirectory);
            if (!Path.IsPathFullyQualified(directory) || !Directory.Exists(directory))
                throw new DirectoryNotFoundException("Working directory must be an existing absolute local folder.");
            start.WorkingDirectory = directory;
        }
        token.ThrowIfCancellationRequested();
        // Do not cancel after dispatch: once started, success must be recorded accurately.
        using var process = Process.Start(start);
        return Task.CompletedTask;
    }
}
