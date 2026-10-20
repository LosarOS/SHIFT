using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Shift.Application;
using Shift.Domain;
using Shift.Infrastructure;

// Synthetic, isolated data only. This tool never opens another app, URL or user's SHIFT database.
var directory = Path.Combine(Path.GetTempPath(), "shift-diagnostics-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(directory);
var metrics = new Dictionary<string, object>
{
    ["platform"] = RuntimeInformation.OSDescription,
    ["architecture"] = RuntimeInformation.ProcessArchitecture.ToString(),
    ["framework"] = RuntimeInformation.FrameworkDescription,
    ["timestampUtc"] = DateTimeOffset.UtcNow,
    ["note"] = "Core storage/queue harness only; these results do not measure WinUI startup, rendering or Windows dispatch."
};
try
{
    using var store = new LocalDataStore(Path.Combine(directory, "shift.db"));
    await store.InitializeAsync();
    var spaces = Enumerable.Range(0, 100).Select(index => new Workspace { Name = $"Workspace {index:000}", Tags = ["synthetic"], Items = Enumerable.Range(0, 20).Select(item => new WorkspaceItem { Name = $"Item {item:00}", Kind = ItemKind.Url, Target = "https://example.invalid/", Order = item }).ToArray() }).ToArray();
    var clock = Stopwatch.StartNew();
    foreach (var space in spaces)
        await store.SaveAsync(space);
    metrics["save100WorkspacesWith20ItemsMs"] = clock.Elapsed.TotalMilliseconds;
    clock.Restart();
    var loaded = await store.ListAsync();
    metrics["read100WorkspacesWith20ItemsMs"] = clock.Elapsed.TotalMilliseconds;
    if (loaded.Count != 100 || loaded.Sum(w => w.Items.Count) != 2000)
        throw new InvalidDataException("Workspace roundtrip failed.");
    clock.Restart();
    for (var index = 0; index < 1000; index++)
    {
        var space = spaces[index % spaces.Length];
        var start = DateTimeOffset.UtcNow.AddMinutes(-index);
        await store.SaveRunAsync(new(Guid.NewGuid(), space.Id, space.Name, start, start.AddSeconds(2), RunStatus.Completed,
            space.Items.Select(i => new LaunchRunItem(i.Id, i.Name, i.Kind, i.Order, ItemStatus.Success, StartedAt: start, FinishedAt: start.AddSeconds(2))).ToArray()));
    }
    metrics["save1000RunsWith20ItemsMs"] = clock.Elapsed.TotalMilliseconds;
    clock.Restart();
    var page = await store.QueryHistoryAsync(new(PageSize: 30));
    metrics["historyPage30Ms"] = clock.Elapsed.TotalMilliseconds;
    clock.Restart();
    var usage = await store.GetUsageAsync();
    metrics["usageFor1000RunsMs"] = clock.Elapsed.TotalMilliseconds;
    if (page.TotalCount != 1000 || usage.TotalRuns != 1000)
        throw new InvalidDataException("History/usage count mismatch.");
    clock.Restart();
    var backup = await store.BackupAsync(Path.Combine(directory, "backups"));
    metrics["onlineBackupMs"] = clock.Elapsed.TotalMilliseconds;
    var launcher = new SyntheticLauncher();
    clock.Restart();
    var run = await new LaunchCoordinator(store, launcher, new DiagnosticLog()).RunAsync(spaces[0], new RejectCommands());
    metrics["dispatch20SyntheticItemsAndPersistProgressMs"] = clock.Elapsed.TotalMilliseconds;
    if (run.Status != RunStatus.Completed || launcher.Count != 20)
        throw new InvalidDataException("Queue regression.");
    metrics["databaseBytes"] = new FileInfo(store.DatabasePath).Length;
    metrics["backupBytes"] = new FileInfo(backup).Length;
    metrics["harnessWorkingSetMiB"] = Process.GetCurrentProcess().WorkingSet64 / (1024.0 * 1024.0);
    var json = JsonSerializer.Serialize(metrics, new JsonSerializerOptions { WriteIndented = true });
    Console.WriteLine(json);
    if (args.Length == 2 && args[0] == "--output")
    {
        var output = Path.GetFullPath(args[1]);
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        await File.WriteAllTextAsync(output, json + Environment.NewLine);
    }
}
finally { SqliteConnection.ClearAllPools(); Directory.Delete(directory, true); }
sealed class SyntheticLauncher : IItemLauncher
{
    public int Count; public Task LaunchAsync(WorkspaceItem item, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        Count++;
        return Task.CompletedTask;
    }
}
sealed class RejectCommands : ICommandConsent
{
    public Task<bool> ConfirmAsync(IReadOnlyList<WorkspaceItem> commands, CancellationToken token) => Task.FromResult(false);
}
sealed class DiagnosticLog : IAppLog
{
    public void Info(string message)
    {
    }
    public void Error(string message, Exception exception) => Console.Error.WriteLine($"{message} {exception.GetType().Name}");
}
