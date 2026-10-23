using Shift.Domain;
using Shift.Infrastructure;
using Xunit;
namespace Shift.Tests;

/// <summary>Windows executes real decoders/read-only probes. Other OSes verify explicit unsupported behavior.</summary>
public sealed class StudioNativeAdapterTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "SHIFT-native-" + Guid.NewGuid().ToString("N"));
    private string Assets => Path.Combine(root, "StudioAssets");
    public StudioNativeAdapterTests() => Directory.CreateDirectory(root);
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }

    [Theory]
    [InlineData("png", "iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAIAAAD91JpzAAAAFklEQVR4nGOsCDjBwMDAxMDAwMDAAAASmgGU1WtH8QAAAABJRU5ErkJggg==")]
    [InlineData("bmp", "Qk1GAAAAAAAAADYAAAAoAAAAAgAAAAIAAAABABgAAAAAABAAAADEDgAAxA4AAAAAAAAAAAAAyFB4yFB4AADIUHjIUHgAAA==")]
    [InlineData("jpg", "/9j/4AAQSkZJRgABAQAAAQABAAD/2wBDAAgGBgcGBQgHBwcJCQgKDBQNDAsLDBkSEw8UHRofHh0aHBwgJC4nICIsIxwcKDcpLDAxNDQ0Hyc5PTgyPC4zNDL/2wBDAQkJCQwLDBgNDRgyIRwhMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjL/wAARCAACAAIDASIAAhEBAxEB/8QAHwAAAQUBAQEBAQEAAAAAAAAAAAECAwQFBgcICQoL/8QAtRAAAgEDAwIEAwUFBAQAAAF9AQIDAAQRBRIhMUEGE1FhByJxFDKBkaEII0KxwRVS0fAkM2JyggkKFhcYGRolJicoKSo0NTY3ODk6Q0RFRkdISUpTVFVWV1hZWmNkZWZnaGlqc3R1dnd4eXqDhIWGh4iJipKTlJWWl5iZmqKjpKWmp6ipqrKztLW2t7i5usLDxMXGx8jJytLT1NXW19jZ2uHi4+Tl5ufo6erx8vP09fb3+Pn6/8QAHwEAAwEBAQEBAQEBAQAAAAAAAAECAwQFBgcICQoL/8QAtREAAgECBAQDBAcFBAQAAQJ3AAECAxEEBSExBhJBUQdhcRMiMoEIFEKRobHBCSMzUvAVYnLRChYkNOEl8RcYGRomJygpKjU2Nzg5OkNERUZHSElKU1RVVldYWVpjZGVmZ2hpanN0dXZ3eHl6goOEhYaHiImKkpOUlZaXmJmaoqOkpaanqKmqsrO0tba3uLm6wsPExcbHyMnK0tPU1dbX2Nna4uPk5ebn6Onq8vP09fb3+Pn6/9oADAMBAAIRAxEAPwDJooor686T/9k=")]
    public async Task Valid_image_is_copied_and_original_survives_or_platform_is_explicit(string extension, string base64)
    {
        var source = Path.Combine(root, "Обои — image." + extension);
        var bytes = Convert.FromBase64String(base64); await File.WriteAllBytesAsync(source, bytes);
        var library = new WindowsStudioImageLibrary(Assets);
        if (!OperatingSystem.IsWindows())
        {
            await Assert.ThrowsAsync<PlatformNotSupportedException>(() => library.ImportAsync(source));
            Assert.False(Directory.Exists(Assets)); return;
        }
        var copied = await library.ImportAsync(source);
        Assert.StartsWith(Assets + Path.DirectorySeparatorChar, copied);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(copied));
        Assert.Equal(bytes, await File.ReadAllBytesAsync(source));
        Assert.Equal(copied, await library.ImportAsync(source));
        Assert.Single(Directory.GetFiles(Assets));
        await library.ValidateAsync(copied);
        File.Delete(source);
        await library.ValidateAsync(copied); // Source disconnect/deletion cannot destroy the managed resource.
    }

    [Fact]
    public async Task Moved_portable_images_rebase_only_verified_managed_content()
    {
        const string bmp = "Qk1GAAAAAAAAADYAAAAoAAAAAgAAAAIAAAABABgAAAAAABAAAADEDgAAxA4AAAAAAAAAAAAAyFB4yFB4AADIUHjIUHgAAA==";
        var source = Path.Combine(root, "selected.bmp"); await File.WriteAllBytesAsync(source, Convert.FromBase64String(bmp));
        var library = new WindowsStudioImageLibrary(Assets);
        if (!OperatingSystem.IsWindows()) { await Assert.ThrowsAsync<PlatformNotSupportedException>(() => library.ImportAsync(source)); return; }
        var oldPath = await library.ImportAsync(source);
        var movedDirectory = Path.Combine(root, "moved Assets"); Directory.Move(Assets, movedDirectory);
        var moved = new WindowsStudioImageLibrary(movedDirectory);
        var current = Path.Combine(movedDirectory, Path.GetFileName(oldPath));
        await moved.ValidateAsync(oldPath);
        Assert.Equal(current, await moved.ImportAsync(oldPath));
        Assert.Single(Directory.GetFiles(movedDirectory));
        var altered = await File.ReadAllBytesAsync(current); altered[^1] ^= 1; await File.WriteAllBytesAsync(current, altered);
        await Assert.ThrowsAsync<InvalidDataException>(() => moved.ValidateAsync(oldPath));
        await Assert.ThrowsAsync<InvalidDataException>(() => moved.ImportAsync(oldPath));
        Assert.Single(Directory.GetFiles(movedDirectory)); Assert.True(File.Exists(source));
    }
    [Fact]
    public async Task Missing_arbitrary_filename_never_rebases_to_current_asset_directory()
    {
        var library = new WindowsStudioImageLibrary(Assets); var source = Path.Combine(root, "unrelated", "image.png");
        if (!OperatingSystem.IsWindows()) { await Assert.ThrowsAsync<PlatformNotSupportedException>(() => library.ValidateAsync(source)); return; }
        Directory.CreateDirectory(Assets); await File.WriteAllBytesAsync(Path.Combine(Assets, "image.png"), [1, 2, 3]);
        var error = await Assert.ThrowsAnyAsync<IOException>(() => library.ValidateAsync(source));
        Assert.True(error is FileNotFoundException or DirectoryNotFoundException);
    }

    [Fact]
    public async Task Cancelled_import_never_creates_managed_storage()
    {
        var library = new WindowsStudioImageLibrary(Assets); using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => library.ImportAsync(Path.Combine(root, "missing.png"), cancellation.Token));
        Assert.False(Directory.Exists(Assets));
    }
    [Fact]
    public async Task Damaged_image_is_not_imported_and_temporary_copy_is_removed()
    {
        var source = Path.Combine(root, "bad.png"); await File.WriteAllBytesAsync(source, [137, 80, 78, 71, 13, 10, 26, 10]);
        var library = new WindowsStudioImageLibrary(Assets);
        if (!OperatingSystem.IsWindows()) { await Assert.ThrowsAsync<PlatformNotSupportedException>(() => library.ImportAsync(source)); return; }
        await Assert.ThrowsAsync<InvalidDataException>(() => library.ImportAsync(source));
        Assert.Empty(Directory.GetFiles(Assets)); Assert.True(File.Exists(source));
    }
    [Fact]
    public async Task Oversized_source_is_rejected_before_copy_or_decode()
    {
        var source = Path.Combine(root, "large.bmp");
        using (var file = File.Create(source)) file.SetLength(WindowsStudioImageLibrary.MaximumFileBytes + 1);
        var library = new WindowsStudioImageLibrary(Assets);
        if (!OperatingSystem.IsWindows()) { await Assert.ThrowsAsync<PlatformNotSupportedException>(() => library.ImportAsync(source)); return; }
        await Assert.ThrowsAsync<InvalidDataException>(() => library.ImportAsync(source));
        Assert.Empty(Directory.GetFiles(Assets)); Assert.True(File.Exists(source));
    }
    [Theory]
    [InlineData("https://example.test/image.png")]
    [InlineData(@"\\server\share\image.png")]
    [InlineData(@"\\?\C:\Windows\image.png")]
    public async Task External_and_device_paths_are_rejected_without_storage_or_network(string path)
    {
        var library = new WindowsStudioImageLibrary(Assets);
        await Assert.ThrowsAnyAsync<NotSupportedException>(() => library.ImportAsync(path));
        Assert.False(Directory.Exists(Assets));
    }
    [Fact]
    public async Task Missing_image_is_an_error_without_creating_storage()
    {
        var library = new WindowsStudioImageLibrary(Assets);
        if (!OperatingSystem.IsWindows()) { await Assert.ThrowsAsync<PlatformNotSupportedException>(() => library.ImportAsync(Path.Combine(root, "gone.png"))); return; }
        await Assert.ThrowsAsync<FileNotFoundException>(() => library.ImportAsync(Path.Combine(root, "gone.png")));
        Assert.False(Directory.Exists(Assets));
    }
    [Fact]
    public async Task Capture_is_readonly_and_reports_restore_limitations()
    {
        var wallpaper = new WindowsWallpaperService();
        if (!OperatingSystem.IsWindows()) { await Assert.ThrowsAsync<PlatformNotSupportedException>(() => wallpaper.CaptureAsync()); return; }
        var before = await wallpaper.CaptureAsync(); var after = await wallpaper.CaptureAsync();
        Assert.NotEmpty(before.Wallpapers); Assert.Equal(before.Position, after.Position);
        Assert.Equal(before.Wallpapers, after.Wallpapers);
        if (!before.CanRestore) Assert.False(string.IsNullOrWhiteSpace(before.Limitation));
    }
    [Fact]
    public async Task Topology_reports_bounded_accessible_workareas_or_platform_error()
    {
        var topology = new WindowsMonitorTopology();
        if (!OperatingSystem.IsWindows()) { await Assert.ThrowsAsync<PlatformNotSupportedException>(() => topology.GetMonitorsAsync()); return; }
        var displays = await topology.GetMonitorsAsync(); Assert.InRange(displays.Count, 1, 64);
        Assert.All(displays, d => { Assert.NotEmpty(d.DeviceId); Assert.True(d.WorkWidth > 0 && d.WorkHeight > 0); Assert.True(double.IsFinite(d.DpiScale) && d.DpiScale > 0); });
    }
    [Fact]
    public void Metrics_do_not_fabricate_cpu_on_first_sample_or_when_unavailable()
    {
        var metrics = new WindowsSystemMetricsProvider(); var first = metrics.Sample(); Assert.Null(first.CpuPercent);
        if (!OperatingSystem.IsWindows()) { Assert.Null(first.TotalMemoryBytes); Assert.Null(first.AvailableMemoryBytes); return; }
        if (first.TotalMemoryBytes is { } total) { Assert.True(total > 0); Assert.InRange(first.AvailableMemoryBytes!.Value, 0UL, total); }
        var next = metrics.Sample(); if (next.CpuPercent is { } cpu) Assert.InRange(cpu, 0, 100);
    }
    [Fact]
    public async Task Invalid_wallpaper_monitor_and_position_never_reach_mutating_native_calls()
    {
        var wallpaper = new WindowsWallpaperService();
        await Assert.ThrowsAsync<ArgumentException>(() => wallpaper.SetWallpaperAsync(new("", Path.Combine(root, "absent.png"))));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => wallpaper.SetPositionAsync((WallpaperPlacement)999));
    }
}
