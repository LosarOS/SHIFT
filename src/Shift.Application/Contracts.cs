using Shift.Domain;
namespace Shift.Application;

public interface IWorkspaceStore
{
    Task<IReadOnlyList<Workspace>> ListAsync(CancellationToken token = default);
    Task SaveAsync(Workspace workspace, CancellationToken token = default);
    Task DeleteAsync(Guid id, CancellationToken token = default);
}
public interface IRunStore
{
    Task SaveRunAsync(LaunchRun run, CancellationToken token = default);
    Task<IReadOnlyList<LaunchRun>> HistoryAsync(int limit = 100, CancellationToken token = default);
    Task RecoverInterruptedAsync(CancellationToken token = default);
}
public interface ISettingsStore
{
    Task<AppSettings> LoadSettingsAsync(CancellationToken token = default);
    Task SaveSettingsAsync(AppSettings settings, CancellationToken token = default);
}
public interface IItemLauncher
{
    Task LaunchAsync(WorkspaceItem item, CancellationToken token);
}
public interface ICommandConsent
{
    Task<bool> ConfirmAsync(IReadOnlyList<WorkspaceItem> commands, CancellationToken token);
}
public interface IAppLog
{
    void Info(string message);
    void Error(string message, Exception exception);
}
public sealed record LaunchProgress(LaunchRun Run);

public interface IHistoryQueries
{
    Task<HistoryPage> QueryHistoryAsync(HistoryFilter filter, CancellationToken token = default);
    Task<UsageSummary> GetUsageAsync(CancellationToken token = default);
    Task ClearHistoryAsync(CancellationToken token = default);
}
