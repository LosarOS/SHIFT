using System.ComponentModel;
using System.Runtime.CompilerServices;
namespace Shift.App.Services;

public enum WorkspaceSort
{
    Recent, Name, MostUsed
}
public sealed class ShellViewModel(LocalDataStore store, LaunchCoordinator coordinator) : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    public IReadOnlyList<Workspace> Workspaces { get; private set; } = [];
    public HistoryPage HistoryPage { get; private set; } = new([], 0, 0, 30);
    public IReadOnlyList<LaunchRun> History => HistoryPage.Runs;
    public IReadOnlyList<LaunchRun> RecentRuns { get; private set; } = [];
    public HistoryFilter HistoryFilter { get; set; } = new();
    public UsageSummary Usage { get; private set; } = UsageSummary.Empty;
    public AppSettings Settings { get; set; } = new();
    public LaunchRun? ActiveRun
    {
        get; private set;
    }
    public bool IsLaunching
    {
        get; private set;
    }
    public CancellationTokenSource? LaunchCancellation
    {
        get; private set;
    }
    public string Section { get; set; } = "Overview";
    public string Query { get; set; } = "";
    public string TagFilter { get; set; } = "";
    public WorkspaceSort Sort
    {
        get; set;
    }
    public Workspace? Selected
    {
        get; set;
    }
    public IEnumerable<Workspace> Filtered
    {
        get
        {
            var terms = Query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var filtered = Workspaces.Where(w => (Section != "Favorites" || w.IsFavorite)
                && (TagFilter.Length == 0 || w.Tags.Contains(TagFilter, StringComparer.OrdinalIgnoreCase))
                && terms.All(term => w.Name.Contains(term, StringComparison.CurrentCultureIgnoreCase)
                    || w.Description.Contains(term, StringComparison.CurrentCultureIgnoreCase)
                    || w.Tags.Any(t => t.Contains(term, StringComparison.CurrentCultureIgnoreCase))));
            return Sort switch
            {
                WorkspaceSort.Name => filtered.OrderBy(w => w.Name, StringComparer.CurrentCultureIgnoreCase),
                WorkspaceSort.MostUsed => filtered.OrderByDescending(w => Usage.Workspaces.FirstOrDefault(u => u.WorkspaceId == w.Id)?.LaunchCount ?? 0).ThenBy(w => w.Name),
                _ => filtered.OrderByDescending(w => w.IsFavorite).ThenByDescending(w => Usage.Workspaces.FirstOrDefault(u => u.WorkspaceId == w.Id)?.LastLaunchedAt ?? w.UpdatedAt)
            };
        }
    }
    public async Task RefreshAsync()
    {
        Workspaces = await store.ListAsync();
        Usage = await store.GetUsageAsync();
        RecentRuns = await store.HistoryAsync(3);
        await RefreshHistoryAsync();
        if (Selected is not null)
            Selected = Workspaces.FirstOrDefault(w => w.Id == Selected.Id);
        Changed();
    }
    public async Task RefreshHistoryAsync()
    {
        HistoryPage = await store.QueryHistoryAsync(HistoryFilter);
        if (HistoryPage.Runs.Count == 0 && HistoryFilter.Offset > 0)
        {
            HistoryFilter = HistoryFilter with
            {
                Offset = 0
            };
            HistoryPage = await store.QueryHistoryAsync(HistoryFilter);
        }
        Changed(nameof(History));
    }
    public async Task SaveAsync(Workspace workspace)
    {
        await store.SaveAsync(workspace);
        await RefreshAsync();
    }
    public async Task DeleteAsync(Workspace workspace)
    {
        await store.DeleteAsync(workspace.Id);
        Selected = null;
        await RefreshAsync();
    }
    public async Task<LaunchRun> LaunchAsync(Workspace workspace, ICommandConsent consent, Action<LaunchRun> updated)
    {
        if (IsLaunching)
            throw new InvalidOperationException("A launch is already in progress.");
        IsLaunching = true;
        ActiveRun = null;
        LaunchCancellation = new();
        Changed(nameof(IsLaunching));
        try
        {
            // Coordinator awaits capture the caller's UI context. Inline progress avoids stale queued
            // callbacks overwriting the final result or a newer launch.
            var run = await coordinator.RunAsync(workspace, consent, new InlineProgress<LaunchProgress>(p => { ActiveRun = p.Run; updated(p.Run); }), LaunchCancellation.Token);
            ActiveRun = run;
            return run;
        }
        finally
        {
            IsLaunching = false;
            LaunchCancellation.Dispose();
            LaunchCancellation = null;
            try
            {
                await RefreshAsync();
            }
            finally { Changed(nameof(IsLaunching)); }
        }
    }
    private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
    private sealed class InlineProgress<T>(Action<T> action) : IProgress<T>
    {
        public void Report(T value) => action(value);
    }
}
