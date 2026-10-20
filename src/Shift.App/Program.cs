using System.Security.Cryptography;
using System.Text;
using Microsoft.UI.Dispatching;
namespace Shift.App;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();
        var paths = new AppPaths();
        var identity = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(paths.DataDirectory.ToUpperInvariant())))[..24];
        using var mutex = new Mutex(true, @"Local\SHIFT-" + identity, out var first);
        if (!first)
            return; // One writer and one launch queue per data directory.
        try
        {
            Microsoft.UI.Xaml.Application.Start(ignored =>
            {
                SynchronizationContext.SetSynchronizationContext(new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
                _ = new ShiftApplication(paths);
            });
        }
        finally { mutex.ReleaseMutex(); }
    }
}

public sealed class ShiftApplication : Microsoft.UI.Xaml.Application
{
    private MainWindow? window;
    private readonly AppPaths paths;
    public ShiftApplication(AppPaths paths)
    {
        this.paths = paths;
        Resources.MergedDictionaries.Add(new Microsoft.UI.Xaml.Controls.XamlControlsResources());
        UnhandledException += (_, e) =>
        {
            // Unknown dispatcher failures are fatal; do not continue with potentially inconsistent state.
            new FileLog(paths.LogDirectory).Error("Unhandled UI error", e.Exception);
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) => new FileLog(paths.LogDirectory).Error("Fatal error", e.ExceptionObject as Exception ?? new Exception("Unknown fatal error"));
    }
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        window = new MainWindow(paths);
        window.Activate();
    }
}
