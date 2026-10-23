using System.ComponentModel;
using System.Runtime.InteropServices;
using Forms = System.Windows.Forms;
namespace Shift.App.Services;

public sealed class WindowCommands : Forms.NativeWindow, IDisposable
{
    private const int HotkeyId = 0x5348;
    private readonly Action quickLaunch;
    private readonly Action settingsChanged;
    private bool registered;
    public bool Compact
    {
        get; set;
    }
    public WindowCommands(nint handle, Action quickLaunch, Action settingsChanged)
    {
        this.quickLaunch = quickLaunch;
        this.settingsChanged = settingsChanged;
        AssignHandle(handle);
    }
    public void ConfigureHotkey(bool enabled)
    {
        if (enabled == registered)
            return;
        if (enabled)
        {
            if (!RegisterHotKey(Handle, HotkeyId, 0x4003, 0x20))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Ctrl+Alt+Space is already in use. Choose another application shortcut or disable the SHIFT global shortcut.");
            registered = true;
        }
        else
        {
            UnregisterHotKey(Handle, HotkeyId);
            registered = false;
        }
    }
    protected override void WndProc(ref Forms.Message message)
    {
        if (message.Msg == 0x0312 && message.WParam == HotkeyId)
        {
            quickLaunch();
            message.Result = 0;
            return;
        }
        base.WndProc(ref message);
        // Desktop HWND notifications work without the UWP CoreWindow required by
        // AccessibilitySettings.HighContrastChanged (0x80070490 in unpackaged WinUI).
        if (message.Msg is 0x001A or 0x031A)
            settingsChanged();
        if (message.Msg == 0x0024)
        {
            var info = Marshal.PtrToStructure<MinMaxInfo>(message.LParam);
            var scale = Math.Max(1, GetDpiForWindow(Handle) / 96.0);
            var width = (int)((Compact ? 520 : 840) * scale);
            var height = (int)((Compact ? 440 : 580) * scale);
            info.MinTrackSize = new Point(Math.Min(width, info.MaxTrackSize.X > 0 ? info.MaxTrackSize.X : width), Math.Min(height, info.MaxTrackSize.Y > 0 ? info.MaxTrackSize.Y : height));
            Marshal.StructureToPtr(info, message.LParam, false);
        }
    }
    public void Dispose()
    {
        if (registered)
            UnregisterHotKey(Handle, HotkeyId);
        registered = false;
        ReleaseHandle();
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct Point(int x, int y)
    {
        public int X = x; public int Y = y;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public Point Reserved, MaxSize, MaxPosition, MinTrackSize, MaxTrackSize;
    }
    [DllImport("user32.dll", SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)] private static extern bool RegisterHotKey(nint window, int id, uint modifiers, uint key);
    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool UnregisterHotKey(nint window, int id);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(nint window);
}
