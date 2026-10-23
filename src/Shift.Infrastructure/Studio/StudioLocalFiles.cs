namespace Shift.Infrastructure;

/// <summary>Reject before traversing descendants: no UNC, mapped network drives, links or offline placeholders.</summary>
internal static class StudioLocalFiles
{
    internal static string ValidatePath(string path, bool requireImageExtension = true)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > 32760 || path.Any(char.IsControl))
            throw new ArgumentException("A bounded local image path is required.", nameof(path));
        if (path.StartsWith(@"\\", StringComparison.Ordinal) || path.StartsWith("//", StringComparison.Ordinal)
            || path.StartsWith(@"\??\", StringComparison.Ordinal) || Uri.TryCreate(path, UriKind.Absolute, out var uri) && !uri.IsFile)
            throw new NotSupportedException("Network, device and URL image paths are not supported.");
        if (!Path.IsPathFullyQualified(path)) throw new ArgumentException("Choose an absolute local image path.", nameof(path));
        var full = Path.GetFullPath(path);
        if (OperatingSystem.IsWindows())
        {
            if (full.AsSpan(2).Contains(':')) throw new NotSupportedException("Alternate data streams are not supported.");
            if (new DriveInfo(Path.GetPathRoot(full)!).DriveType == DriveType.Network)
                throw new NotSupportedException("Network drives are not supported.");
        }
        if (requireImageExtension && !new[] { ".png", ".jpg", ".jpeg", ".bmp" }.Contains(Path.GetExtension(full), StringComparer.OrdinalIgnoreCase))
            throw new NotSupportedException("Only local PNG, JPEG and BMP images are supported.");
        var ancestors = new Stack<string>();
        for (var directory = Path.GetDirectoryName(full); !string.IsNullOrEmpty(directory); directory = Path.GetDirectoryName(directory)) ancestors.Push(directory);
        foreach (var directory in ancestors) RejectLinked(directory);
        var attributes = RejectLinked(full);
        if (attributes.HasFlag(FileAttributes.Directory)) throw new IOException("An image must be a regular file.");
        return full;
    }
    private static FileAttributes RejectLinked(string path)
    {
        var attributes = File.GetAttributes(path);
        if ((attributes & (FileAttributes.ReparsePoint | FileAttributes.Offline)) != 0)
            throw new NotSupportedException("Linked and offline image paths are not supported.");
        return attributes;
    }
}
