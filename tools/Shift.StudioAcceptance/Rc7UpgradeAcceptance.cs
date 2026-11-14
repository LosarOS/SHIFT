using System.Text.Json;
using Microsoft.Data.Sqlite;
using Shift.Infrastructure;

internal static class Rc7UpgradeAcceptance
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    public static async Task RunAsync(string stage, SqliteStore store, string data, string[] args)
    {
        var baseline = Path.Combine(data, "rc7-upgrade-baseline.json");
        if (stage == "upgrade-rc7-seed")
        {
            if (args.Length != 4 || File.Exists(store.DatabasePath)) throw new InvalidOperationException("Fresh owned target and RC7 source required.");
            var source = Path.GetFullPath(args[3]);
            if (!File.Exists(Path.Combine(Path.GetDirectoryName(source)!, ".shift-ci-owner"))
                || File.ReadAllText(Path.Combine(Path.GetDirectoryName(source)!, ".shift-ci-owner")).Trim() != args[2]) throw new InvalidOperationException("Unowned RC7 source.");
            await Snapshot(source, store.DatabasePath);
            if (await Version(store.DatabasePath) != 2) throw new InvalidOperationException("Actual RC7 profile must be schema2.");
            var marker = Path.Combine(Path.GetDirectoryName(source)!, "dispatch-proof.txt");
            if (!File.Exists(marker)) throw new InvalidOperationException("Actual RC7 command proof missing.");
            File.Copy(marker, Path.Combine(data, "dispatch-proof.txt"));
            await File.WriteAllTextAsync(baseline, await Records(store));
        }
        else if (stage == "upgrade-rc7-check")
        {
            if (await Version(store.DatabasePath) != 3 || !File.Exists(baseline) || await Records(store) != await File.ReadAllTextAsync(baseline))
                throw new InvalidOperationException("RC7 upgrade lost workspace/history/settings or schema3 was not applied.");
            var backups = Path.Combine(data, "migration-backups"); var confirmed = false;
            foreach (var file in Directory.EnumerateFiles(backups, "*.db"))
                if (await Version(file) == 2 && await Records(new SqliteStore(file)) == await File.ReadAllTextAsync(baseline))
                {
                    var inspected = await SqliteStore.InspectBackupAsync(file);
                    if (inspected.SchemaVersion == 2) confirmed = true;
                }
            if (!confirmed) throw new InvalidOperationException("Matching pre-migration schema2 safety backup missing.");
            await using var db = Open(store.DatabasePath); await db.OpenAsync();
            using var command = db.CreateCommand(); command.CommandText = "PRAGMA foreign_key_check";
            if (await command.ExecuteScalarAsync() is not null) throw new InvalidOperationException("Broken references after RC7 upgrade.");
            Console.WriteLine("ACTUAL_RC7_UPGRADE_PRESERVED");
        }
        else if (stage == "upgrade-rc7-downgrade-snapshot")
        {
            if (args.Length != 4 || File.Exists(store.DatabasePath)) throw new InvalidOperationException("Fresh downgrade target required.");
            var source = Path.GetFullPath(args[3]);
            if (!File.Exists(Path.Combine(Path.GetDirectoryName(source)!, ".studio-ci-owner"))
                || File.ReadAllText(Path.Combine(Path.GetDirectoryName(source)!, ".studio-ci-owner")).Trim() != args[2]) throw new InvalidOperationException("Unowned upgraded source.");
            await Snapshot(source, store.DatabasePath);
            if (await Version(store.DatabasePath) != 3) throw new InvalidOperationException("Downgrade copy must have schema3.");
        }
        else throw new ArgumentException("Unknown RC7 upgrade stage.");
    }
    private static SqliteConnection Open(string path) => new(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
    private static async Task<int> Version(string path)
    { await using var db = Open(path); await db.OpenAsync(); using var command = db.CreateCommand(); command.CommandText = "PRAGMA user_version"; return Convert.ToInt32(await command.ExecuteScalarAsync()); }
    private static async Task Snapshot(string source, string target)
    {
        await using var db = Open(source); await using var destination = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = target, Pooling = false }.ToString());
        await db.OpenAsync(); await destination.OpenAsync(); db.BackupDatabase(destination);
        using var command = destination.CreateCommand(); command.CommandText = "PRAGMA journal_mode=DELETE"; await command.ExecuteNonQueryAsync();
    }
    private static async Task<string> Records(SqliteStore store)
    {
        var spaces = await store.ListAsync(); var history = await store.HistoryAsync(1000); var settings = await store.LoadSettingsAsync();
        if (!spaces.Any(w => w.Name == "SHIFT smoke workspace") || !history.Any()) throw new InvalidOperationException("Actual RC7 UI fixture missing workspace/history.");
        return JsonSerializer.Serialize(new { spaces, history, settings.Theme, settings.Language, settings.MinimizeToTray, settings.OnboardingComplete,
            settings.Notifications, settings.GlobalHotkeyEnabled, settings.BackupDirectory }, Json);
    }
}
