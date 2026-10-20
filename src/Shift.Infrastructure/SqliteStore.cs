using Microsoft.Data.Sqlite;
using Shift.Application;
using Shift.Domain;
using System.Text.Json;
namespace Shift.Infrastructure;

public sealed class SqliteStore : IWorkspaceStore, IRunStore, ISettingsStore
{
    private readonly string connectionString;
    public string DatabasePath
    {
        get;
    }
    public SqliteStore(string path)
    {
        DatabasePath = Path.GetFullPath(path);
        connectionString = new SqliteConnectionStringBuilder { DataSource = DatabasePath, ForeignKeys = true, DefaultTimeout = 15 }.ToString();
    }
    private async Task<SqliteConnection> OpenAsync(CancellationToken token)
    {
        var connection = new SqliteConnection(connectionString);
        try
        {
            await connection.OpenAsync(token);
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
        if (version > 1)
            throw new InvalidOperationException("This database belongs to a newer SHIFT version. Update SHIFT before opening it.");
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
            CREATE TABLE IF NOT EXISTS LaunchRunItems (
              RunId TEXT NOT NULL REFERENCES LaunchRuns(Id) ON DELETE CASCADE, ItemId TEXT NOT NULL,
              Name TEXT NOT NULL, Kind INTEGER NOT NULL, SortOrder INTEGER NOT NULL,
              Status INTEGER NOT NULL, Error TEXT, StartedAt TEXT, FinishedAt TEXT, PRIMARY KEY(RunId, ItemId));
            CREATE TABLE IF NOT EXISTS AppSettings (Id INTEGER PRIMARY KEY CHECK(Id=1), Json TEXT NOT NULL);
            PRAGMA user_version=1;
            """, token);
        tx.Commit();
    }
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
            cmd.CommandText = "SELECT * FROM Workspaces ORDER BY IsFavorite DESC, UpdatedAt DESC";
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
                cmd.CommandText = "SELECT * FROM WorkspaceItems WHERE WorkspaceId=$id ORDER BY SortOrder,Id";
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
        await ExecuteAsync(db, tx, "DELETE FROM Workspaces WHERE Id=$id; DELETE FROM Tags WHERE Name NOT IN (SELECT TagName FROM WorkspaceTagLinks)", token, ("$id", id.ToString()));
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
    public async Task<IReadOnlyList<LaunchRun>> HistoryAsync(int limit = 100, CancellationToken token = default)
    {
        await using var db = await OpenAsync(token);
        using var tx = db.BeginTransaction(deferred: true);
        var runs = new List<LaunchRun>();
        using (var cmd = db.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "SELECT * FROM LaunchRuns ORDER BY StartedAt DESC LIMIT $limit";
            cmd.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 1000));
            await using var r = await cmd.ExecuteReaderAsync(token);
            while (await r.ReadAsync(token))
                runs.Add(new LaunchRun(Guid.Parse(r.GetString(0)), Guid.Parse(r.GetString(1)), r.GetString(2), DateTimeOffset.Parse(r.GetString(3)), r.IsDBNull(4) ? null : DateTimeOffset.Parse(r.GetString(4)), (RunStatus)r.GetInt32(5), []));
        }
        for (var index = 0; index < runs.Count; index++)
        {
            using var cmd = db.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "SELECT * FROM LaunchRunItems WHERE RunId=$id ORDER BY SortOrder,ItemId";
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
        tx.Commit();
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
}
