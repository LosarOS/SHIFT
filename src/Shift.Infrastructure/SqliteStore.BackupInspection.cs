using Microsoft.Data.Sqlite;
using Shift.Application;
namespace Shift.Infrastructure;

public sealed partial class SqliteStore
{
    /// <summary>Captures committed WAL pages into an owned snapshot, then uses the same validation as restore.</summary>
    public static async Task<BackupInspection> InspectBackupAsync(string path, CancellationToken token = default)
    {
        path = Path.GetFullPath(path);
        if (!File.Exists(path)) throw new FileNotFoundException("Backup file not found.");
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Choose a regular backup file, not a symbolic link.");
        var tempDirectory = Path.Combine(Path.GetTempPath(), "shift-backup-inspection-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
        var snapshot = Path.Combine(tempDirectory, "snapshot.db");
        try
        {
            token.ThrowIfCancellationRequested();
            await using (var source = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString()))
            await using (var target = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = snapshot, Pooling = false }.ToString()))
            {
                await source.OpenAsync(token);
                await target.OpenAsync(token);
                source.BackupDatabase(target);
                await ExecuteAsync(target, null, "PRAGMA journal_mode=DELETE", token);
            }
            await ValidateBackupAsync(snapshot, token);
            await using var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = snapshot, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
            await db.OpenAsync(token);
            async Task<long> Scalar(string sql)
            {
                using var command = db.CreateCommand(); command.CommandText = sql;
                return Convert.ToInt64(await command.ExecuteScalarAsync(token));
            }
            var version = (int)await Scalar("PRAGMA user_version");
            return new(path, version, await Scalar("SELECT COUNT(*) FROM Workspaces"), await Scalar("SELECT COUNT(*) FROM LaunchRuns"),
                version >= 2 ? await Scalar("SELECT COUNT(*) FROM StudioProfiles") : 0,
                version >= 2 ? await Scalar("SELECT COUNT(*) FROM StudioDefinitions") : 0);
        }
        finally { Directory.Delete(tempDirectory, recursive: true); }
    }
}
