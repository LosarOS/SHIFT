using Microsoft.Data.Sqlite;
using Shift.Application;
using Shift.Domain;
using System.Text.Json;
namespace Shift.Infrastructure;

public sealed partial class SqliteStore : IWorkspaceStore, IRunStore, ISettingsStore, IHistoryQueries, IStudioStore
{
    private readonly string connectionString;
    public string DatabasePath
    {
        get;
    }
    public SqliteStore(string path) : this(path, readOnly: false) { }
    private SqliteStore(string path, bool readOnly, bool? pooling = null)
    {
        DatabasePath = Path.GetFullPath(path);
        connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath, ForeignKeys = true, DefaultTimeout = 15,
            Mode = readOnly ? SqliteOpenMode.ReadOnly : SqliteOpenMode.ReadWriteCreate,
            Pooling = pooling ?? !readOnly
        }.ToString();
    }
    private async Task<SqliteConnection> OpenAsync(CancellationToken token)
    {
        var connection = new SqliteConnection(connectionString);
        try
        {
            await connection.OpenAsync(token);
            connection.CreateFunction<string, string>("CASEFOLD", value => value.ToUpperInvariant(), isDeterministic: true);
            return connection;
        }
        catch { await connection.DisposeAsync(); throw; }
    }
    private static async Task ExecuteAsync(SqliteConnection db, SqliteTransaction? tx, string sql, CancellationToken token, params (string, object?)[] values)
    {
        using var cmd = db.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        foreach (var (name, value) in values)
            cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(token);
    }
    public async Task InitializeAsync(CancellationToken token = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath)!);
        await using var db = await OpenAsync(token);
        using var versionCmd = db.CreateCommand();
        versionCmd.CommandText = "PRAGMA user_version";
        var version = Convert.ToInt32(await versionCmd.ExecuteScalarAsync(token));
        if (version is < 0 or > 2)
            throw new InvalidOperationException("This database belongs to a newer SHIFT version. Update SHIFT before opening it.");
        if (version == 0)
        {
            using var existingSchema = db.CreateCommand();
            existingSchema.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE name NOT GLOB 'sqlite_*'";
            if (Convert.ToInt64(await existingSchema.ExecuteScalarAsync(token)) != 0)
                throw new InvalidDataException("Unversioned nonempty databases cannot be initialized as SHIFT data.");
        }
        if (version == 1)
        {
            // Snapshot committed WAL contents before the first migration schema mutation.
            LastMigrationBackupPath = await BackupAsync(Path.Combine(Path.GetDirectoryName(DatabasePath)!, "migration-backups"), token);
            await ValidateBackupAsync(LastMigrationBackupPath, token);
        }
        await ExecuteAsync(db, null, "PRAGMA journal_mode=WAL; PRAGMA synchronous=FULL;", token);
        using var tx = db.BeginTransaction();
        await ExecuteAsync(db, tx, """
            CREATE TABLE IF NOT EXISTS Workspaces (
              Id TEXT PRIMARY KEY, Name TEXT NOT NULL, Description TEXT NOT NULL, Color TEXT NOT NULL,
              IsFavorite INTEGER NOT NULL, CreatedAt TEXT NOT NULL, UpdatedAt TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS WorkspaceItems (
              Id TEXT PRIMARY KEY, WorkspaceId TEXT NOT NULL REFERENCES Workspaces(Id) ON DELETE CASCADE,
              Name TEXT NOT NULL, Kind INTEGER NOT NULL, Target TEXT NOT NULL, Arguments TEXT NOT NULL,
              WorkingDirectory TEXT NOT NULL, Shell INTEGER NOT NULL, SortOrder INTEGER NOT NULL,
              DelayMs INTEGER NOT NULL CHECK(DelayMs BETWEEN 0 AND 300000), Enabled INTEGER NOT NULL,
              Note TEXT NOT NULL, Icon TEXT NOT NULL);
            CREATE INDEX IF NOT EXISTS IX_WorkspaceItems_Workspace ON WorkspaceItems(WorkspaceId, SortOrder);
            CREATE TABLE IF NOT EXISTS Tags (Name TEXT PRIMARY KEY COLLATE NOCASE);
            CREATE TABLE IF NOT EXISTS WorkspaceTagLinks (
              WorkspaceId TEXT NOT NULL REFERENCES Workspaces(Id) ON DELETE CASCADE,
              TagName TEXT NOT NULL REFERENCES Tags(Name), PRIMARY KEY(WorkspaceId, TagName));
            CREATE TABLE IF NOT EXISTS LaunchRuns (
              Id TEXT PRIMARY KEY, WorkspaceId TEXT NOT NULL, WorkspaceName TEXT NOT NULL,
              StartedAt TEXT NOT NULL, FinishedAt TEXT, Status INTEGER NOT NULL);
            CREATE INDEX IF NOT EXISTS IX_LaunchRuns_StartedAt ON LaunchRuns(StartedAt DESC);
            CREATE INDEX IF NOT EXISTS IX_LaunchRuns_Workspace ON LaunchRuns(WorkspaceId, StartedAt DESC);
            CREATE TABLE IF NOT EXISTS LaunchRunItems (
              RunId TEXT NOT NULL REFERENCES LaunchRuns(Id) ON DELETE CASCADE, ItemId TEXT NOT NULL,
              Name TEXT NOT NULL, Kind INTEGER NOT NULL, SortOrder INTEGER NOT NULL,
              Status INTEGER NOT NULL, Error TEXT, StartedAt TEXT, FinishedAt TEXT, PRIMARY KEY(RunId, ItemId));
            CREATE TABLE IF NOT EXISTS AppSettings (Id INTEGER PRIMARY KEY CHECK(Id=1), Json TEXT NOT NULL);

            """, token);
        if (version < 2)
            await ExecuteAsync(db, tx, StudioSchema, token);
        await ExecuteAsync(db, tx, "PRAGMA user_version=2", token);
        tx.Commit();
    }
    public string? LastMigrationBackupPath { get; private set; }
    public async Task SaveAsync(Workspace workspace, CancellationToken token = default)
    {
        WorkspaceValidator.Validate(workspace);
        await using var db = await OpenAsync(token);
        using var tx = db.BeginTransaction();
        await ExecuteAsync(db, tx, """
            INSERT INTO Workspaces VALUES($id,$name,$description,$color,$favorite,$created,$updated)
            ON CONFLICT(Id) DO UPDATE SET Name=excluded.Name, Description=excluded.Description,
            Color=excluded.Color, IsFavorite=excluded.IsFavorite, UpdatedAt=excluded.UpdatedAt;
            DELETE FROM WorkspaceItems WHERE WorkspaceId=$id;
            DELETE FROM WorkspaceTagLinks WHERE WorkspaceId=$id;
            """, token, ("$id", workspace.Id.ToString()), ("$name", workspace.Name.Trim()), ("$description", workspace.Description),
            ("$color", workspace.Color), ("$favorite", workspace.IsFavorite), ("$created", workspace.CreatedAt.ToString("O")), ("$updated", DateTimeOffset.UtcNow.ToString("O")));
        foreach (var item in workspace.Items)
            await ExecuteAsync(db, tx, "INSERT INTO WorkspaceItems VALUES($id,$workspace,$name,$kind,$target,$args,$dir,$shell,$order,$delay,$enabled,$note,$icon)", token,
                ("$id", item.Id.ToString()), ("$workspace", workspace.Id.ToString()), ("$name", item.Name), ("$kind", (int)item.Kind),
                ("$target", item.Target), ("$args", item.Arguments), ("$dir", item.WorkingDirectory), ("$shell", (int)item.Shell),
                ("$order", item.Order), ("$delay", item.DelayMs), ("$enabled", item.Enabled), ("$note", item.Note), ("$icon", item.Icon));
        foreach (var tag in workspace.Tags.Select(t => t.Trim()).Distinct(StringComparer.OrdinalIgnoreCase))
            await ExecuteAsync(db, tx, "INSERT OR IGNORE INTO Tags VALUES($tag); INSERT INTO WorkspaceTagLinks VALUES($workspace,$tag)", token, ("$tag", tag), ("$workspace", workspace.Id.ToString()));
        await ExecuteAsync(db, tx, "DELETE FROM Tags WHERE Name NOT IN (SELECT TagName FROM WorkspaceTagLinks)", token);
        tx.Commit();
    }
    public async Task<IReadOnlyList<Workspace>> ListAsync(CancellationToken token = default)
    {
        await using var db = await OpenAsync(token);
        using var tx = db.BeginTransaction(deferred: true);
        var workspaces = new List<Workspace>();
        using (var cmd = db.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "SELECT Id,Name,Description,Color,IsFavorite,CreatedAt,UpdatedAt FROM Workspaces ORDER BY IsFavorite DESC, UpdatedAt DESC";
            await using var r = await cmd.ExecuteReaderAsync(token);
            while (await r.ReadAsync(token))
                workspaces.Add(new Workspace { Id = Guid.Parse(r.GetString(0)), Name = r.GetString(1), Description = r.GetString(2), Color = r.GetString(3), IsFavorite = r.GetBoolean(4), CreatedAt = DateTimeOffset.Parse(r.GetString(5)), UpdatedAt = DateTimeOffset.Parse(r.GetString(6)) });
        }
        for (var index = 0; index < workspaces.Count; index++)
        {
            var workspace = workspaces[index];
            var items = new List<WorkspaceItem>();
            var tags = new List<string>();
            using (var cmd = db.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "SELECT Id,WorkspaceId,Name,Kind,Target,Arguments,WorkingDirectory,Shell,SortOrder,DelayMs,Enabled,Note,Icon FROM WorkspaceItems WHERE WorkspaceId=$id ORDER BY SortOrder,Id";
                cmd.Parameters.AddWithValue("$id", workspace.Id.ToString());
                await using var r = await cmd.ExecuteReaderAsync(token);
                while (await r.ReadAsync(token))
                    items.Add(new WorkspaceItem { Id = Guid.Parse(r.GetString(0)), Name = r.GetString(2), Kind = (ItemKind)r.GetInt32(3), Target = r.GetString(4), Arguments = r.GetString(5), WorkingDirectory = r.GetString(6), Shell = (CommandShell)r.GetInt32(7), Order = r.GetInt32(8), DelayMs = r.GetInt32(9), Enabled = r.GetBoolean(10), Note = r.GetString(11), Icon = r.GetString(12) });
            }
            using (var cmd = db.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "SELECT TagName FROM WorkspaceTagLinks WHERE WorkspaceId=$id ORDER BY TagName";
                cmd.Parameters.AddWithValue("$id", workspace.Id.ToString());
                await using var r = await cmd.ExecuteReaderAsync(token);
                while (await r.ReadAsync(token))
                    tags.Add(r.GetString(0));
            }
            workspaces[index] = workspace with
            {
                Items = items,
                Tags = tags
            };
        }
        tx.Commit();
        return workspaces;
    }
    public async Task DeleteAsync(Guid id, CancellationToken token = default)
    {
        await using var db = await OpenAsync(token);
        using var tx = db.BeginTransaction();
        await ExecuteAsync(db, tx, "UPDATE StudioProfiles SET AutoApply=0,Revision=Revision+1 WHERE WorkspaceId=$id; DELETE FROM Workspaces WHERE Id=$id; DELETE FROM Tags WHERE Name NOT IN (SELECT TagName FROM WorkspaceTagLinks)", token, ("$id", id.ToString()));
        tx.Commit();
    }
    public async Task SaveRunAsync(LaunchRun run, CancellationToken token = default)
    {
        await using var db = await OpenAsync(token);
        using var tx = db.BeginTransaction();
        await ExecuteAsync(db, tx, """
            INSERT INTO LaunchRuns VALUES($id,$workspace,$name,$started,$finished,$status)
            ON CONFLICT(Id) DO UPDATE SET FinishedAt=excluded.FinishedAt, Status=excluded.Status;
            DELETE FROM LaunchRunItems WHERE RunId=$id;
            """, token, ("$id", run.Id.ToString()), ("$workspace", run.WorkspaceId.ToString()), ("$name", run.WorkspaceName),
            ("$started", run.StartedAt.ToString("O")), ("$finished", run.FinishedAt?.ToString("O")), ("$status", (int)run.Status));
        foreach (var item in run.Items)
            await ExecuteAsync(db, tx, "INSERT INTO LaunchRunItems VALUES($run,$item,$name,$kind,$order,$status,$error,$started,$finished)", token,
                ("$run", run.Id.ToString()), ("$item", item.ItemId.ToString()), ("$name", item.Name), ("$kind", (int)item.Kind), ("$order", item.Order),
                ("$status", (int)item.Status), ("$error", item.Error), ("$started", item.StartedAt?.ToString("O")), ("$finished", item.FinishedAt?.ToString("O")));
        tx.Commit();
    }
    private const string HistoryWhere = "instr(CASEFOLD(WorkspaceName),CASEFOLD($query))>0 AND ($failures=0 OR Status IN (2,4))";
    private static void BindHistoryFilter(SqliteCommand command, HistoryFilter filter)
    {
        command.Parameters.AddWithValue("$query", filter.Query ?? "");
        command.Parameters.AddWithValue("$failures", filter.OnlyFailures);
        command.Parameters.AddWithValue("$limit", Math.Clamp(filter.PageSize, 1, 1000));
        command.Parameters.AddWithValue("$offset", Math.Max(filter.Offset, 0));
    }
    public async Task<IReadOnlyList<LaunchRun>> HistoryAsync(int limit = 100, CancellationToken token = default)
        => (await QueryHistoryAsync(new HistoryFilter(PageSize: limit), token)).Runs;

    public async Task<HistoryPage> QueryHistoryAsync(HistoryFilter filter, CancellationToken token = default)
    {
        filter = filter with
        {
            Offset = Math.Max(0, filter.Offset),
            PageSize = Math.Clamp(filter.PageSize, 1, 1000)
        };
        await using var db = await OpenAsync(token);
        using var tx = db.BeginTransaction(deferred: true);
        using var countCommand = db.CreateCommand();
        countCommand.Transaction = tx;
        countCommand.CommandText = "SELECT COUNT(*) FROM LaunchRuns WHERE " + HistoryWhere;
        BindHistoryFilter(countCommand, filter);
        var count = Convert.ToInt64(await countCommand.ExecuteScalarAsync(token));
        var runs = await ReadRunsAsync(db, tx, filter, token);
        tx.Commit();
        return new HistoryPage(runs, count, filter.Offset, filter.PageSize);
    }
    private static async Task<IReadOnlyList<LaunchRun>> ReadRunsAsync(SqliteConnection db, SqliteTransaction tx, HistoryFilter filter, CancellationToken token)
    {
        var runs = new List<LaunchRun>();
        using (var cmd = db.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "SELECT Id,WorkspaceId,WorkspaceName,StartedAt,FinishedAt,Status FROM LaunchRuns WHERE " + HistoryWhere + " ORDER BY StartedAt DESC,Id DESC LIMIT $limit OFFSET $offset";
            BindHistoryFilter(cmd, filter);
            await using var r = await cmd.ExecuteReaderAsync(token);
            while (await r.ReadAsync(token))
                runs.Add(new LaunchRun(Guid.Parse(r.GetString(0)), Guid.Parse(r.GetString(1)), r.GetString(2), DateTimeOffset.Parse(r.GetString(3)), r.IsDBNull(4) ? null : DateTimeOffset.Parse(r.GetString(4)), (RunStatus)r.GetInt32(5), []));
        }
        for (var index = 0; index < runs.Count; index++)
        {
            using var cmd = db.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "SELECT RunId,ItemId,Name,Kind,SortOrder,Status,Error,StartedAt,FinishedAt FROM LaunchRunItems WHERE RunId=$id ORDER BY SortOrder,ItemId";
            cmd.Parameters.AddWithValue("$id", runs[index].Id.ToString());
            await using var r = await cmd.ExecuteReaderAsync(token);
            var items = new List<LaunchRunItem>();
            while (await r.ReadAsync(token))
                items.Add(new LaunchRunItem(Guid.Parse(r.GetString(1)), r.GetString(2), (ItemKind)r.GetInt32(3), r.GetInt32(4), (ItemStatus)r.GetInt32(5), r.IsDBNull(6) ? null : r.GetString(6), r.IsDBNull(7) ? null : DateTimeOffset.Parse(r.GetString(7)), r.IsDBNull(8) ? null : DateTimeOffset.Parse(r.GetString(8))));
            runs[index] = runs[index] with
            {
                Items = items
            };
        }
        return runs;
    }
    public async Task RecoverInterruptedAsync(CancellationToken token = default)
    {
        await using var db = await OpenAsync(token);
        using var tx = db.BeginTransaction();
        await ExecuteAsync(db, tx, """
            UPDATE LaunchRunItems SET Status=4, Error='SHIFT stopped before launch completion; dispatch may have occurred.', FinishedAt=$now
            WHERE Status IN (0,1) AND RunId IN (SELECT Id FROM LaunchRuns WHERE Status=0);
            UPDATE LaunchRuns SET Status=4, FinishedAt=$now WHERE Status=0;
            """, token, ("$now", DateTimeOffset.UtcNow.ToString("O")));
        tx.Commit();
    }
    public async Task<AppSettings> LoadSettingsAsync(CancellationToken token = default)
    {
        await using var db = await OpenAsync(token);
        using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT Json FROM AppSettings WHERE Id=1";
        var json = await cmd.ExecuteScalarAsync(token) as string;
        if (json is null)
            return new AppSettings();
        var settings = JsonSerializer.Deserialize<AppSettings>(json, WorkspaceTransfer.Json) ?? throw new InvalidDataException("Settings are empty.");
        if (!Enum.IsDefined(settings.Theme) || settings.Language is not ("ru" or "en") || settings.BackupDirectory is null)
            throw new InvalidDataException("Settings format is invalid.");
        return settings;
    }
    public async Task SaveSettingsAsync(AppSettings settings, CancellationToken token = default)
    {
        if (!Enum.IsDefined(settings.Theme) || settings.Language is not ("ru" or "en") || settings.BackupDirectory is null)
            throw new ValidationException(["Settings format is invalid."]);
        await using var db = await OpenAsync(token);
        await ExecuteAsync(db, null, "INSERT INTO AppSettings VALUES(1,$json) ON CONFLICT(Id) DO UPDATE SET Json=excluded.Json", token, ("$json", JsonSerializer.Serialize(settings, WorkspaceTransfer.Json)));
    }
    public async Task<string> BackupAsync(string directory, CancellationToken token = default)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"SHIFT-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.db");
        var temp = path + ".tmp";
        try
        {
            await using var source = await OpenAsync(token);
            await using (var destination = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = temp, Pooling = false }.ToString()))
            {
                await destination.OpenAsync(token);
                source.BackupDatabase(destination);
            }
            token.ThrowIfCancellationRequested();
            File.Move(temp, path);
            return path;
        }
        catch { if (File.Exists(temp)) File.Delete(temp); throw; }
    }

    public async Task<UsageSummary> GetUsageAsync(CancellationToken token = default)
    {
        await using var db = await OpenAsync(token);
        using var tx = db.BeginTransaction(deferred: true);
        long total = 0, completed = 0, failed = 0, cancelled = 0;
        using (var cmd = db.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "SELECT COUNT(*),COALESCE(SUM(Status=1),0),COALESCE(SUM(Status IN (2,4)),0),COALESCE(SUM(Status IN (3,5)),0) FROM LaunchRuns";
            await using var reader = await cmd.ExecuteReaderAsync(token);
            if (await reader.ReadAsync(token))
            {
                total = reader.GetInt64(0);
                completed = reader.GetInt64(1);
                failed = reader.GetInt64(2);
                cancelled = reader.GetInt64(3);
            }
        }
        var usage = new List<WorkspaceUsage>();
        using (var cmd = db.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "SELECT WorkspaceId,COUNT(*),MAX(StartedAt),COALESCE(AVG((julianday(FinishedAt)-julianday(StartedAt))*86400000),0) FROM LaunchRuns GROUP BY WorkspaceId";
            await using var reader = await cmd.ExecuteReaderAsync(token);
            while (await reader.ReadAsync(token))
                usage.Add(new(Guid.Parse(reader.GetString(0)), reader.GetInt64(1), reader.IsDBNull(2) ? null : DateTimeOffset.Parse(reader.GetString(2)), reader.GetDouble(3)));
        }
        tx.Commit();
        return new(total, completed, failed, cancelled, usage);
    }
    public async Task ClearHistoryAsync(CancellationToken token = default)
    {
        await using var db = await OpenAsync(token);
        // A running queue must retain its durable crash-recovery record.
        await ExecuteAsync(db, null, "DELETE FROM LaunchRuns WHERE Status<>0", token);
    }
    private static async Task ValidateBackupAsync(string path, CancellationToken token)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("Backup file not found.");
        await using var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        await db.OpenAsync(token);
        using var check = db.CreateCommand();
        check.CommandText = "PRAGMA quick_check";
        if (!string.Equals(await check.ExecuteScalarAsync(token) as string, "ok", StringComparison.Ordinal))
            throw new InvalidDataException("Backup failed SQLite integrity validation.");
        check.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type IN ('trigger','view')";
        if (Convert.ToInt64(await check.ExecuteScalarAsync(token)) != 0)
            throw new InvalidDataException("SHIFT backups cannot contain executable triggers or views.");
        check.CommandText = "PRAGMA user_version";
        var backupVersion = Convert.ToInt32(await check.ExecuteScalarAsync(token));
        if (backupVersion is not (1 or 2))
            throw new InvalidDataException("Unsupported backup database version.");
        foreach (var table in new[] { "Workspaces", "WorkspaceItems", "Tags", "WorkspaceTagLinks", "LaunchRuns", "LaunchRunItems", "AppSettings" })
        {
            check.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=$table";
            check.Parameters.Clear();
            check.Parameters.AddWithValue("$table", table);
            if (Convert.ToInt64(await check.ExecuteScalarAsync(token)) != 1)
                throw new InvalidDataException("Backup is missing required SHIFT tables.");
        }
        foreach (var sql in new[]{
            "SELECT Id,Name,Description,Color,IsFavorite,CreatedAt,UpdatedAt FROM Workspaces LIMIT 0",
            "SELECT Id,WorkspaceId,Name,Kind,Target,Arguments,WorkingDirectory,Shell,SortOrder,DelayMs,Enabled,Note,Icon FROM WorkspaceItems LIMIT 0",
            "SELECT Id,WorkspaceId,WorkspaceName,StartedAt,FinishedAt,Status FROM LaunchRuns LIMIT 0",
            "SELECT RunId,ItemId,Name,Kind,SortOrder,Status,Error,StartedAt,FinishedAt FROM LaunchRunItems LIMIT 0",
            "SELECT Name FROM Tags LIMIT 0", "SELECT WorkspaceId,TagName FROM WorkspaceTagLinks LIMIT 0"})
        {
            check.CommandText = sql;
            check.Parameters.Clear();
            await using var columns = await check.ExecuteReaderAsync(token);
        }
        check.CommandText = "SELECT Json FROM AppSettings WHERE Id=1";
        check.Parameters.Clear();
        if (await check.ExecuteScalarAsync(token) is string settingsJson)
        {
            var settings = JsonSerializer.Deserialize<AppSettings>(settingsJson, WorkspaceTransfer.Json);
            if (settings is null || !Enum.IsDefined(settings.Theme) || settings.Language is not ("ru" or "en") || settings.BackupDirectory is null)
                throw new InvalidDataException("Backup settings are invalid.");
        }
        check.Parameters.Clear();
        check.CommandText = "PRAGMA foreign_key_check";
        await using (var violations = await check.ExecuteReaderAsync(token))
            if (await violations.ReadAsync(token))
                throw new InvalidDataException("Backup has invalid database relations.");

        // Validate the same models the UI will read, before replacing the working database.
        // Read-only, unpooled connections cannot leave the temporary snapshot locked on Windows.
        var candidate = new SqliteStore(path, readOnly: true);
        foreach (var workspace in await candidate.ListAsync(token))
            WorkspaceValidator.Validate(workspace);
        if (backupVersion == 2)
            await candidate.ValidateStudioBackupAsync(token);
        for (var offset = 0; ; offset += 200)
        {
            var page = await candidate.QueryHistoryAsync(new(Offset: offset, PageSize: 200), token);
            foreach (var run in page.Runs)
            {
                if (run.Id == Guid.Empty || run.WorkspaceId == Guid.Empty || !Enum.IsDefined(run.Status)
                    || string.IsNullOrWhiteSpace(run.WorkspaceName) || run.WorkspaceName.Length > 100 || run.WorkspaceName.Any(char.IsControl)
                    || run.Items.Any(item => item.ItemId == Guid.Empty || !Enum.IsDefined(item.Kind) || !Enum.IsDefined(item.Status)
                        || item.Order < 0 || string.IsNullOrWhiteSpace(item.Name) || item.Name.Length > 100 || item.Name.Any(char.IsControl)))
                    throw new InvalidDataException("Backup contains invalid launch history.");
            }
            if (!page.HasNext)
                break;
        }
    }
    public async Task<RestoreResult> RestoreAsync(string backupPath, string safetyDirectory, CancellationToken token = default)
    {
        backupPath = Path.GetFullPath(backupPath);
        if (string.Equals(backupPath, DatabasePath, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new ValidationException(["Choose a separate backup file, not the active database."]);
        var temporary = DatabasePath + ".restore-" + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            // SQLite's snapshot includes committed WAL pages. Copying only the main file can
            // silently restore older data when the selected database still has a WAL journal.
            await using (var source = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = backupPath, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString()))
            await using (var target = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = temporary, Pooling = false }.ToString()))
            {
                await source.OpenAsync(token);
                await target.OpenAsync(token);
                token.ThrowIfCancellationRequested();
                source.BackupDatabase(target);
                // Keep the private snapshot self-contained. Read-only validation of a WAL-mode
                // header can otherwise leave journal sidecars behind, even without any writes.
                await ExecuteAsync(target, null, "PRAGMA journal_mode=DELETE", token);
            }
            token.ThrowIfCancellationRequested();
            await ValidateBackupAsync(temporary, token);
            int snapshotVersion;
            await using (var inspected = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = temporary, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString()))
            {
                await inspected.OpenAsync(token);
                using var version = inspected.CreateCommand(); version.CommandText = "PRAGMA user_version";
                snapshotVersion = Convert.ToInt32(await version.ExecuteScalarAsync(token));
            }
            if (snapshotVersion == 1)
            {
                // Upgrade the owned snapshot BEFORE replacing live data. Failed migration must
                // leave the working database unchanged, including backup-path/DDL failures.
                var upgraded = new SqliteStore(temporary, readOnly: false, pooling: false);
                await upgraded.InitializeAsync(token);
                LastMigrationBackupPath = upgraded.LastMigrationBackupPath;
                await using (var standalone = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = temporary, Pooling = false }.ToString()))
                {
                    await standalone.OpenAsync(token);
                    await ExecuteAsync(standalone, null, "PRAGMA journal_mode=DELETE", token);
                }
                await ValidateBackupAsync(temporary, token);
            }
            var safety = await BackupAsync(safetyDirectory, token);
            await using (var db = await OpenAsync(token))
            {
                using var command = db.CreateCommand();
                command.CommandText = "PRAGMA wal_checkpoint(TRUNCATE)";
                await using var result = await command.ExecuteReaderAsync(token);
                if (await result.ReadAsync(token) && result.GetInt32(0) != 0)
                    throw new IOException("Database is busy. Close other SHIFT processes before restoring.");
            }
            using (var poolKey = new SqliteConnection(connectionString))
                SqliteConnection.ClearPool(poolKey);
            token.ThrowIfCancellationRequested();
            // Checkpoint made the current main file complete; a crash before replacement leaves it usable.
            foreach (var suffix in new[] { "-wal", "-shm" })
                if (File.Exists(DatabasePath + suffix))
                    File.Delete(DatabasePath + suffix);
            File.Move(temporary, DatabasePath, true);
            // Do not observe cancellation after committing replacement; report the actual resulting state.
            await InitializeAsync(CancellationToken.None);
            await RecoverInterruptedAsync(CancellationToken.None);
            return new(safety);
        }
        finally
        {
            foreach (var suffix in new[] { "", "-wal", "-shm", "-journal" })
                if (File.Exists(temporary + suffix))
                    File.Delete(temporary + suffix);
        }
    }
}
