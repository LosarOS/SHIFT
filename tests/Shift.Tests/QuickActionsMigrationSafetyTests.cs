using Microsoft.Data.Sqlite;
using Shift.Domain;
using Shift.Infrastructure;
using Xunit;
namespace Shift.Tests;

public sealed class QuickActionsMigrationSafetyTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "shift-v3-safety-" + Guid.NewGuid().ToString("N"));
    private async Task<(SqliteStore Store, Guid Instance)> LegacyV2()
    {
        var store = new SqliteStore(Path.Combine(root, Guid.NewGuid().ToString("N"), "shift.db"));
        await store.InitializeAsync();
        await store.SaveAsync(new Workspace { Name = "Keep workspace" });
        var definition = await store.SaveDefinitionAsync(new() { Title = "Keep note", Kind = WidgetKind.Notes });
        var profile = await store.SaveProfileAsync(new() { Name = "Keep profile", Widgets = [new() { DefinitionId = definition.Id }] });
        var instance = profile.Widgets[0].Id;
        await store.SaveWidgetStateAsync(new() { InstanceId = instance, Notes = "Never lose this — строка" });
        await Sql(store.DatabasePath, """
            PRAGMA foreign_keys=OFF; BEGIN;
            CREATE TABLE LegacyDefinitions(Id TEXT PRIMARY KEY,SchemaVersion INTEGER NOT NULL CHECK(SchemaVersion=1),Title TEXT NOT NULL,
            Kind INTEGER NOT NULL CHECK(Kind BETWEEN 0 AND 3),Style TEXT NOT NULL CHECK(json_valid(Style)),Content TEXT NOT NULL CHECK(json_valid(Content)),Revision INTEGER NOT NULL CHECK(Revision>0));
            INSERT INTO LegacyDefinitions SELECT * FROM StudioDefinitions; DROP TABLE StudioDefinitions;
            ALTER TABLE LegacyDefinitions RENAME TO StudioDefinitions; PRAGMA user_version=2; COMMIT;
            """);
        return (store, instance);
    }
    [Fact]
    public async Task V2_restore_migrates_only_private_snapshot_and_preserves_external_backup()
    {
        var (source, instance) = await LegacyV2();
        var backup = await source.BackupAsync(Path.Combine(root, "old-backups"));
        var original = await File.ReadAllBytesAsync(backup);
        var target = new SqliteStore(Path.Combine(root, "target", "shift.db")); await target.InitializeAsync();
        await target.SaveAsync(new Workspace { Name = "Safety copy must retain this" });
        var restored = await target.RestoreAsync(backup, Path.Combine(root, "safety"));
        Assert.Equal(3L, await Sql(target.DatabasePath, "PRAGMA user_version"));
        Assert.Equal(2L, await Sql(backup, "PRAGMA user_version"));
        Assert.Equal(original, await File.ReadAllBytesAsync(backup));
        Assert.Equal("Never lose this — строка", (await target.GetWidgetStateAsync(instance))!.Notes);
        Assert.Null(await Sql(target.DatabasePath, "PRAGMA foreign_key_check"));
        var safety = new SqliteStore(restored.SafetyBackupPath);
        Assert.Equal("Safety copy must retain this", Assert.Single(await safety.ListAsync()).Name);
    }
    [Fact]
    public async Task Failed_migration_leaves_v2_tables_state_and_references_intact()
    {
        var (store, instance) = await LegacyV2();
        // A conflicting staging name forces actual migration DDL failure, after the validated backup was written.
        await Sql(store.DatabasePath, "CREATE TABLE StudioDefinitions_v3(Collision TEXT)");
        await Assert.ThrowsAsync<SqliteException>(() => store.InitializeAsync());
        Assert.Equal(2L, await Sql(store.DatabasePath, "PRAGMA user_version"));
        Assert.Equal("Never lose this — строка", (await store.GetWidgetStateAsync(instance))!.Notes);
        Assert.Equal("Keep profile", Assert.Single(await store.ListProfilesAsync()).Name);
        Assert.Null(await Sql(store.DatabasePath, "PRAGMA foreign_key_check"));
        Assert.NotNull(store.LastMigrationBackupPath);
        Assert.Equal(2L, await Sql(store.LastMigrationBackupPath!, "PRAGMA user_version"));
    }
    [Fact]
    public async Task Cancelled_restore_leaves_v2_source_and_live_v3_unchanged()
    {
        var (source, _) = await LegacyV2(); var backup = await source.BackupAsync(Path.Combine(root, "backup"));
        var target = new SqliteStore(Path.Combine(root, "target", "shift.db")); await target.InitializeAsync();
        await target.SaveAsync(new Workspace { Name = "Current" });
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => target.RestoreAsync(backup, Path.Combine(root, "safety"), cancellation.Token));
        Assert.Equal("Current", Assert.Single(await target.ListAsync()).Name);
        Assert.Equal(3L, await Sql(target.DatabasePath, "PRAGMA user_version"));
        Assert.Equal(2L, await Sql(backup, "PRAGMA user_version"));
    }
    [Fact]
    public async Task Cancelled_initialization_does_not_upgrade_v2()
    {
        var (store, instance) = await LegacyV2(); using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.InitializeAsync(cancellation.Token));
        Assert.Equal(2L, await Sql(store.DatabasePath, "PRAGMA user_version"));
        Assert.Equal("Never lose this — строка", (await store.GetWidgetStateAsync(instance))!.Notes);
    }
    private static async Task<object?> Sql(string path, string sql)
    {
        await using var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        await db.OpenAsync(); using var command = db.CreateCommand(); command.CommandText = sql; return await command.ExecuteScalarAsync();
    }
    public void Dispose() { SqliteConnection.ClearAllPools(); if (Directory.Exists(root)) Directory.Delete(root, true); }
}
