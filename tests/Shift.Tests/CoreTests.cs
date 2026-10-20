using Shift.Application;
using Shift.Domain;
using Shift.Infrastructure;
using Microsoft.Data.Sqlite;
using Xunit;
namespace Shift.Tests;

public sealed class CoreTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "shift-tests-" + Guid.NewGuid());
    private async Task<SqliteStore> Store()
    {
        var store = new SqliteStore(Path.Combine(directory, "shift.db"));
        await store.InitializeAsync();
        return store;
    }
    private static Workspace Sample() => new() { Name = "Engineering", Tags = ["Work", "Local"], Items = [new() { Name = "Docs", Kind = ItemKind.Url, Target = "https://learn.microsoft.com", Order = 1 }, new() { Name = "Disabled", Kind = ItemKind.Url, Target = "https://example.com", Order = 0, Enabled = false }] };
    [Fact]
    public async Task Sqlite_roundtrip_update_clone_delete_preserves_history()
    {
        var store = await Store();
        var workspace = Sample();
        await store.SaveAsync(workspace);
        var read = Assert.Single(await store.ListAsync());
        Assert.Equal(2, read.Items.Count);
        Assert.Equal("Local", read.Tags[0]);
        await store.SaveAsync(read with
        {
            Name = "Updated",
            Tags = ["Solo"],
            Items = read.Items.Take(1).ToArray()
        });
        read = Assert.Single(await store.ListAsync());
        Assert.Single(read.Items);
        Assert.Equal("Solo", Assert.Single(read.Tags));
        var clone = read.Duplicate("Copy");
        await store.SaveAsync(clone);
        Assert.NotEqual(read.Items[0].Id, clone.Items[0].Id);
        await new LaunchCoordinator(store, new FakeLauncher(), new NullLog()).RunAsync(read, new Consent(true));
        await store.DeleteAsync(read.Id);
        Assert.Single(await store.ListAsync());
        Assert.Single(await store.HistoryAsync());
    }
    [Fact]
    public async Task Launch_is_ordered_skips_disabled_and_continues_after_error()
    {
        var store = await Store();
        var launcher = new FakeLauncher { Fail = true };
        var workspace = Sample() with
        {
            Items = [.. Sample().Items, new() { Name = "Last", Kind = ItemKind.Url, Target = "https://example.org", Order = 2 }]
        };
        var run = await new LaunchCoordinator(store, launcher, new NullLog()).RunAsync(workspace, new Consent(true));
        Assert.Equal(RunStatus.PartialFailure, run.Status);
        Assert.Equal(new[] { "Docs", "Last" }, launcher.Names);
        Assert.Equal(new[] { ItemStatus.Skipped, ItemStatus.Failed, ItemStatus.Success }, run.Items.Select(i => i.Status));
        Assert.Equal(run.Status, Assert.Single(await store.HistoryAsync()).Status);
    }
    [Fact]
    public async Task Command_decline_launches_nothing_and_is_recorded()
    {
        var store = await Store();
        var launcher = new FakeLauncher();
        var consent = new Consent(false);
        var run = await new LaunchCoordinator(store, launcher, new NullLog()).RunAsync(new Workspace { Name = "Commands", Items = [new() { Name = "Visible command", Kind = ItemKind.Command, Target = "Write-Output hello" }] }, consent);
        Assert.True(consent.Called);
        Assert.Empty(launcher.Names);
        Assert.Equal(RunStatus.Declined, run.Status);
        Assert.Equal(ItemStatus.Skipped, run.Items[0].Status);
    }
    [Fact]
    public async Task Cancellation_during_delay_preserves_success_and_skips_remaining()
    {
        var store = await Store();
        using var cts = new CancellationTokenSource();
        var launcher = new FakeLauncher { After = () => cts.Cancel() };
        var workspace = new Workspace { Name = "Cancel", Items = [new() { Name = "First", Kind = ItemKind.Url, Target = "https://example.com" }, new() { Name = "Later", Kind = ItemKind.Url, Target = "https://example.com", Order = 1, DelayMs = 10000 }] };
        var run = await new LaunchCoordinator(store, launcher, new NullLog()).RunAsync(workspace, new Consent(true), token: cts.Token);
        Assert.Equal(RunStatus.Cancelled, run.Status);
        Assert.Equal(ItemStatus.Success, run.Items[0].Status);
        Assert.Equal(ItemStatus.Skipped, run.Items[1].Status);
    }
    [Fact]
    public async Task Cancellation_inside_delay_never_dispatches()
    {
        var store = await Store();
        using var cts = new CancellationTokenSource(100);
        var launcher = new FakeLauncher();
        var run = await new LaunchCoordinator(store, launcher, new NullLog()).RunAsync(new Workspace { Name = "Delay", Items = [new() { Name = "Later", Kind = ItemKind.Url, Target = "https://example.com", DelayMs = 10000 }] }, new Consent(true), token: cts.Token);
        Assert.Empty(launcher.Names);
        Assert.Equal(RunStatus.Cancelled, run.Status);
    }
    [Fact]
    public async Task Concurrent_launch_is_rejected()
    {
        var store = await Store();
        var blocker = new BlockingLauncher();
        var coordinator = new LaunchCoordinator(store, blocker, new NullLog());
        var first = coordinator.RunAsync(Sample(), new Consent(true));
        await blocker.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.RunAsync(Sample(), new Consent(true)));
        blocker.Release.SetResult();
        await first;
    }
    [Fact]
    public async Task Crash_recovery_marks_only_incomplete_items_and_does_not_relaunch()
    {
        var store = await Store();
        var run = new LaunchRun(Guid.NewGuid(), Guid.NewGuid(), "Old", DateTimeOffset.UtcNow, null, RunStatus.Running, [new(Guid.NewGuid(), "Done", ItemKind.Url, 0, ItemStatus.Success), new(Guid.NewGuid(), "Unknown", ItemKind.App, 1, ItemStatus.Running)]);
        await store.SaveRunAsync(run);
        await store.RecoverInterruptedAsync();
        var read = Assert.Single(await store.HistoryAsync());
        Assert.Equal(RunStatus.Interrupted, read.Status);
        Assert.Equal(ItemStatus.Success, read.Items[0].Status);
        Assert.Equal(ItemStatus.Skipped, read.Items[1].Status);
        Assert.NotNull(read.FinishedAt);
    }
    [Fact]
    public async Task Online_backup_is_consistent_and_settings_survive()
    {
        var store = await Store();
        await store.SaveAsync(Sample());
        await store.SaveSettingsAsync(new()
        {
            Theme = AppTheme.Forest,
            Language = "en"
        });
        var backup = await store.BackupAsync(Path.Combine(directory, "backups"));
        var restored = new SqliteStore(backup);
        await restored.InitializeAsync();
        Assert.Single(await restored.ListAsync());
        Assert.Equal(AppTheme.Forest, (await restored.LoadSettingsAsync()).Theme);
    }
    [Fact]
    public async Task Future_database_version_is_rejected_without_migration()
    {
        var store = await Store();
        await using (var db = new SqliteConnection("Data Source=" + store.DatabasePath))
        {
            await db.OpenAsync();
            using var cmd = db.CreateCommand();
            cmd.CommandText = "PRAGMA user_version=99";
            await cmd.ExecuteNonQueryAsync();
        }
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.InitializeAsync());
    }
    [Fact]
    public void Json_roundtrip_allocates_new_ids_and_preserves_disabled_commands()
    {
        var workspace = Sample() with
        {
            Items = [new() { Name = "Command", Kind = ItemKind.Command, Target = "Get-Date", Enabled = false }]
        };
        var imported = WorkspaceTransfer.Import(WorkspaceTransfer.Export(workspace));
        Assert.NotEqual(workspace.Id, imported.Id);
        Assert.NotEqual(workspace.Items[0].Id, imported.Items[0].Id);
        Assert.False(imported.Items[0].Enabled);
        Assert.Equal("Get-Date", imported.Items[0].Target);
    }
    [Theory]
    [InlineData("{}")]
    [InlineData("{\"schemaVersion\":9}")]
    [InlineData("{\"schemaVersion\":1,\"workspace\":{\"name\":null}}")]
    [InlineData("{\"schemaVersion\":1,\"workspace\":{\"name\":\"A\",\"items\":[null]}}")]
    [InlineData("not-json")]
    public void Invalid_import_is_a_readable_validation_error(string json) => Assert.Throws<ValidationException>(() => WorkspaceTransfer.Import(json));
    [Theory]
    [InlineData("file:///C:/Windows/cmd.exe")]
    [InlineData("javascript:alert(1)")]
    [InlineData("https://")]
    public void Unsafe_url_schemes_are_rejected(string target) => Assert.Throws<ValidationException>(() => WorkspaceValidator.Validate(new() { Name = "Test", Items = [new() { Name = "URL", Kind = ItemKind.Url, Target = target }] }));
    [Theory]
    [InlineData(ItemKind.File, "C:\\test.cmd")]
    [InlineData(ItemKind.App, "C:\\Windows\\System32\\cmd.exe")]
    [InlineData(ItemKind.App, "relative.exe")]
    [InlineData(ItemKind.Folder, "\\\\server\\share")]
    public void Unsafe_path_types_are_rejected(ItemKind kind, string target) => Assert.Throws<ValidationException>(() => WorkspaceValidator.Validate(new() { Name = "Test", Items = [new() { Name = "Item", Kind = kind, Target = target }] }));
    [Fact] public void Oversized_import_is_rejected() => Assert.Throws<ValidationException>(() => WorkspaceTransfer.Import(new string(' ', WorkspaceTransfer.MaxBytes + 1)));
    [Fact]
    public async Task Approved_command_runs_once_and_disabled_command_never_requires_consent()
    {
        var store = await Store();
        var launcher = new FakeLauncher();
        var consent = new Consent(true);
        var workspace = new Workspace { Name = "Command", Items = [new() { Name = "Approved", Kind = ItemKind.Command, Target = "Get-Date" }] };
        var run = await new LaunchCoordinator(store, launcher, new NullLog()).RunAsync(workspace, consent);
        Assert.True(consent.Called);
        Assert.Single(launcher.Names);
        Assert.Equal(RunStatus.Completed, run.Status);
        var disabledConsent = new Consent(false);
        await new LaunchCoordinator(store, launcher, new NullLog()).RunAsync(workspace with
        {
            Items = [workspace.Items[0] with { Enabled = false }]
        }, disabledConsent);
        Assert.False(disabledConsent.Called);
        Assert.Single(launcher.Names);
    }
    [Fact]
    public async Task Persistence_failure_before_dispatch_stops_the_entire_queue()
    {
        var launcher = new FakeLauncher();
        var store = new FaultingRunStore();
        await Assert.ThrowsAsync<IOException>(() => new LaunchCoordinator(store, launcher, new NullLog()).RunAsync(Sample(), new Consent(true)));
        Assert.Empty(launcher.Names);
        Assert.Equal(RunStatus.Interrupted, store.Last!.Status);
        Assert.All(store.Last.Items, i => Assert.Equal(ItemStatus.Skipped, i.Status));
    }
    [Theory]
    [InlineData("C:\\temp\\run.cmd ")]
    [InlineData("C:\\temp\\run.cmd.")]
    [InlineData("C:\\temp\\normal.txt:run.cmd")]
    [InlineData("C:\\temp\\..\\run.cmd")]
    public void Win32_path_ambiguities_are_rejected(string target) => Assert.Throws<ValidationException>(() => WorkspaceValidator.Validate(new() { Name = "Path", Items = [new() { Name = "File", Kind = ItemKind.File, Target = target }] }));
    [Fact] public void Invalid_accent_is_rejected() => Assert.Throws<ValidationException>(() => WorkspaceValidator.Validate(new() { Name = "Color", Color = "invalid" }));
    private sealed class FaultingRunStore : IRunStore
    {
        private int count; public LaunchRun? Last;
        public Task SaveRunAsync(LaunchRun run, CancellationToken token = default)
        {
            if (++count == 3)
                throw new IOException("Disk full");
            Last = run;
            return Task.CompletedTask;
        }
        public Task<IReadOnlyList<LaunchRun>> HistoryAsync(int limit = 100, CancellationToken token = default) => Task.FromResult<IReadOnlyList<LaunchRun>>([]);
        public Task RecoverInterruptedAsync(CancellationToken token = default) => Task.CompletedTask;
    }
    [Fact]
    public void Expanded_file_targets_cannot_bypass_command_type_guard()
    {
        const string key = "SHIFT_TEST_UNSAFE_TARGET";
        Environment.SetEnvironmentVariable(key, @"C:\Windows\System32\cmd.exe");
        try
        {
            foreach (var kind in new[] { ItemKind.File, ItemKind.App })
                Assert.Throws<ValidationException>(() => WorkspaceValidator.Validate(new() { Name = "Env", Items = [new() { Name = "Shell", Kind = kind, Target = "%" + key + "%" }] }));
        }
        finally { Environment.SetEnvironmentVariable(key, null); }
    }
    [Fact]
    public async Task Stream_import_accepts_UTF8_BOM_and_rejects_overflow_and_bad_encoding()
    {
        var json = System.Text.Encoding.UTF8.GetBytes(WorkspaceTransfer.Export(Sample()));
        using var valid = new MemoryStream([0xEF, 0xBB, 0xBF, .. json]);
        Assert.Equal("Engineering", (await WorkspaceTransfer.ImportAsync(valid)).Name);
        using var oversized = new MemoryStream(new byte[WorkspaceTransfer.MaxBytes + 1]);
        await Assert.ThrowsAsync<ValidationException>(() => WorkspaceTransfer.ImportAsync(oversized));
        using var invalid = new MemoryStream([0xFF, 0xFF]);
        await Assert.ThrowsAsync<ValidationException>(() => WorkspaceTransfer.ImportAsync(invalid));
    }
    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(directory))
            Directory.Delete(directory, true);
    }
    private sealed class Consent(bool allowed) : ICommandConsent
    {
        public bool Called; public Task<bool> ConfirmAsync(IReadOnlyList<WorkspaceItem> commands, CancellationToken token)
        {
            Called = true;
            return Task.FromResult(allowed);
        }
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
    private sealed class FakeLauncher : IItemLauncher
    {
        public List<string> Names { get; } = []; public bool Fail; public Action? After;
        public Task LaunchAsync(WorkspaceItem item, CancellationToken token)
        {
            Names.Add(item.Name);
            After?.Invoke();
            if (Fail && Names.Count == 1)
                throw new IOException("Application missing");
            return Task.CompletedTask;
        }
    }
    private sealed class BlockingLauncher : IItemLauncher
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously); public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task LaunchAsync(WorkspaceItem item, CancellationToken token)
        {
            Started.TrySetResult();
            await Release.Task;
        }
    }
}
