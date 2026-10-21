using Microsoft.Data.Sqlite;
using Shift.Application;
using Shift.Domain;
using Shift.Infrastructure;
using Xunit;
namespace Shift.Tests;

public sealed class StabilityTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "shift-v1-tests-" + Guid.NewGuid());
    private async Task<SqliteStore> Store(string name = "shift.db")
    {
        var store = new SqliteStore(Path.Combine(directory, name));
        await store.InitializeAsync();
        return store;
    }
    private static Workspace Space(string name = "Работа") => new() { Name = name, Items = [new() { Name = "Docs", Kind = ItemKind.Url, Target = "https://example.com" }] };
    private static LaunchRun Run(Guid workspace, string name, int index, RunStatus status = RunStatus.Completed) => new(Guid.NewGuid(), workspace, name, DateTimeOffset.UtcNow.AddMinutes(-index), DateTimeOffset.UtcNow.AddMinutes(-index).AddSeconds(2), status, []);
    [Fact]
    public async Task History_pagination_counts_all_records_and_unicode_search_matches()
    {
        var store = await Store();
        var id = Guid.NewGuid();
        for (var index = 0; index < 123; index++)
            await store.SaveRunAsync(Run(id, index % 2 == 0 ? "РАБОТА" : "Study", index, index % 3 == 0 ? RunStatus.PartialFailure : RunStatus.Completed));
        var first = await store.QueryHistoryAsync(new(PageSize: 30));
        var second = await store.QueryHistoryAsync(new(Offset: 30, PageSize: 30));
        Assert.Equal(123, first.TotalCount);
        Assert.Equal(30, first.Runs.Count);
        Assert.True(first.HasNext);
        Assert.False(first.HasPrevious);
        Assert.True(second.HasPrevious);
        Assert.Empty(first.Runs.Select(r => r.Id).Intersect(second.Runs.Select(r => r.Id)));
        var search = await store.QueryHistoryAsync(new("работа", true, PageSize: 1000));
        Assert.Equal(21, search.TotalCount);
        Assert.All(search.Runs, r => Assert.Equal(RunStatus.PartialFailure, r.Status));
        var usage = await store.GetUsageAsync();
        Assert.Equal(123, usage.TotalRuns);
        Assert.Equal(41, usage.FailedRuns);
        Assert.Equal(123, Assert.Single(usage.Workspaces).LaunchCount);
        Assert.InRange(usage.Workspaces[0].AverageDurationMs, 1950, 2050);
    }
    [Fact]
    public async Task Clear_history_preserves_active_run_and_workspace_data()
    {
        var store = await Store();
        var space = Space();
        await store.SaveAsync(space);
        await store.SaveRunAsync(Run(space.Id, space.Name, 0));
        await store.SaveRunAsync(Run(space.Id, space.Name, 1, RunStatus.Running));
        await store.ClearHistoryAsync();
        Assert.Single(await store.ListAsync());
        Assert.Equal(RunStatus.Running, Assert.Single(await store.HistoryAsync()).Status);
    }
    [Fact]
    public async Task Restore_replaces_data_and_retains_safety_backup()
    {
        var store = await Store();
        var space = Space();
        await store.SaveAsync(space);
        await store.SaveSettingsAsync(new()
        {
            Theme = AppTheme.Forest,
            Language = "en"
        });
        var backup = await store.BackupAsync(Path.Combine(directory, "original"));
        await store.SaveAsync(Space("Current"));
        await store.SaveSettingsAsync(new()
        {
            Language = "ru"
        });
        var restored = await store.RestoreAsync(backup, Path.Combine(directory, "safety"));
        Assert.Equal(space.Id, Assert.Single(await store.ListAsync()).Id);
        Assert.Equal("en", (await store.LoadSettingsAsync()).Language);
        var safety = new SqliteStore(restored.SafetyBackupPath);
        await safety.InitializeAsync();
        Assert.Equal(2, (await safety.ListAsync()).Count);
    }
    [Fact]
    public async Task Restore_includes_committed_changes_from_source_WAL()
    {
        var store = await Store();
        await store.SaveAsync(Space("Original"));
        var backup = await store.BackupAsync(Path.Combine(directory, "wal-source"));
        await using var source = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = backup, Pooling = false }.ToString());
        await source.OpenAsync();
        using var command = source.CreateCommand();
        command.CommandText = "PRAGMA journal_mode=WAL; PRAGMA wal_autocheckpoint=0; UPDATE Workspaces SET Name='Committed in WAL';";
        await command.ExecuteNonQueryAsync();
        Assert.True(new FileInfo(backup + "-wal").Length > 0);

        await store.RestoreAsync(backup, Path.Combine(directory, "safety"));

        Assert.Equal("Committed in WAL", Assert.Single(await store.ListAsync()).Name);
    }
    [Theory]
    [InlineData("UPDATE Workspaces SET Name=''")]
    [InlineData("UPDATE WorkspaceItems SET Kind=999")]
    [InlineData("UPDATE LaunchRuns SET Status=999")]
    [InlineData("UPDATE LaunchRunItems SET ItemId='invalid-guid'")]
    [InlineData("UPDATE LaunchRunItems SET Status=999")]
    [InlineData("UPDATE Workspaces SET CreatedAt='invalid-date'")]
    [InlineData("UPDATE LaunchRuns SET StartedAt='invalid-date'")]
    [InlineData("UPDATE LaunchRuns SET WorkspaceName=''")]
    public async Task Invalid_backup_content_is_rejected_before_replacing_current_data(string corruption)
    {
        var store = await Store();
        var space = Space("Current data");
        await store.SaveAsync(space);
        await store.SaveRunAsync(new(Guid.NewGuid(), space.Id, space.Name, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
            RunStatus.Completed, [new(space.Items[0].Id, "Docs", ItemKind.Url, 0, ItemStatus.Success)]));
        var backup = await store.BackupAsync(Path.Combine(directory, "invalid-content"));
        await using (var source = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = backup, Pooling = false }.ToString()))
        {
            await source.OpenAsync();
            using var command = source.CreateCommand();
            command.CommandText = corruption;
            await command.ExecuteNonQueryAsync();
        }

        await Assert.ThrowsAnyAsync<Exception>(() => store.RestoreAsync(backup, Path.Combine(directory, "safety")));

        Assert.Equal(space.Id, Assert.Single(await store.ListAsync()).Id);
        Assert.Equal("Current data", Assert.Single(await store.ListAsync()).Name);
        Assert.Equal(RunStatus.Completed, Assert.Single(await store.HistoryAsync()).Status);
        Assert.Empty(Directory.EnumerateFiles(directory, "*.restore-*"));
    }
    [Fact]
    public async Task Restore_validates_history_after_the_first_page()
    {
        var store = await Store();
        var space = Space();
        await store.SaveAsync(space);
        for (var index = 0; index < 201; index++)
            await store.SaveRunAsync(Run(space.Id, space.Name, index));
        var backup = await store.BackupAsync(Path.Combine(directory, "paged-history"));
        await using (var source = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = backup, Pooling = false }.ToString()))
        {
            await source.OpenAsync();
            using var command = source.CreateCommand();
            command.CommandText = "UPDATE LaunchRuns SET Status=999 WHERE Id=(SELECT Id FROM LaunchRuns ORDER BY StartedAt ASC LIMIT 1)";
            await command.ExecuteNonQueryAsync();
        }

        await Assert.ThrowsAsync<InvalidDataException>(() => store.RestoreAsync(backup, Path.Combine(directory, "safety")));

        Assert.Equal(201, (await store.GetUsageAsync()).CompletedRuns);
        Assert.Equal(space.Id, Assert.Single(await store.ListAsync()).Id);
    }
    [Fact]
    public async Task Invalid_restore_leaves_original_database_untouched()
    {
        var store = await Store();
        await store.SaveAsync(Space());
        var invalid = Path.Combine(directory, "invalid.db");
        await File.WriteAllTextAsync(invalid, "not a database");
        await Assert.ThrowsAnyAsync<Exception>(() => store.RestoreAsync(invalid, Path.Combine(directory, "safety")));
        Assert.Equal("Работа", Assert.Single(await store.ListAsync()).Name);
        await Assert.ThrowsAsync<ValidationException>(() => store.RestoreAsync(store.DatabasePath, Path.Combine(directory, "safety")));
    }
    [Fact]
    public async Task Structurally_invalid_backup_is_rejected_before_replacement()
    {
        var store = await Store();
        await store.SaveAsync(Space());
        var fake = Path.Combine(directory, "fake.db");
        await using (var db = new SqliteConnection("Data Source=" + fake))
        {
            await db.OpenAsync();
            using var cmd = db.CreateCommand();
            cmd.CommandText = "PRAGMA user_version=1;" + string.Join(";", new[] { "Workspaces", "WorkspaceItems", "Tags", "WorkspaceTagLinks", "LaunchRuns", "LaunchRunItems", "AppSettings" }.Select(t => $"CREATE TABLE {t}(Id INTEGER)"));
            await cmd.ExecuteNonQueryAsync();
        }
        await Assert.ThrowsAnyAsync<Exception>(() => store.RestoreAsync(fake, Path.Combine(directory, "safety")));
        Assert.Single(await store.ListAsync());
    }
    [Fact]
    public async Task Cancelled_restore_does_not_change_current_data()
    {
        var store = await Store();
        await store.SaveAsync(Space());
        var backup = await store.BackupAsync(Path.Combine(directory, "backups"));
        await store.SaveAsync(Space("New"));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.RestoreAsync(backup, Path.Combine(directory, "safety"), cancellation.Token));
        Assert.Equal(2, (await store.ListAsync()).Count);
    }
    [Fact]
    public async Task Background_store_serializes_concurrent_writes_and_is_usable_after_cancellation()
    {
        using var store = new LocalDataStore(Path.Combine(directory, "parallel.db"));
        await store.InitializeAsync();
        await Task.WhenAll(Enumerable.Range(0, 40).Select(index => store.SaveAsync(Space("Space " + index))));
        Assert.Equal(40, (await store.ListAsync()).Count);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.ListAsync(cts.Token));
        Assert.Equal(40, (await store.ListAsync()).Count);
    }
    [Fact]
    public async Task Instance_signal_activates_once_without_launch_payload()
    {
        var name = "shift-tests-" + Guid.NewGuid().ToString("N");
        var activated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var server = new SingleInstanceSignal(name, () => activated.TrySetResult(), new NullLog());
        Assert.True(await SingleInstanceSignal.SendAsync(name));
        await activated.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }
    [Fact]
    public void Export_cannot_produce_a_file_that_exceeds_its_own_import_limit()
    {
        var space = Space() with
        {
            Items = Enumerable.Range(0, 100).Select(index => new WorkspaceItem
            {
                Name = "Command " + index,
                Kind = ItemKind.Command,
                Target = new string('Ж', 8192)
            }).ToArray()
        };
        WorkspaceValidator.Validate(space);

        var error = Assert.Throws<ValidationException>(() => WorkspaceTransfer.Export(space));

        Assert.Equal("ExportSize", Assert.Single(error.Issues).Code);
    }
    [Fact]
    public void Duplicate_JSON_keys_are_rejected()
    {
        var json=WorkspaceTransfer.Export(Space("Work")).Replace("\"name\": \"Work\"","\"name\": \"Work\",\"name\":\"Other\"");
        Assert.Throws<ValidationException>(() => WorkspaceTransfer.Import(json));
    }
    [Theory]
    [InlineData("C:\\NUL.txt")]
    [InlineData("C:\\temp\\COM1")]
    [InlineData("C:\\temp\\LPT1.log")]
    [InlineData("C:\\temp\\bad\nfile.pdf")]
    public void Reserved_or_control_character_paths_are_rejected(string target) => Assert.Throws<ValidationException>(() => WorkspaceValidator.Validate(Space() with { Items = [new() { Name = "File", Kind = ItemKind.File, Target = target }] }));
    [Fact]
    public void Null_domain_fields_produce_validation_errors_instead_of_null_reference_errors()
    {
        foreach (var workspace in new[] { Space() with { Name = null! }, Space() with { Tags = null! }, Space() with { Items = null! }, Space() with { Items = [null!] }, Space() with { Items = [new() { Name = "Broken", Target = null!, Icon = null!, Note = null! }] } })
            Assert.Throws<ValidationException>(() => WorkspaceValidator.Validate(workspace));
    }
    [Fact]
    public async Task Invalid_settings_are_never_persisted()
    {
        var store = await Store();
        await Assert.ThrowsAsync<ValidationException>(() => store.SaveSettingsAsync(new() { Language = "invalid" }));
        Assert.Equal("ru", (await store.LoadSettingsAsync()).Language);
    }
    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(directory))
            Directory.Delete(directory, true);
    }
    private sealed class NullLog : IAppLog
    {
        public void Info(string message)
        {
        }
        public void Error(string message, Exception exception)
        {
        }
    }
}
