using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Microsoft.Win32.SafeHandles;
using Shift.Application;
namespace Shift.Infrastructure;

/// <summary>Copies explicitly chosen local images; decoded through built-in GDI+ codecs, never a shell handler.</summary>
public sealed class WindowsStudioImageLibrary(string managedAssetsDirectory) : IStudioImageLibrary
{
    public const long MaximumFileBytes = 32L * 1024 * 1024;
    public const long MaximumDecodedPixels = 32_000_000;
    private readonly string directory = Path.GetFullPath(managedAssetsDirectory);

    public Task<string> ImportAsync(string sourcePath, CancellationToken token = default) => Task.Run(async () =>
    {
        RequireWindows(); token.ThrowIfCancellationRequested();
        var source = ResolveSource(sourcePath);
        EnsureManagedDirectory();
        var temporary = Path.Combine(directory, ".import-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            string digest;
            await using (var input = OpenLocal(source))
            {
                if (input.Length is <= 0 or > MaximumFileBytes) throw new InvalidDataException("Image must be between 1 byte and 32 MiB.");
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                await using var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous);
                var buffer = new byte[65536]; long total = 0;
                int count;
                while ((count = await input.ReadAsync(buffer, token).ConfigureAwait(false)) != 0)
                {
                    total += count;
                    if (total > MaximumFileBytes) throw new InvalidDataException("Image exceeds 32 MiB.");
                    hash.AppendData(buffer, 0, count);
                    await output.WriteAsync(buffer.AsMemory(0, count), token).ConfigureAwait(false);
                }
                await output.FlushAsync(token).ConfigureAwait(false);
                output.Flush(flushToDisk: true);
                digest = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
            }
            if (ManagedDigest(source) is { } expected && !string.Equals(digest, expected, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Managed image content differs from its content-addressed name.");
            token.ThrowIfCancellationRequested();
            var extension = Decode(temporary);
            var destination = Path.Combine(directory, digest + extension);
            token.ThrowIfCancellationRequested();
            if (!File.Exists(destination))
            {
                try { File.Move(temporary, destination); }
                catch (IOException) when (File.Exists(destination)) { /* Another explicit import won the same content-addressed name. */ }
            }
            using var existing = OpenLocal(StudioLocalFiles.ValidatePath(destination));
            if (!string.Equals(Convert.ToHexString(SHA256.HashData(existing)), digest, StringComparison.OrdinalIgnoreCase))
                throw new IOException("Managed image content differs from its content-addressed name.");
            return destination;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }, token);

    public Task ValidateAsync(string imagePath, CancellationToken token = default) => Task.Run(() =>
    {
        RequireWindows(); token.ThrowIfCancellationRequested();
        var full = ResolveSource(imagePath);
        using var locked = OpenLocal(full);
        if (locked.Length is <= 0 or > MaximumFileBytes) throw new InvalidDataException("Image must be between 1 byte and 32 MiB.");
        if (ManagedDigest(full) is { } expected && !string.Equals(Convert.ToHexString(SHA256.HashData(locked)), expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Managed image content differs from its content-addressed name.");
        _ = Decode(full);
        token.ThrowIfCancellationRequested();
    }, token);

    private string ResolveSource(string path)
    {
        try { return StudioLocalFiles.ValidatePath(path); }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            // A portable directory may have moved. Only our exact content-addressed basename is eligible.
            // The caller verifies its actual SHA256 while holding the opened file before using it.
            var name = Path.GetFileName(path);
            if (!System.Text.RegularExpressions.Regex.IsMatch(name, @"^[0-9a-f]{64}\.(png|jpg|bmp)$", System.Text.RegularExpressions.RegexOptions.CultureInvariant)) throw;
            return StudioLocalFiles.ValidatePath(Path.Combine(directory, name));
        }
    }
    private string? ManagedDigest(string path)
    {
        if (!string.Equals(Path.GetDirectoryName(path), directory, StringComparison.OrdinalIgnoreCase)) return null;
        var name = Path.GetFileName(path);
        return System.Text.RegularExpressions.Regex.IsMatch(name, @"^[0-9a-f]{64}\.(png|jpg|bmp)$", System.Text.RegularExpressions.RegexOptions.CultureInvariant) ? name[..64] : null;
    }
    private void EnsureManagedDirectory()
    {
        if (directory.StartsWith(@"\\", StringComparison.Ordinal) || new DriveInfo(Path.GetPathRoot(directory)!).DriveType == DriveType.Network)
            throw new NotSupportedException("Managed image storage must be on a local drive.");
        var ancestors = new Stack<string>();
        for (var parent = directory; !string.IsNullOrEmpty(parent); parent = Path.GetDirectoryName(parent)) ancestors.Push(parent);
        foreach (var parent in ancestors)
        {
            if (!Directory.Exists(parent)) continue;
            if ((File.GetAttributes(parent) & (FileAttributes.ReparsePoint | FileAttributes.Offline)) != 0)
                throw new NotSupportedException("Managed images cannot be stored in linked or offline directories.");
        }
        Directory.CreateDirectory(directory);
    }
    private static FileStream OpenLocal(string path)
    {
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.SequentialScan);
        try
        {
            // Guard a link replacement between path checks and opening the actual file.
            var resolved = new System.Text.StringBuilder(32768);
            var length = GetFinalPathNameByHandle(stream.SafeFileHandle, resolved, (uint)resolved.Capacity, 0);
            if (length == 0 || length >= resolved.Capacity) throw new IOException("Cannot verify the opened image path.");
            var actual = resolved.ToString();
            if (actual.StartsWith(@"\\?\", StringComparison.Ordinal)) actual = actual[4..];
            if (!string.Equals(actual, Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase))
                throw new NotSupportedException("Image path changed or resolves through a link.");
            return stream;
        }
        catch { stream.Dispose(); throw; }
    }
    private static void RequireWindows()
    { if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Studio image decoding requires Windows GDI+."); }

    private static string Decode(string path)
    {
        var startup = new GdiplusStartupInput { Version = 1, SuppressExternalCodecs = true };
        Check(GdiplusStartup(out var token, ref startup, 0));
        nint image = 0, bitmap = 0, graphics = 0;
        try
        {
            Check(GdipLoadImageFromFile(path, out image));
            Check(GdipGetImageWidth(image, out var width)); Check(GdipGetImageHeight(image, out var height));
            if (width == 0 || height == 0 || width > 16384 || height > 16384 || (long)width * height > MaximumDecodedPixels)
                throw new InvalidDataException("Decoded image exceeds the 32-million-pixel limit or has invalid dimensions.");
            Check(GdipGetImageRawFormat(image, out var format));
            var extension = format == new Guid("b96b3caf-0728-11d3-9d7b-0000f81ef32e") ? ".png"
                : format == new Guid("b96b3cae-0728-11d3-9d7b-0000f81ef32e") ? ".jpg"
                : format == new Guid("b96b3cab-0728-11d3-9d7b-0000f81ef32e") ? ".bmp"
                : throw new InvalidDataException("Only decoded PNG, JPEG and BMP images are supported.");
            // Draw all pixels, rather than accepting a header-only image or a successful metadata probe.
            Check(GdipCreateBitmapFromScan0((int)width, (int)height, 0, 0x26200A, 0, out bitmap));
            Check(GdipGetImageGraphicsContext(bitmap, out graphics));
            Check(GdipDrawImageRectI(graphics, image, 0, 0, (int)width, (int)height));
            return extension;
        }
        finally
        {
            if (graphics != 0) _ = GdipDeleteGraphics(graphics);
            if (bitmap != 0) _ = GdipDisposeImage(bitmap);
            if (image != 0) _ = GdipDisposeImage(image);
            GdiplusShutdown(token);
        }
    }
    private static void Check(int status)
    { if (status != 0) throw new InvalidDataException($"Windows could not fully decode the image (GDI+ status {status})."); }
    [StructLayout(LayoutKind.Sequential)] private struct GdiplusStartupInput
    { public uint Version; public nint DebugCallback; [MarshalAs(UnmanagedType.Bool)] public bool SuppressBackgroundThread; [MarshalAs(UnmanagedType.Bool)] public bool SuppressExternalCodecs; }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "GetFinalPathNameByHandleW")]
    private static extern uint GetFinalPathNameByHandle(SafeFileHandle file, System.Text.StringBuilder path, uint length, uint flags);
    [DllImport("gdiplus.dll", ExactSpelling = true)] private static extern int GdiplusStartup(out nuint token, ref GdiplusStartupInput input, nint output);
    [DllImport("gdiplus.dll", ExactSpelling = true)] private static extern void GdiplusShutdown(nuint token);
    [DllImport("gdiplus.dll", CharSet = CharSet.Unicode, ExactSpelling = true)] private static extern int GdipLoadImageFromFile(string path, out nint image);
    [DllImport("gdiplus.dll", ExactSpelling = true)] private static extern int GdipGetImageWidth(nint image, out uint width);
    [DllImport("gdiplus.dll", ExactSpelling = true)] private static extern int GdipGetImageHeight(nint image, out uint height);
    [DllImport("gdiplus.dll", ExactSpelling = true)] private static extern int GdipGetImageRawFormat(nint image, out Guid format);
    [DllImport("gdiplus.dll", ExactSpelling = true)] private static extern int GdipCreateBitmapFromScan0(int width, int height, int stride, int format, nint scan0, out nint bitmap);
    [DllImport("gdiplus.dll", ExactSpelling = true)] private static extern int GdipGetImageGraphicsContext(nint image, out nint graphics);
    [DllImport("gdiplus.dll", ExactSpelling = true)] private static extern int GdipDrawImageRectI(nint graphics, nint image, int x, int y, int width, int height);
    [DllImport("gdiplus.dll", ExactSpelling = true)] private static extern int GdipDeleteGraphics(nint graphics);
    [DllImport("gdiplus.dll", ExactSpelling = true)] private static extern int GdipDisposeImage(nint image);
}
