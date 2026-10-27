using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Forms = System.Windows.Forms;

namespace Shift.App.Studio;

/// <summary>Owns ordinary managed tool windows on one STA. Never parents windows into Explorer.</summary>
public sealed class StudioWidgetRuntime : IWidgetRuntime
{
    private readonly IStudioStore store;
    private readonly IMonitorTopology topology;
    private readonly ISystemMetricsProvider metrics;
    private readonly IAppLog log;
    private readonly SemaphoreSlim operations = new(1, 1);
    private WidgetHost? host;
    private bool disposed;
    private int windowCount;
    private string language = "ru";
    public string Language { get => language; set { language = value == "en" ? "en" : "ru"; if (host is { } h) h.Post(() => h.SetLanguage(language)); } }
    public int WindowCount => Volatile.Read(ref windowCount);
    public event Action<Guid>? EditRequested;
    public event Action<string>? NotificationRequested;
    public event Action<Exception>? RuntimeFaulted;
    public StudioWidgetRuntime(IStudioStore store, IMonitorTopology topology, ISystemMetricsProvider metrics, IAppLog log)
    { this.store = store; this.topology = topology; this.metrics = metrics; this.log = log; }

    public async Task ApplyAsync(DesktopProfile profile, IReadOnlyList<WidgetDefinition> definitions, CancellationToken token = default)
    {
        await operations.WaitAsync(token).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (profile.Widgets.Count > 64 || profile.Widgets.Select(x => x.Id).Distinct().Count() != profile.Widgets.Count)
                throw new InvalidDataException("Invalid widget instance list.");
            var monitors = await topology.GetMonitorsAsync(token).ConfigureAwait(false);
            if (monitors.Count == 0 && profile.Widgets.Any(w => w.Visible)) throw new InvalidOperationException("No display is available.");
            var current = await EnsureHostAsync().ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            await current.InvokeAsync(() => current.ApplyAsync(profile, definitions, monitors)).ConfigureAwait(false);
        }
        finally { operations.Release(); }
    }

    public async Task PreviewAsync(WidgetDefinition draft, WidgetLayout layout, CancellationToken token = default)
    {
        await operations.WaitAsync(token).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            var monitors = await topology.GetMonitorsAsync(token).ConfigureAwait(false);
            if (monitors.Count == 0) throw new InvalidOperationException("No display is available.");
            var current = await EnsureHostAsync().ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            await current.InvokeAsync(() => current.PreviewAsync(draft, layout, monitors)).ConfigureAwait(false);
        }
        finally { operations.Release(); }
    }
    public async Task ClosePreviewAsync(CancellationToken token = default)
    {
        await operations.WaitAsync(token).ConfigureAwait(false);
        try { if (host is { } current) await current.InvokeAsync(() => { current.ClosePreview(); return Task.CompletedTask; }).ConfigureAwait(false); }
        finally { operations.Release(); }
    }
    public async Task SetInteractionAsync(WidgetInteraction interaction, CancellationToken token = default)
    {
        if (!Enum.IsDefined(interaction)) throw new ArgumentOutOfRangeException(nameof(interaction));
        await operations.WaitAsync(token).ConfigureAwait(false);
        try { if (host is { } current) await current.InvokeAsync(() => current.SetInteractionAsync(interaction)).ConfigureAwait(false); }
        finally { operations.Release(); }
    }
    public async Task GatherAsync(CancellationToken token = default)
    {
        await operations.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (host is not { } current) return;
            var monitors = await topology.GetMonitorsAsync(token).ConfigureAwait(false);
            await current.InvokeAsync(() => current.GatherAsync(monitors)).ConfigureAwait(false);
        }
        finally { operations.Release(); }
    }
    public async Task FlushPendingAsync(CancellationToken token = default)
    {
        await operations.WaitAsync(token).ConfigureAwait(false);
        try { if (host is { } current && !current.Exited.IsCompleted) await current.InvokeAsync(current.FlushAsync).ConfigureAwait(false); }
        finally { operations.Release(); }
    }
    public async Task StopAsync(CancellationToken token = default)
    {
        await operations.WaitAsync(token).ConfigureAwait(false);
        try { await CloseHostAsync().ConfigureAwait(false); }
        finally { operations.Release(); }
    }
    private async Task CloseHostAsync()
    {
        if (host is not { } current) return;
        if (!current.Exited.IsCompleted)
        {
            // If both SQLite and the recovery directory are unavailable, keep the unsaved
            // window visible rather than silently discard text. The caller displays the error.
            await current.InvokeAsync(current.StopAsync).ConfigureAwait(false);
            await current.Exited.ConfigureAwait(false);
        }
        host = null;
        Volatile.Write(ref windowCount, 0);
    }
    public async ValueTask DisposeAsync()
    {
        await operations.WaitAsync().ConfigureAwait(false);
        try
        {
            if (disposed) return;
            await CloseHostAsync().ConfigureAwait(false);
            disposed = true;
        }
        finally { operations.Release(); }
        // No WaitHandle is allocated by this gate. Do not dispose it while a concurrently
        // queued operation may still be waiting; subsequent calls remain safely idempotent.
    }
    private async Task<WidgetHost> EnsureHostAsync()
    {
        if (host is { } existing && !existing.Exited.IsCompleted) return existing;
        var created = new WidgetHost(store, topology, metrics, log, language,
            count => Volatile.Write(ref windowCount, count), id => EditRequested?.Invoke(id),
            text => NotificationRequested?.Invoke(text), ex => { log.Error("Studio widget runtime failed", ex); RuntimeFaulted?.Invoke(ex); });
        await created.StartAsync().ConfigureAwait(false);
        host = created;
        return created;
    }
}

