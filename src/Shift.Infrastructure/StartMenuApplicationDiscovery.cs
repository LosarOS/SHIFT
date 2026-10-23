using System.Security.Cryptography;
using System.Text;
using Shift.Application;
namespace Shift.Infrastructure;

public sealed record ApplicationDiscoveryRoot(string Path, ApplicationSource Source);

/// <summary>Bounded, read-only shortcut discovery. Does not resolve links or inspect running processes.</summary>
public sealed class StartMenuApplicationDiscovery : IApplicationDiscovery
{
    public const int MaximumCandidates = 500;
    public const int MaximumEntries = 5000;
    public const int MaximumDirectories = 256;
    public const int MaximumDepth = 8;
    private readonly ApplicationDiscoveryRoot[] roots;
    private readonly bool rootLimitReached;
    public StartMenuApplicationDiscovery(IEnumerable<ApplicationDiscoveryRoot>? roots = null)
    {
        var bounded = (roots ?? DefaultRoots()).Take(9).ToArray();
        this.roots = bounded.Take(8).ToArray();
        rootLimitReached = bounded.Length > 8;
    }

    private static IEnumerable<ApplicationDiscoveryRoot> DefaultRoots()
    {
        yield return new(Environment.GetFolderPath(Environment.SpecialFolder.Programs), ApplicationSource.UserStartMenu);
        yield return new(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), ApplicationSource.SharedStartMenu);
        yield return new(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), ApplicationSource.Desktop);
    }
    public Task<ApplicationDiscovery> DiscoverAsync(CancellationToken token = default) => Task.Run(() => Discover(token), token);

    private ApplicationDiscovery Discover(CancellationToken token)
    {
        var candidates = new List<ApplicationCandidate>();
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int entries = 0, directories = 0, unavailable = 0, invalid = 0;
        bool limited = rootLimitReached;
        var header = new byte[76];
        foreach (var root in roots)
        {
            token.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(root.Path)) continue;
            string rootPath;
            try
            {
                if (!Path.IsPathFullyQualified(root.Path)) { unavailable++; continue; }
                rootPath = Path.GetFullPath(root.Path);
                if (Path.TrimEndingDirectorySeparator(rootPath) == Path.TrimEndingDirectorySeparator(Path.GetPathRoot(rootPath)!)) { unavailable++; continue; }
                if (!IsLocalUnlinked(rootPath, token)) { unavailable++; continue; }
            }
            catch (Exception ex) when (IsFileFailure(ex)) { unavailable++; continue; }
            var pending = new Stack<(string Path, int Depth)>();
            pending.Push((rootPath, 0));
            while (pending.TryPop(out var current))
            {
                token.ThrowIfCancellationRequested();
                if (directories >= MaximumDirectories || entries >= MaximumEntries || candidates.Count >= MaximumCandidates) { limited = true; break; }
                directories++;
                try
                {
                    // Recheck ancestry before enumeration: a folder could have been changed since discovery.
                    if (!IsLocalUnlinked(current.Path, token)) { unavailable++; continue; }
                    foreach (var entry in Directory.EnumerateFileSystemEntries(current.Path))
                    {
                        token.ThrowIfCancellationRequested();
                        if (++entries > MaximumEntries || candidates.Count >= MaximumCandidates) { limited = true; break; }
                        try
                        {
                            var attributes = File.GetAttributes(entry);
                            if ((attributes & (FileAttributes.ReparsePoint | FileAttributes.Offline)) != 0) { unavailable++; continue; }
                            if (attributes.HasFlag(FileAttributes.Directory))
                            {
                                if (current.Depth >= MaximumDepth || pending.Count + directories >= MaximumDirectories) limited = true;
                                else pending.Push((entry, current.Depth + 1));
                                continue;
                            }
                            if (!Path.GetExtension(entry).Equals(".lnk", StringComparison.OrdinalIgnoreCase) || !paths.Add(entry)) continue;
                            if (!IsLocalUnlinked(entry, token)) { unavailable++; continue; }
                            using var stream = new FileStream(entry, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                            if (stream.ReadAtLeast(header, 76, throwOnEndOfStream: false) != 76 || !LocalItemInspection.IsShellLinkHeader(header)) { invalid++; continue; }
                            var name = Path.GetFileNameWithoutExtension(entry).Trim();
                            if (name.Length == 0 || name.Any(char.IsControl)) { invalid++; continue; }
                            if (name.Length > 100) name = name[..(char.IsHighSurrogate(name[99]) ? 99 : 100)];
                            var group = Path.GetRelativePath(rootPath, Path.GetDirectoryName(entry)!);
                            var id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(entry.ToUpperInvariant())));
                            candidates.Add(new(id, name, entry, root.Source, group == "." ? "" : group));
                        }
                        catch (Exception ex) when (IsFileFailure(ex)) { unavailable++; }
                    }
                }
                catch (Exception ex) when (IsFileFailure(ex)) { unavailable++; }
                if (limited && (entries >= MaximumEntries || candidates.Count >= MaximumCandidates)) break;
            }
            if (limited && (directories >= MaximumDirectories || entries >= MaximumEntries || candidates.Count >= MaximumCandidates)) break;
        }
        token.ThrowIfCancellationRequested();
        return new(candidates.OrderBy(c => c.Source).ThenBy(c => c.Group, StringComparer.CurrentCultureIgnoreCase).ThenBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase).ToArray(), unavailable, invalid, limited);
    }
    private static bool IsFileFailure(Exception ex) => ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException or NotSupportedException;

    private static bool IsLocalUnlinked(string path, CancellationToken token)
    {
        if (OperatingSystem.IsWindows() && (path.StartsWith("\\\\", StringComparison.Ordinal) || new DriveInfo(Path.GetPathRoot(path)!).DriveType == DriveType.Network)) return false;
        var parents = new Stack<string>();
        for (string? current = path; !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current)) parents.Push(current);
        foreach (var parent in parents)
        {
            token.ThrowIfCancellationRequested();
            if ((File.GetAttributes(parent) & (FileAttributes.ReparsePoint | FileAttributes.Offline)) != 0) return false;
        }
        return true;
    }
}
