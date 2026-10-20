using System.Diagnostics;
using Microsoft.Win32;
using Forms = System.Windows.Forms;
namespace Shift.App.Services;

public sealed class DesktopIntegration : IDisposable
{
    private Forms.NotifyIcon? tray;
    public void CreateTray(Action restore, Action quit, string openText, string quitText)
    {
        if (tray is not null)
        {
            tray.Visible = false;
            tray.ContextMenuStrip?.Dispose();
            tray.Icon?.Dispose();
            tray.Dispose();
        }
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add(openText, null, (_, _) => restore());
        menu.Items.Add(quitText, null, (_, _) => quit());
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "shift.ico");
        tray = new Forms.NotifyIcon { Text = "SHIFT · Workspaces", Icon = File.Exists(iconPath) ? new System.Drawing.Icon(iconPath) : System.Drawing.SystemIcons.Application, Visible = true, ContextMenuStrip = menu };
        tray.DoubleClick += (_, _) => restore();
    }
    public void Notify(string title, string message)
    {
        if (tray is not null)
        {
            tray.BalloonTipTitle = title;
            tray.BalloonTipText = message;
            tray.ShowBalloonTip(4000);
        }
    }
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public bool IsStartupEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue("SHIFT") as string == StartupCommand;
    }
    private static string StartupCommand => "\"" + Environment.ProcessPath + "\"";
    public void SetStartup(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey) ?? throw new IOException("Cannot open startup settings.");
        if (enabled)
            key.SetValue("SHIFT", StartupCommand, RegistryValueKind.String);
        else
            key.DeleteValue("SHIFT", false);
    }
    public static void OpenFolder(string path)
    {
        Directory.CreateDirectory(path);
        using var process = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }
    public void Dispose()
    {
        if (tray is not null)
        {
            tray.Visible = false;
            tray.ContextMenuStrip?.Dispose();
            tray.Icon?.Dispose();
            tray.Dispose();
            tray = null;
        }
    }
}
