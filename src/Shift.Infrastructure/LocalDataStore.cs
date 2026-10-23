using Shift.Application;
using Shift.Domain;
namespace Shift.Infrastructure;

/// <summary>Serializes SQLite access off the UI thread. Each operation owns and closes its connections.</summary>
public sealed partial class LocalDataStore(string path) : IWorkspaceStore, IRunStore, ISettingsStore, IHistoryQueries, IStudioStore, IDisposable
{
    private readonly SqliteStore inner = new(path);
    private readonly SemaphoreSlim gate = new(1, 1);
    public string DatabasePath => inner.DatabasePath;

    private async Task<T> ExecuteAsync<T>(Func<Task<T>> operation, CancellationToken token)
    {
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            return await Task.Run(operation, token).ConfigureAwait(false);
        }
        finally { gate.Release(); }
    }
    private Task ExecuteAsync(Func<Task> operation, CancellationToken token) => ExecuteAsync(async () => { await operation().ConfigureAwait(false); return true; }, token);

    public Task InitializeAsync(CancellationToken token = default) => ExecuteAsync(() => inner.InitializeAsync(token), token);
    public Task<IReadOnlyList<Workspace>> ListAsync(CancellationToken token = default) => ExecuteAsync(() => inner.ListAsync(token), token);
    public Task SaveAsync(Workspace workspace, CancellationToken token = default) => ExecuteAsync(() => inner.SaveAsync(workspace, token), token);
    public Task DeleteAsync(Guid id, CancellationToken token = default) => ExecuteAsync(() => inner.DeleteAsync(id, token), token);
    public Task SaveRunAsync(LaunchRun run, CancellationToken token = default) => ExecuteAsync(() => inner.SaveRunAsync(run, token), token);
    public Task<IReadOnlyList<LaunchRun>> HistoryAsync(int limit = 100, CancellationToken token = default) => ExecuteAsync(() => inner.HistoryAsync(limit, token), token);
    public Task RecoverInterruptedAsync(CancellationToken token = default) => ExecuteAsync(() => inner.RecoverInterruptedAsync(token), token);
    public Task<AppSettings> LoadSettingsAsync(CancellationToken token = default) => ExecuteAsync(() => inner.LoadSettingsAsync(token), token);
    public Task SaveSettingsAsync(AppSettings settings, CancellationToken token = default) => ExecuteAsync(() => inner.SaveSettingsAsync(settings, token), token);
    public Task<string> BackupAsync(string directory, CancellationToken token = default) => ExecuteAsync(() => inner.BackupAsync(directory, token), token);
    public Task<HistoryPage> QueryHistoryAsync(HistoryFilter filter, CancellationToken token = default) => ExecuteAsync(() => inner.QueryHistoryAsync(filter, token), token);
    public Task<UsageSummary> GetUsageAsync(CancellationToken token = default) => ExecuteAsync(() => inner.GetUsageAsync(token), token);
    public Task ClearHistoryAsync(CancellationToken token = default) => ExecuteAsync(() => inner.ClearHistoryAsync(token), token);
    public Task<RestoreResult> RestoreAsync(string backup, string safetyDirectory, CancellationToken token = default) => ExecuteAsync(() => inner.RestoreAsync(backup, safetyDirectory, token), token);
    public void Dispose() => gate.Dispose(); // Called after the UI and launch queue have stopped.
}
