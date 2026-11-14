namespace Shift.Application;

public sealed record BackupEntry(string Path, long SizeBytes, DateTimeOffset ModifiedAt);
public sealed record BackupInventory(IReadOnlyList<BackupEntry> Entries, bool Truncated, IReadOnlyList<string> UnavailableDirectories);
public sealed record BackupInspection(string Path, int SchemaVersion, long Workspaces, long LaunchRuns, long StudioProfiles,
    long WidgetDefinitions, bool ExternalStudioResourcesIncluded = false);

/// <summary>Discovery and private-snapshot validation only; never replaces live user data.</summary>
public interface IBackupCatalog
{
    Task<BackupInventory> ListAsync(string directory, CancellationToken token = default);
    Task<BackupInspection> InspectAsync(string path, CancellationToken token = default);
}
