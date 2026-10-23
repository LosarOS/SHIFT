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
        // Opt-in diagnostics for disposable CI only; never log first-chance payloads.
        if (Environment.GetEnvironmentVariable("SHIFT_STARTUP_TRACE") == "1")
            AppDomain.CurrentDomain.FirstChanceException += (_, e) =>
            {
                if (e.Exception is System.Runtime.InteropServices.COMException)
                    new FileLog(paths.LogDirectory).Error("First-chance COM failure", e.Exception);
            };
        var identity = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(paths.DataDirectory.ToUpperInvariant())))[..24];
        using var mutex = new Mutex(true, @"Local\SHIFT-" + identity, out var first);
        if (!first)
        {
            SingleInstanceSignal.SendAsync("SHIFT-" + identity).GetAwaiter().GetResult();
            return;
        }
        try
        {
            Microsoft.UI.Xaml.Application.Start(ignored =>
            {
                SynchronizationContext.SetSynchronizationContext(new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
                _ = new ShiftApplication(paths, "SHIFT-" + identity);
            });
        }
        catch (Exception ex)
        {
            new FileLog(paths.LogDirectory).Error("Application initialization failed", ex);
            throw;
        }
        finally { mutex.ReleaseMutex(); }
    }
}

public sealed partial class ShiftApplication : Microsoft.UI.Xaml.Application
{
    private MainWindow? window;
    private readonly AppPaths paths;
    private readonly string signalName;
    private SingleInstanceSignal? signal;
    public ShiftApplication(AppPaths paths, string signalName)
    {
        this.paths = paths;
        this.signalName = signalName;
        UnhandledException += (_, e) =>
        {
            // Unknown dispatcher failures are fatal; do not continue with potentially inconsistent state.
            new FileLog(paths.LogDirectory).Error("Unhandled UI error", e.Exception);
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) => new FileLog(paths.LogDirectory).Error("Fatal error", e.ExceptionObject as Exception ?? new Exception("Unknown fatal error"));
        new FileLog(paths.LogDirectory).Info("Initializing WinUI resources.");
        InitializeComponent();
        new FileLog(paths.LogDirectory).Info("WinUI resources initialized.");
    }
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        new FileLog(paths.LogDirectory).Info("Creating main window.");
        window = new MainWindow(paths);
        new FileLog(paths.LogDirectory).Info("Main window created.");
        signal = new SingleInstanceSignal(signalName, () => window.DispatcherQueue.TryEnqueue(window.ShowAndActivate), new FileLog(paths.LogDirectory));
        window.Closed += async (_, _) => { if (signal is not null) await signal.DisposeAsync(); };
        new FileLog(paths.LogDirectory).Info("Activating main window.");
        window.Activate();
        new FileLog(paths.LogDirectory).Info("Main window activated.");
    }
}
