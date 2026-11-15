using Microsoft.Data.Sqlite;
using Shift.Domain;
using Shift.Infrastructure;
using Shift.App.Services;
using Xunit;
namespace Shift.Tests;

public sealed class BackupCatalogTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "shift-recovery-tests-" + Guid.NewGuid().ToString("N"));
    private async Task<SqliteStore> Create()
    {
        var store = new SqliteStore(Path.Combine(root, "live.db"));
        await store.InitializeAsync();
        await store.SaveAsync(new Workspace { Name = "Работа 🟣", Items = [new() { Name = "Docs", Kind = ItemKind.Url, Target = "https://example.com" }] });
        return store;
    }
    [Fact]
    public async Task Preview_validates_private_snapshot_and_preserves_source_and_live_state()
    {
        var live = await Create(); var path = await live.BackupAsync(Path.Combine(root, "Копии"));
        var bytes = await File.ReadAllBytesAsync(path);
        var result = await new BackupCatalog().InspectAsync(path);
        Assert.Equal(Path.GetFullPath(path), result.Path);
        Assert.Equal(1, result.Workspaces);
        Assert.Equal(0, result.LaunchRuns); Assert.Equal(0, result.StudioProfiles); Assert.Equal(0, result.WidgetDefinitions);
        Assert.False(result.ExternalStudioResourcesIncluded);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
        Assert.Equal("Работа 🟣", Assert.Single(await live.ListAsync()).Name);
    }
    [Fact]
    public async Task Invalid_database_cannot_modify_live_data()
    {
        var live = await Create(); var path = Path.Combine(root, "broken.db");
        await File.WriteAllTextAsync(path, "not sqlite");
        await Assert.ThrowsAnyAsync<Exception>(() => new BackupCatalog().InspectAsync(path));
        Assert.Equal("Работа 🟣", Assert.Single(await live.ListAsync()).Name);
        Assert.Equal("not sqlite", await File.ReadAllTextAsync(path));
    }
    [Fact]
    public async Task Missing_file_is_not_created_by_inspection()
    {
        Directory.CreateDirectory(root); var path = Path.Combine(root, "missing.db");
        await Assert.ThrowsAsync<FileNotFoundException>(() => new BackupCatalog().InspectAsync(path));
        Assert.False(File.Exists(path));
    }
    [Fact]
    public async Task Rejects_unknown_schema_before_restore()
    {
        var live = await Create(); var path = await live.BackupAsync(Path.Combine(root, "backups"));
        await Change(path, "PRAGMA user_version=999");
        await Assert.ThrowsAsync<InvalidDataException>(() => new BackupCatalog().InspectAsync(path));
        Assert.Single(await live.ListAsync());
    }
    [Fact]
    public async Task Rejects_executable_trigger_even_if_integrity_is_valid()
    {
        var live = await Create(); var path = await live.BackupAsync(Path.Combine(root, "backups"));
        await Change(path, "CREATE TRIGGER unexpected AFTER INSERT ON Tags BEGIN DELETE FROM Workspaces; END");
        await Assert.ThrowsAsync<InvalidDataException>(() => new BackupCatalog().InspectAsync(path));
        Assert.Single(await live.ListAsync());
    }
    [Fact]
    public async Task Inspection_captures_committed_WAL_pages()
    {
        var live = await Create();
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = live.DatabasePath, Pooling = false }.ToString());
        await connection.OpenAsync();
        using var command = connection.CreateCommand(); command.CommandText = "PRAGMA journal_mode=WAL; PRAGMA wal_autocheckpoint=0; INSERT INTO Workspaces SELECT '11111111-1111-1111-1111-111111111111', 'WAL space', Description, Color, IsFavorite, CreatedAt, UpdatedAt FROM Workspaces LIMIT 1";
        await command.ExecuteNonQueryAsync();
        var result = await new BackupCatalog().InspectAsync(live.DatabasePath);
        Assert.Equal(2, result.Workspaces);
    }
    [Fact]
    public async Task Cancelled_inspection_and_inventory_do_not_start_work()
    {
        var live = await Create(); using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        var catalog = new BackupCatalog();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => catalog.InspectAsync(live.DatabasePath, cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => catalog.ListAsync(root, cancellation.Token));
    }
    [Fact]
    public async Task Inventory_is_bounded_ignores_other_files_and_does_not_recurse()
    {
        Directory.CreateDirectory(root);
        for (var i = 0; i < 105; i++) await File.WriteAllTextAsync(Path.Combine(root, i + ".db"), "fixture");
        await File.WriteAllTextAsync(Path.Combine(root, "other.txt"), "fixture");
        Directory.CreateDirectory(Path.Combine(root, "unrelated"));
        await File.WriteAllTextAsync(Path.Combine(root, "unrelated", "hidden.db"), "fixture");
        var inventory = await new BackupCatalog().ListAsync(root);
        Assert.Equal(BackupCatalog.MaximumEntries, inventory.Entries.Count); Assert.True(inventory.Truncated);
        Assert.DoesNotContain(inventory.Entries, e => e.Path.Contains("hidden.db"));
    }
    [Fact]
    public async Task Inventory_handles_absent_directory_and_known_subdirectories()
    {
        var catalog = new BackupCatalog(); var missing = await catalog.ListAsync(root);
        Assert.Empty(missing.Entries); Assert.Empty(missing.UnavailableDirectories);
        Directory.CreateDirectory(Path.Combine(root, "automatic"));
        var file = Path.Combine(root, "automatic", "snapshot.db"); await File.WriteAllTextAsync(file, "fixture");
        var inventory = await catalog.ListAsync(root);
        Assert.Equal(file, Assert.Single(inventory.Entries).Path); Assert.Empty(inventory.UnavailableDirectories);
    }
    [Fact]
    public async Task Inventory_missing_root_is_absent_not_an_access_warning()
    {
        var inventory = await new BackupCatalog().ListAsync(root);
        Assert.Empty(inventory.Entries);
        Assert.Empty(inventory.UnavailableDirectories);
        Assert.False(inventory.Truncated);
    }
    [Fact]
    public async Task Inventory_missing_optional_directories_do_not_warn()
    {
        Directory.CreateDirectory(root);
        var snapshot = Path.Combine(root, "snapshot.db");
        await File.WriteAllTextAsync(snapshot, "fixture");
        var inventory = await new BackupCatalog().ListAsync(root);
        Assert.Equal(snapshot, Assert.Single(inventory.Entries).Path);
        Assert.Empty(inventory.UnavailableDirectories);
        Assert.False(inventory.Truncated);
    }
    [Fact]
    public void New_recovery_text_is_localized_and_explicit_about_scope()
    {
        var text = new Strings(); Assert.Contains("НЕ входят", text.Recovery("Scope"));
        text.Language = "en"; Assert.Contains("NOT included", text.Recovery("Scope"));
        Assert.Contains("WAL", text.Recovery("FullCopy"));
    }
    private static async Task Change(string path, string sql)
    {
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        await connection.OpenAsync(); using var command = connection.CreateCommand(); command.CommandText = sql; await command.ExecuteNonQueryAsync();
    }
    public void Dispose() { SqliteConnection.ClearAllPools(); if (Directory.Exists(root)) Directory.Delete(root, true); }
}