internal sealed class WidgetHost
{
    private readonly IStudioStore store;
    private readonly IMonitorTopology topology;
    private readonly ISystemMetricsProvider metrics;
    private readonly IAppLog log;
    private readonly Action<int> countChanged;
    private readonly Action<Guid> edit;
    private readonly Action<string> notify;
    private readonly Action<Exception> fault;
    private readonly Dictionary<Guid, WidgetWindow> windows = [];
    private readonly SemaphoreSlim hostGate = new(1, 1);
    private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource exited = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Forms.Control? dispatcher;
    private Forms.ApplicationContext? context;
    private Forms.Timer? scheduler;
    private DesktopProfile? profile;
    private WidgetWindow? preview;
    private IReadOnlyList<MonitorIdentity> monitors = [];
    private bool ticking;
    private bool stopping;
    private int failures;
    private string language;
    private bool topologyDirty = true;
    public Task Exited => exited.Task;
    public WidgetHost(IStudioStore store, IMonitorTopology topology, ISystemMetricsProvider metrics, IAppLog log,
        string language, Action<int> countChanged, Action<Guid> edit, Action<string> notify, Action<Exception> fault)
    { this.store = store; this.topology = topology; this.metrics = metrics; this.log = log; this.language = language; this.countChanged = countChanged; this.edit = edit; this.notify = notify; this.fault = fault; }
    public Task StartAsync()
    {
        var thread = new Thread(() =>
        {
            try
            {
                // Per-thread error handling does not change WinUI's main-thread exception policy.
                Forms.Application.SetUnhandledExceptionMode(Forms.UnhandledExceptionMode.CatchException, true);
                System.Threading.ThreadExceptionEventHandler handler = (_, e) => fault(e.Exception);
                Forms.Application.ThreadException += handler;
                EventHandler displayChanged = (_, _) => Post(() => topologyDirty = true);
                Microsoft.Win32.PowerModeChangedEventHandler powerChanged = (_, _) => Post(() => topologyDirty = true);
                Microsoft.Win32.SystemEvents.DisplaySettingsChanged += displayChanged;
                Microsoft.Win32.SystemEvents.PowerModeChanged += powerChanged;
                try
                {
                    using var control = new Forms.Control();
                    _ = control.Handle;
                    dispatcher = control;
                    context = new Forms.ApplicationContext();
                    scheduler = new Forms.Timer { Interval = 1000 };
                    scheduler.Tick += Tick;
                    ready.TrySetResult();
                    Forms.Application.Run(context);
                }
                finally
                {
                    Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= displayChanged;
                    Microsoft.Win32.SystemEvents.PowerModeChanged -= powerChanged;
                    Forms.Application.ThreadException -= handler; scheduler?.Dispose(); context?.Dispose();
                }
            }
            catch (Exception ex) { ready.TrySetException(ex); fault(ex); }
            finally { exited.TrySetResult(); }
        }) { IsBackground = true, Name = "SHIFT Studio widgets" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return ready.Task;
    }
    public void Post(Action action)
    {
        var control = dispatcher;
        if (control is null || control.IsDisposed) return;
        try { control.BeginInvoke(action); } catch (InvalidOperationException) { }
    }
    public Task InvokeAsync(Func<Task> action)
    {
        if (Exited.IsCompleted) return Task.FromException(new InvalidOperationException("Widget host has stopped."));
        var result = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            dispatcher!.BeginInvoke(new Action(async () =>
            {
                try { await hostGate.WaitAsync(); try { await action(); } finally { hostGate.Release(); } result.TrySetResult(); }
                catch (Exception ex) { result.TrySetException(ex); }
            }));
        }
        catch (Exception ex) { result.TrySetException(ex); }
        return result.Task;
    }
    public async Task ApplyAsync(DesktopProfile next, IReadOnlyList<WidgetDefinition> definitions,
        IReadOnlyList<MonitorIdentity> topologySnapshot)
    {
        await FlushAsync();
        // Read after flush: reapplying must not recreate a note from the pre-flush revision.
        var states = new Dictionary<Guid, WidgetRuntimeState?>();
        foreach (var instance in next.Widgets.Where(w => w.Visible))
            states[instance.Id] = await store.GetWidgetStateAsync(instance.Id);
        var dictionary = definitions.ToDictionary(d => d.Id);
        foreach (var instance in next.Widgets.Where(w => w.Visible))
        {
            if (!dictionary.TryGetValue(instance.DefinitionId, out var definition)) throw new InvalidDataException("Widget definition is missing.");
            WidgetWindow.Validate(definition, instance.Layout);
        }
        ClosePreview();
        foreach (var window in windows.Values) window.CloseOwned();
        windows.Clear();
        profile = next;
        monitors = topologySnapshot;
        try
        {
            foreach (var instance in next.Widgets.Where(w => w.Visible))
            {
                var window = CreateWindow(instance, dictionary[instance.DefinitionId], states[instance.Id], false);
                windows.Add(instance.Id, window);
                await window.RestoreDraftAsync();
                window.Show();
            }
            countChanged(windows.Count);
            if (windows.Count > 0) scheduler!.Start(); else scheduler!.Stop();
        }
        catch
        {
            foreach (var window in windows.Values) window.CloseOwned();
            windows.Clear(); countChanged(0); throw;
        }
    }
    private WidgetWindow CreateWindow(WidgetInstance instance, WidgetDefinition definition, WidgetRuntimeState? state, bool isPreview)
    {
        var window = new WidgetWindow(instance, definition, state, monitors, language, isPreview, store, log,
            () => edit(instance.Id), text => notify(text));
        window.HideRequested += async (_, _) =>
        {
            await hostGate.WaitAsync();
            try
            {
                if (window.IsDisposed) return;
                await window.FlushForCloseAsync();
                if (isPreview) { ClosePreview(); return; }
                if (profile is null) return;
                var latest = (await store.ListProfilesAsync()).FirstOrDefault(p => p.Id == profile.Id) ?? throw new InvalidOperationException("The active profile was deleted.");
                profile = await store.SaveProfileAsync(latest with { Widgets = latest.Widgets.Select(w => w.Id == instance.Id ? w with { Visible = false } : w).ToArray() });
                windows.Remove(instance.Id); window.CloseOwned(); countChanged(windows.Count);
                if (windows.Count == 0 && preview is null) scheduler?.Stop();
            }
            catch (Exception ex) { if (!window.IsDisposed) window.ShowError(ex); log.Error("Widget could not be hidden safely", ex); }
            finally { hostGate.Release(); }
        };
        return window;
    }
    public Task PreviewAsync(WidgetDefinition definition, WidgetLayout layout, IReadOnlyList<MonitorIdentity> topologySnapshot)
    {
        WidgetWindow.Validate(definition, layout);
        ClosePreview(); monitors = topologySnapshot;
        preview = CreateWindow(new WidgetInstance { DefinitionId = definition.Id, Layout = layout, Interaction = WidgetInteraction.Edit }, definition, null, true);
        preview.Show(); scheduler!.Start(); return Task.CompletedTask;
    }
    public void ClosePreview() { preview?.CloseOwned(); preview = null; if (windows.Count == 0) scheduler?.Stop(); }
    public void SetLanguage(string value)
    { language = value; foreach (var window in windows.Values) window.SetLanguage(value); preview?.SetLanguage(value); }
    public async Task SetInteractionAsync(WidgetInteraction interaction)
    {
        if (profile is null) return;
        var latest = (await store.ListProfilesAsync()).FirstOrDefault(p => p.Id == profile.Id) ?? throw new InvalidOperationException("The active profile was deleted.");
        // A mode switch may arrive between drag/resize and the scheduler's layout save.
        // Merge those live edits into the latest record before acknowledging them as saved.
        var dirtyLayouts = windows.Values.Where(w => w.LayoutDirty).ToDictionary(w => w.Instance.Id, w => w.CaptureLayout());
        profile = await store.SaveProfileAsync(WidgetPresentation.MergeInteraction(latest, interaction, dirtyLayouts));
        foreach (var window in windows.Values) window.SetInteraction(interaction, persisted: true);
        foreach (var saved in WidgetPresentation.WrittenInstances(profile, dirtyLayouts.Keys))
            if (windows.TryGetValue(saved.Id, out var window)) window.LayoutSaved(saved);
    }
    public async Task GatherAsync(IReadOnlyList<MonitorIdentity> topologySnapshot)
    {
        if (topologySnapshot.Count == 0) throw new InvalidOperationException("No display is available.");
        monitors = topologySnapshot;
        var primary = monitors.FirstOrDefault(m => m.Primary) ?? monitors[0];
        var index = 0;
        foreach (var window in windows.Values)
        {
            var layout = window.Instance.Layout with { MonitorId = primary.DeviceId, X = .04 + (index % 4) * .05, Y = .04 + (index % 4) * .05 };
            window.Reposition(layout, monitors, persist: true); index++;
        }
        await SaveLayoutsAsync();
    }
    private async void Tick(object? sender, EventArgs args)
    {
        if (ticking || stopping) return;
        ticking = true;
        if (!await hostGate.WaitAsync(0)) { ticking = false; return; }
        try
        {
            var now = DateTimeOffset.UtcNow;
            if (topologyDirty)
            {
                topologyDirty = false;
                var current = await topology.GetMonitorsAsync();
                if (!SameTopology(monitors, current) && current.Count > 0)
                {
                    monitors = current;
                    foreach (var window in windows.Values) window.Reposition(window.Instance.Layout, monitors);
                }
            }
            var currentWindows = windows.Values.Concat(preview is null ? [] : new[] { preview }).ToArray();
            SystemMetrics? sample = null;
            if (currentWindows.Any(w => w.Visible && w.MetricsDue(now))) sample = await Task.Run(metrics.Sample);
            foreach (var window in currentWindows)
            {
                if (!window.IsDisposed) await window.TickAsync(now, sample);
            }
            if (windows.Values.Any(w => w.LayoutDirty)) await SaveLayoutsAsync();
            failures = 0;
        }
        catch (Exception ex)
        {
            log.Error("Widget update failed", ex);
            if (++failures >= 3) { scheduler!.Stop(); fault(ex); }
        }
        finally { hostGate.Release(); ticking = false; }
    }
    private static bool SameTopology(IReadOnlyList<MonitorIdentity> a, IReadOnlyList<MonitorIdentity> b)
        => a.Count == b.Count && a.Zip(b).All(p => p.First == p.Second);
    private async Task SaveLayoutsAsync()
    {
        if (profile is null || !windows.Values.Any(w => w.LayoutDirty)) return;
        var latest = (await store.ListProfilesAsync()).FirstOrDefault(p => p.Id == profile.Id)
            ?? throw new InvalidOperationException("The active profile was deleted. Disable Studio to close its windows.");
        // Snapshot the actual write set before awaiting storage. A clean fallback window
        // must not be acknowledged against a preferred monitor/size that is temporarily absent.
        var edits = windows.Values.Where(w => w.LayoutDirty).ToDictionary(w => w.Instance.Id,
            w => w.Instance with { Layout = w.CaptureLayout() });
        var saved = await store.SaveProfileAsync(WidgetPresentation.MergeLayoutEdits(latest, edits));
        profile = saved;
        foreach (var instance in WidgetPresentation.WrittenInstances(saved, edits.Keys))
            if (windows.TryGetValue(instance.Id, out var window)) window.LayoutSaved(instance);
    }
    public async Task FlushAsync()
    {
        foreach (var window in windows.Values.ToArray()) await window.FlushForCloseAsync();
        try { await SaveLayoutsAsync(); }
        catch (Exception ex)
        {
            // Geometry failure must not strand windows when the user disables Studio.
            // The previously saved layout remains intact; current notes have a durable draft.
            log.Error("Latest widget placement could not be saved", ex);
            notify(language == "en" ? "Latest widget position was not saved; previous layout retained." : "Новое положение виджета не сохранено; прежняя схема сохранена.");
        }
    }
    public async Task StopAsync()
    {
        stopping = true; scheduler?.Stop();
        try { await FlushAsync(); }
        catch { stopping = false; scheduler?.Start(); throw; }
        ClosePreview();
        foreach (var window in windows.Values) window.CloseOwned();
        windows.Clear(); countChanged(0);
        context!.ExitThread();
    }
}
