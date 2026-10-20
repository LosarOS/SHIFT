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
        token.ThrowIfCancellationRequested();
        var start = LaunchRequestFactory.Create(item, Environment.GetFolderPath(Environment.SpecialFolder.Windows));
        token.ThrowIfCancellationRequested();
        // Once Windows accepts dispatch, record success even if cancellation races immediately after it.
        using var process = Process.Start(start);
        return Task.CompletedTask;
    }
}
