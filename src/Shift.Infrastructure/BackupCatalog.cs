using Shift.Application;
namespace Shift.Infrastructure;

/// <summary>Bounded top-level discovery. Does not recurse into arbitrary directories or follow links.</summary>
public sealed class BackupCatalog : IBackupCatalog
{
    public const int MaximumEntries = 100;
    public Task<BackupInventory> ListAsync(string directory, CancellationToken token = default) => Task.Run(() =>
    {
        var entries = new List<BackupEntry>(); var unavailable = new List<string>();
        var truncated = false;
        var root = Path.GetFullPath(directory);
        if (Directory.Exists(root) && (File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
            return new BackupInventory([], false, [root]);
        foreach (var location in new[] { root, Path.Combine(root, "automatic"), Path.Combine(root, "migration-backups") })
        {
            token.ThrowIfCancellationRequested();
            try
            {
                if ((File.GetAttributes(location) & FileAttributes.ReparsePoint) != 0) { unavailable.Add(location); continue; }
                foreach (var path in Directory.EnumerateFiles(location, "*.db", SearchOption.TopDirectoryOnly))
                {
                    token.ThrowIfCancellationRequested();
                    if (entries.Count == MaximumEntries) { truncated = true; break; }
                    var file = new FileInfo(path);
                    if ((file.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                    entries.Add(new(file.FullName, file.Length, file.LastWriteTimeUtc));
                }
            }
            catch (DirectoryNotFoundException) { }
            catch (FileNotFoundException) { } // Missing optional directories/rotated files are absence, not access failures.
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { unavailable.Add(location); }
            if (truncated) break;
        }
        return new BackupInventory(entries.OrderByDescending(e => e.ModifiedAt).ThenBy(e => e.Path, StringComparer.Ordinal).ToArray(), truncated, unavailable);
    }, token);
    public Task<BackupInspection> InspectAsync(string path, CancellationToken token = default) =>
        Task.Run(() => SqliteStore.InspectBackupAsync(path, token), token);
}
