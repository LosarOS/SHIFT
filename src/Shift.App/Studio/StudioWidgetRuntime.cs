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
    /// <summary>Reads current UI state without resolving conflicts or requiring a database write.</summary>
    public async Task<IReadOnlyList<WidgetRuntimeState>> CaptureLiveStatesAsync(Guid profileId, CancellationToken token = default)
    {
        await operations.WaitAsync(token).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (host is not { } current || current.Exited.IsCompleted) return [];
            IReadOnlyList<WidgetRuntimeState> captured = [];
            await current.InvokeAsync(() => { captured = current.CaptureStates(profileId); return Task.CompletedTask; }).ConfigureAwait(false);
            return captured;
        }
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
            // Let the final action acknowledge its task before ending the dispatcher;
            // its async continuation must not depend on a message pump we just stopped.
            current.RequestExit();
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
    // Acknowledged storage baselines stay separate from live geometry and whole-profile revisions.
    private readonly Dictionary<Guid, WidgetInstance> layoutBaselines = [];
    private readonly HashSet<Guid> layoutConflicts = [];
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
    public void RequestExit() => Post(() => context?.ExitThread());
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
        return WidgetPresentation.AwaitHostOperationAsync(result.Task, Exited);
    }
    public async Task ApplyAsync(DesktopProfile next, IReadOnlyList<WidgetDefinition> definitions,
        IReadOnlyList<MonitorIdentity> topologySnapshot)
    {
        // Input can otherwise change a note after its flush while later database reads
        // await IO. Freeze the old and replacement windows until the hand-off completes.
        var inputSuspensions = windows.Values.Select(w => w.SuspendInput()).ToList();
        try
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
            windows.Clear(); layoutBaselines.Clear(); layoutConflicts.Clear();
            profile = next;
            monitors = topologySnapshot;
            try
            {
                foreach (var instance in next.Widgets.Where(w => w.Visible))
                {
                    var window = CreateWindow(instance, dictionary[instance.DefinitionId], states[instance.Id], false);
                    inputSuspensions.Add(window.SuspendInput());
                    windows.Add(instance.Id, window); layoutBaselines[instance.Id] = instance;
                    await window.RestoreDraftAsync();
                    window.Show();
                }
                countChanged(windows.Count);
                if (windows.Count > 0) scheduler!.Start(); else scheduler!.Stop();
            }
            catch
            {
                foreach (var window in windows.Values) window.CloseOwned();
                windows.Clear(); layoutBaselines.Clear(); layoutConflicts.Clear(); countChanged(0); throw;
            }
        }
        finally
        {
            foreach (var input in inputSuspensions) input.Dispose();
        }
    }
    private WidgetWindow CreateWindow(WidgetInstance instance, WidgetDefinition definition, WidgetRuntimeState? state, bool isPreview)
    {
        var window = new WidgetWindow(instance, definition, state, monitors, language, isPreview, store, log,
            () => edit(instance.Id), text => notify(text));
        window.HideRequested += async (_, _) =>
        {
            await hostGate.WaitAsync();
            IDisposable? input = null;
            try
            {
                if (window.IsDisposed) return;
                input = window.SuspendInput();
                await window.FlushForCloseAsync();
                if (isPreview) { ClosePreview(); return; }
                if (profile is null) return;
                var latest = (await store.ListProfilesAsync()).FirstOrDefault(p => p.Id == profile.Id) ?? throw new InvalidOperationException("The active profile was deleted.");
                profile = await store.SaveProfileAsync(latest with { Widgets = latest.Widgets.Select(w => w.Id == instance.Id ? w with { Visible = false } : w).ToArray() });
                windows.Remove(instance.Id); layoutBaselines.Remove(instance.Id); layoutConflicts.Remove(instance.Id);
                window.CloseOwned(); countChanged(windows.Count);
                if (windows.Count == 0 && preview is null) scheduler?.Stop();
            }
            catch (Exception ex) { if (!window.IsDisposed) window.ShowError(ex); log.Error("Widget could not be hidden safely", ex); }
            finally { input?.Dispose(); hostGate.Release(); }
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
        var input = windows.Values.Select(w => w.SuspendInput()).ToArray();
        try
        {
            await SaveLayoutsAsync();
            var latest = (await store.ListProfilesAsync()).FirstOrDefault(p => p.Id == profile.Id)
                ?? throw new InvalidOperationException("The active profile was deleted.");
            // A global mode command is explicit, but it must not overwrite a blocked
            // pending geometry edit. Flush compatible edits before taking this snapshot.
            profile = await store.SaveProfileAsync(WidgetPresentation.MergeInteraction(latest, interaction,
                new Dictionary<Guid, WidgetLayout>()));
            foreach (var window in windows.Values)
            {
                window.SetInteraction(interaction, persisted: true);
                var saved = profile.Widgets.FirstOrDefault(w => w.Id == window.Instance.Id);
                // Persisting a mode does not apply editor geometry to the live window.
                // Never silently rebase a clean old window onto that unseen placement.
                if (saved is null || !layoutBaselines.TryGetValue(window.Instance.Id, out var acknowledged)
                    || !WidgetPresentation.CanAcknowledgeModeChange(acknowledged, saved))
                    MarkLayoutConflict(window.Instance.Id);
                else if (!layoutConflicts.Contains(saved.Id)) layoutBaselines[saved.Id] = saved;
            }
        }
        finally { foreach (var lease in input) lease.Dispose(); }
    }
    public async Task GatherAsync(IReadOnlyList<MonitorIdentity> topologySnapshot)
    {
        if (topologySnapshot.Count == 0) throw new InvalidOperationException("No display is available.");
        if (profile is null) return;
        // InvokeAsync already owns hostGate. Only input is leased here; re-acquiring
        // the non-reentrant gate would deadlock.
        var input = windows.Values.Select(w => w.SuspendInput()).ToArray();
        try
        {
            var latest = (await store.ListProfilesAsync()).FirstOrDefault(p => p.Id == profile.Id)
                ?? throw new InvalidOperationException("The active profile was deleted.");
            monitors = topologySnapshot;
            var primary = monitors.FirstOrDefault(m => m.Primary) ?? monitors[0];
            var edits = new List<StudioLayoutEdit>();
            var presented = new Dictionary<Guid, WidgetLayout>();
            var index = 0;
            foreach (var window in windows.Values)
            {
                // Gather changes position/monitor only. Take dimensions, topmost and
                // mode from the current stored instance, never the stale live window.
                var saved = latest.Widgets.FirstOrDefault(w => w.Id == window.Instance.Id);
                if (saved is null || saved.DefinitionId != window.Instance.DefinitionId || !saved.Visible)
                { MarkLayoutConflict(window.Instance.Id); continue; }
                var target = WidgetPresentation.GatherPlacement(saved, primary.DeviceId, index++);
                layoutBaselines[saved.Id] = saved; layoutConflicts.Remove(saved.Id); window.SetLayoutConflict(false);
                window.SetInteraction(target.Interaction, persisted: true);
                window.Reposition(target.Layout, monitors, persist: true);
                presented[saved.Id] = window.CaptureLayout();
                edits.Add(new(saved, target.Layout, target.Interaction));
            }
            if (edits.Count > 0)
            {
                var result = await store.SaveLayoutEditsAsync(profile.Id, edits);
                AcknowledgeLayouts(result, presented);
            }
        }
        finally { foreach (var lease in input) lease.Dispose(); }
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
        if (profile is null) return;
        var edits = windows.Values.Where(w => w.LayoutDirty && !layoutConflicts.Contains(w.Instance.Id))
            .Select(w => new StudioLayoutEdit(layoutBaselines[w.Instance.Id], w.CaptureLayout(), w.Instance.Interaction)).ToArray();
        if (edits.Length == 0) return;
        // Atomic per-instance compare/write handles unrelated profile metadata/editor
        // changes without a stale whole-profile save or a blind retry.
        var result = await store.SaveLayoutEditsAsync(profile.Id, edits);
        AcknowledgeLayouts(result);
    }
    private void AcknowledgeLayouts(StudioLayoutWriteResult result, IReadOnlyDictionary<Guid, WidgetLayout>? presented = null)
    {
        foreach (var saved in result.Saved)
            if (windows.TryGetValue(saved.Id, out var window))
            {
                layoutBaselines[saved.Id] = saved;
                if (presented is not null && presented.TryGetValue(saved.Id, out var display))
                    window.LayoutSaved(saved, display);
                else window.LayoutSaved(saved);
                window.SetLayoutConflict(false);
            }
        foreach (var id in result.Conflicts) MarkLayoutConflict(id);
    }
    private void MarkLayoutConflict(Guid id)
    {
        if (!windows.TryGetValue(id, out var window)) return;
        window.SetLayoutConflict(true);
        if (layoutConflicts.Add(id))
            notify(language == "en"
                ? "A widget placement changed in SHIFT. Live geometry was not saved over it; reapply the profile or gather widgets."
                : "Положение виджета изменено в SHIFT. Новое оформление не перезаписано; примените профиль заново или соберите виджеты.");
    }
    public IReadOnlyList<WidgetRuntimeState> CaptureStates(Guid profileId)
        => profile?.Id == profileId ? windows.Values.Where(w => !w.IsDisposed).Select(w => w.CaptureState()).ToArray() : [];
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
        var inputSuspensions = windows.Values.Select(w => w.SuspendInput()).ToArray();
        try
        {
            try { await FlushAsync(); }
            catch { stopping = false; scheduler?.Start(); throw; }
            ClosePreview();
            foreach (var window in windows.Values) window.CloseOwned();
            windows.Clear(); layoutBaselines.Clear(); layoutConflicts.Clear(); countChanged(0);
        }
        finally { foreach (var input in inputSuspensions) input.Dispose(); }
    }
}
