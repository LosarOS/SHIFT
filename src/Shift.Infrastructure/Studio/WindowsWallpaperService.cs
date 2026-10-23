using System.Runtime.InteropServices;
using Shift.Application;
using Shift.Domain;
namespace Shift.Infrastructure;

/// <summary>Supported IDesktopWallpaper API only. Each operation owns an STA and its COM object.</summary>
public sealed class WindowsWallpaperService : IWallpaperService
{
    public Task<WallpaperSnapshot> CaptureAsync(CancellationToken token = default) => DesktopWallpaperInterop.RunAsync(wallpaper =>
    {
        wallpaper.GetStatus(out var status);
        wallpaper.GetPosition(out var position);
        if (!Enum.IsDefined((WallpaperPlacement)position))
            throw new IOException("Windows returned an unsupported wallpaper position.");
        var originals = new List<WallpaperAssignment>();
        wallpaper.GetMonitorDevicePathCount(out var count);
        if (count is 0 or > 64) throw new IOException("Windows returned an invalid display count.");
        string? limitation = status == 1 ? null : "Studio cannot safely restore slideshow, disabled, or remote-session wallpaper state.";
        for (uint index = 0; index < count; index++)
        {
            wallpaper.GetMonitorDevicePathAt(index, out var monitor);
            wallpaper.GetWallpaper(monitor, out var image);
            originals.Add(new(monitor, image ?? ""));
            if (string.IsNullOrWhiteSpace(image)) limitation ??= "Restoring an empty/solid-color wallpaper is not guaranteed by IDesktopWallpaper.SetWallpaper.";
            else
            {
                try
                {
                    var local = StudioLocalFiles.ValidatePath(image, requireImageExtension: false);
                    using var readable = new FileStream(local, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    if (readable.Length == 0) limitation ??= "An original wallpaper file is empty.";
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
                { limitation ??= "An original wallpaper is missing or not an accessible local regular file."; }
            }
        }
        return new WallpaperSnapshot(originals, (WallpaperPlacement)position, limitation is null, (status & 2) != 0,
            limitation ?? "Only static image values and position are captured. Windows Spotlight/provider configuration is not detectable or restorable through this API.");
    }, token);

    public Task SetWallpaperAsync(WallpaperAssignment assignment, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(assignment);
        token.ThrowIfCancellationRequested();
        // No empty monitor shorthand: callers enumerate explicit monitors and journal each action.
        if (string.IsNullOrWhiteSpace(assignment.MonitorId) || assignment.MonitorId.Length > 2048 || assignment.MonitorId.Any(char.IsControl))
            throw new ArgumentException("A Windows monitor device path is required.", nameof(assignment));
        StudioLocalFiles.ValidatePath(assignment.ImagePath, requireImageExtension: false);
        return DesktopWallpaperInterop.RunAsync(wallpaper =>
        {
            wallpaper.GetMonitorDevicePathCount(out var count);
            var found = false;
            for (uint index = 0; index < count && index < 64; index++)
            { wallpaper.GetMonitorDevicePathAt(index, out var id); found |= string.Equals(id, assignment.MonitorId, StringComparison.OrdinalIgnoreCase); }
            if (!found) throw new IOException("The selected display is no longer connected.");
            wallpaper.SetWallpaper(assignment.MonitorId, assignment.ImagePath);
            return true;
        }, token);
    }
    public Task SetPositionAsync(WallpaperPlacement position, CancellationToken token = default)
    {
        if (!Enum.IsDefined(position)) throw new ArgumentOutOfRangeException(nameof(position));
        return DesktopWallpaperInterop.RunAsync(wallpaper => { wallpaper.SetPosition((uint)position); return true; }, token);
    }
}

internal static class DesktopWallpaperInterop
{
    internal static Task<T> RunAsync<T>(Func<IDesktopWallpaper, T> action, CancellationToken token)
    {
        if (!OperatingSystem.IsWindows()) return Task.FromException<T>(new PlatformNotSupportedException("Desktop wallpaper requires Windows."));
        if (token.IsCancellationRequested) return Task.FromCanceled<T>(token);
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            if (!OperatingSystem.IsWindows()) { completion.TrySetException(new PlatformNotSupportedException("Desktop wallpaper requires Windows.")); return; }
            IDesktopWallpaper? wallpaper = null;
            var initialized = false;
            T? result = default;
            Exception? failure = null;
            try
            {
                token.ThrowIfCancellationRequested();
                Marshal.ThrowExceptionForHR(CoInitializeEx(0, 2)); initialized = true;
                wallpaper = (IDesktopWallpaper)Activator.CreateInstance(Type.GetTypeFromCLSID(new("C2CF3110-460E-4FC1-B9D0-8A1C0C9CC4BD"), true)!)!;
                // Cancellation is observed before mutation, never after a successful non-cancellable OS call.
                token.ThrowIfCancellationRequested();
                result = action(wallpaper);
            }
            catch (Exception ex) { failure = ex; }
            finally
            {
                try { if (wallpaper is not null) Marshal.FinalReleaseComObject(wallpaper); }
                catch (Exception ex) { failure ??= ex; }
                finally { if (initialized) CoUninitialize(); }
            }
            if (failure is OperationCanceledException) completion.TrySetCanceled(token);
            else if (failure is not null) completion.TrySetException(failure);
            else completion.TrySetResult(result!);
        }) { IsBackground = true, Name = "SHIFT wallpaper STA" };
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        return completion.Task;
    }
    [DllImport("ole32.dll")] private static extern int CoInitializeEx(nint reserved, uint apartment);
    [DllImport("ole32.dll")] private static extern void CoUninitialize();
    [StructLayout(LayoutKind.Sequential)] internal struct Rect { public int Left, Top, Right, Bottom; }
    // Full native vtable, in SDK order. HRESULT failures become COMException.
    [ComImport, Guid("B92B56A9-8B55-4E14-9A89-0199BBB6F93B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IDesktopWallpaper
    {
        void SetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string? monitorId, [MarshalAs(UnmanagedType.LPWStr)] string wallpaper);
        void GetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string? monitorId, [MarshalAs(UnmanagedType.LPWStr)] out string wallpaper);
        void GetMonitorDevicePathAt(uint index, [MarshalAs(UnmanagedType.LPWStr)] out string monitorId);
        void GetMonitorDevicePathCount(out uint count);
        void GetMonitorRECT([MarshalAs(UnmanagedType.LPWStr)] string monitorId, out Rect rect);
        void SetBackgroundColor(uint color);
        void GetBackgroundColor(out uint color);
        void SetPosition(uint position);
        void GetPosition(out uint position);
        void SetSlideshow(nint items);
        void GetSlideshow(out nint items);
        void SetSlideshowOptions(uint options, uint milliseconds);
        void GetSlideshowOptions(out uint options, out uint milliseconds);
        void AdvanceSlideshow([MarshalAs(UnmanagedType.LPWStr)] string? monitorId, uint direction);
        void GetStatus(out uint status);
        void Enable([MarshalAs(UnmanagedType.Bool)] bool enabled);
    }
}
