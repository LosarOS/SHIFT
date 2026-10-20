using System.ComponentModel;
using System.Runtime.CompilerServices;
namespace Shift.App.Services;

public sealed class ShellViewModel(SqliteStore store, LaunchCoordinator coordinator) : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    public IReadOnlyList<Workspace> Workspaces { get; private set; } = [];
    public IReadOnlyList<LaunchRun> History { get; private set; } = [];
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
    public Workspace? Selected
    {
        get; set;
    }
    public IEnumerable<Workspace> Filtered => Workspaces.Where(w => (Section != "Favorites" || w.IsFavorite) && (string.IsNullOrWhiteSpace(Query) || w.Name.Contains(Query, StringComparison.CurrentCultureIgnoreCase) || w.Tags.Any(t => t.Contains(Query, StringComparison.CurrentCultureIgnoreCase)) || w.Description.Contains(Query, StringComparison.CurrentCultureIgnoreCase)));
    public async Task RefreshAsync()
    {
        Workspaces = await store.ListAsync();
        History = await store.HistoryAsync();
        if (Selected is not null)
            Selected = Workspaces.FirstOrDefault(w => w.Id == Selected.Id);
        Changed();
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
            return await coordinator.RunAsync(workspace, consent, new Progress<LaunchProgress>(p => { ActiveRun = p.Run; updated(p.Run); }), LaunchCancellation.Token);
        }
        finally { IsLaunching = false; LaunchCancellation.Dispose(); LaunchCancellation = null; await RefreshAsync(); Changed(nameof(IsLaunching)); }
    }
    private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}
