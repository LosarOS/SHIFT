using System.Text.Json;
using Microsoft.Data.Sqlite;
using Shift.Application;
using Shift.Domain;
using Shift.Infrastructure;
using Xunit;

namespace Shift.Tests;

public class LaunchScenarioTests
{
    private static Workspace Sample() => new() { Name = "Сценарий", Items = [
        new() { Name = "Missing", Kind = ItemKind.Url, Target = "https://one.example", Order = 0 },
        new() { Name = "Ready", Kind = ItemKind.Url, Target = "https://two.example", Order = 1 },
        new() { Name = "Command", Kind = ItemKind.Command, Target = "echo local", Order = 2 }] };
    private static DoctorReport Report(Workspace w) => new(w.Id, DateTimeOffset.UtcNow,
        w.Items.Select((i, n) => new DoctorItemResult(i.Id, i.Name, n == 0 ? DoctorState.Error : DoctorState.Warning,
            [new(n == 0 ? DoctorState.Error : DoctorState.Warning, n == 0 ? "Doctor.PathMissing" : "Doctor.CommandConsent")])).ToArray());

    [Theory]
    [InlineData(PreflightPolicy.Off, 0, 3)]
    [InlineData(PreflightPolicy.Continue, 1, 3)]
    [InlineData(PreflightPolicy.StopOnErrors, 1, 0)]
    [InlineData(PreflightPolicy.SkipErrors, 1, 2)]
    public async Task Explicit_preflight_rules_control_dispatch(PreflightPolicy policy, int checks, int dispatches)
    {
        var w = Sample(); var doctor = new Doctor(w); var launcher = new Launcher(); var store = new Store(); var consent = new Consent();
        var run = await new LaunchCoordinator(store, launcher, new Log()).RunAsync(w, consent,
            scenario: new() { Preflight = policy }, doctor: doctor);
        Assert.Equal(checks, doctor.Calls); Assert.Equal(dispatches, launcher.Items.Count);
        Assert.Equal(policy != PreflightPolicy.StopOnErrors, consent.Called);
        Assert.NotNull(run.FinishedAt); Assert.All(run.Items, i => Assert.NotEqual(ItemStatus.Pending, i.Status));
        Assert.Equal(policy is PreflightPolicy.StopOnErrors or PreflightPolicy.SkipErrors ? RunStatus.PartialFailure : RunStatus.Completed, run.Status);
        if (policy == PreflightPolicy.StopOnErrors) Assert.Equal(ItemStatus.Failed, run.Items[0].Status);
        if (policy == PreflightPolicy.SkipErrors) Assert.Equal(ItemStatus.Skipped, run.Items[0].Status);
    }
    [Theory]
    [InlineData(LaunchErrorPolicy.Continue, 3)]
    [InlineData(LaunchErrorPolicy.Stop, 1)]
    public async Task Launch_error_policy_never_retries(LaunchErrorPolicy policy, int expected)
    {
        var launcher = new Launcher { Fail = true };
        var run = await new LaunchCoordinator(new Store(), launcher, new Log()).RunAsync(Sample(), new Consent(), scenario: new() { OnLaunchError = policy });
        Assert.Equal(expected, launcher.Items.Count); Assert.Equal(RunStatus.PartialFailure, run.Status);
        Assert.Equal(ItemStatus.Failed, run.Items[0].Status);
        if (policy == LaunchErrorPolicy.Stop) Assert.All(run.Items.Skip(1), i => Assert.Equal("Scenario.LaunchErrorStopped", i.Error));
    }
    [Fact] public async Task Skip_all_rejected_commands_does_not_request_unnecessary_consent()
    {
        var w = Sample() with { Items = [Sample().Items[2]] }; var consent = new Consent(); var launcher = new Launcher();
        var run = await new LaunchCoordinator(new Store(), launcher, new Log()).RunAsync(w, consent, scenario: new() { Preflight = PreflightPolicy.SkipErrors }, doctor: new Doctor(w));
        Assert.False(consent.Called); Assert.Empty(launcher.Items); Assert.Equal(RunStatus.PartialFailure, run.Status);
    }
    [Fact] public async Task Preflight_does_not_bypass_command_refusal()
    {
        var w = Sample(); var launcher = new Launcher(); var consent = new Consent { Allowed = false };
        var run = await new LaunchCoordinator(new Store(), launcher, new Log()).RunAsync(w, consent, scenario: new() { Preflight = PreflightPolicy.SkipErrors }, doctor: new Doctor(w));
        Assert.True(consent.Called); Assert.Empty(launcher.Items); Assert.Equal(RunStatus.Declined, run.Status);
    }
    [Fact] public async Task Cancellation_during_preflight_is_durable_and_dispatches_nothing()
    {
        using var cts = new CancellationTokenSource(); var w = Sample(); var store = new Store(); var launcher = new Launcher();
        var doctor = new Doctor(w) { Before = () => cts.Cancel() };
        var run = await new LaunchCoordinator(store, launcher, new Log()).RunAsync(w, new Consent(), token: cts.Token, scenario: new() { Preflight = PreflightPolicy.Continue }, doctor: doctor);
        Assert.Empty(launcher.Items); Assert.Equal(RunStatus.Cancelled, store.Last!.Status); Assert.Equal(run, store.Last);
    }
    [Fact] public async Task Failed_preflight_persistence_blocks_dispatch()
    {
        var w = Sample(); var store = new Store { FailAt = 2 }; var launcher = new Launcher();
        await Assert.ThrowsAsync<IOException>(() => new LaunchCoordinator(store, launcher, new Log()).RunAsync(w, new Consent(), scenario: new() { Preflight = PreflightPolicy.Continue }, doctor: new Doctor(w)));
        Assert.Empty(launcher.Items); Assert.Equal(RunStatus.Interrupted, store.Last!.Status);
    }
    [Fact] public async Task Preflight_requires_adapter_before_creating_history()
    {
        var store = new Store();
        await Assert.ThrowsAsync<ArgumentException>(() => new LaunchCoordinator(store, new Launcher(), new Log()).RunAsync(Sample(), new Consent(), scenario: new() { Preflight = PreflightPolicy.Continue }));
        Assert.Null(store.Last);
    }
    [Fact] public async Task Mismatched_doctor_snapshot_is_never_dispatched()
    {
        var w = Sample(); var launcher = new Launcher(); var store = new Store();
        await Assert.ThrowsAsync<InvalidDataException>(() => new LaunchCoordinator(store, launcher, new Log()).RunAsync(w, new Consent(), scenario: new() { Preflight = PreflightPolicy.Continue }, doctor: new Doctor(Sample())));
        Assert.Empty(launcher.Items); Assert.Equal(RunStatus.Interrupted, store.Last!.Status);
    }
    [Fact] public async Task Warnings_and_unknown_do_not_claim_readiness_or_block_by_error_policy()
    {
        var w = Sample(); var launcher = new Launcher(); var doctor = new Doctor(w) { Override = Report(w) with { Items = w.Items.Select(i => new DoctorItemResult(i.Id, i.Name, DoctorState.Unknown, [new(DoctorState.Unknown, "Doctor.Unavailable")])).ToArray() } };
        var run = await new LaunchCoordinator(new Store(), launcher, new Log()).RunAsync(w, new Consent(), scenario: new() { Preflight = PreflightPolicy.StopOnErrors }, doctor: doctor);
        Assert.Equal(3, launcher.Items.Count); Assert.Equal(RunStatus.Completed, run.Status);
    }
    [Theory] [InlineData(-1)] [InlineData(99)]
    public void Unknown_policies_rejected(int invalid)
    {
        Assert.Throws<ValidationException>(() => new LaunchScenarioOptions { Preflight = (PreflightPolicy)invalid }.Validate());
        Assert.Throws<ValidationException>(() => new LaunchScenarioOptions { OnLaunchError = (LaunchErrorPolicy)invalid }.Validate());
    }
    [Fact] public void Old_settings_default_to_legacy_and_invalid_maps_rejected()
    {
        var old = JsonSerializer.Deserialize<AppSettings>("{\"language\":\"ru\",\"theme\":\"Dark\"}", WorkspaceTransfer.Json)!;
        Assert.Empty(old.WorkspaceScenarios); Assert.True(LaunchScenarioOptions.ValidSettings(old));
        Assert.False(LaunchScenarioOptions.ValidSettings(old with { WorkspaceScenarios = null! }));
        Assert.False(LaunchScenarioOptions.ValidSettings(old with { WorkspaceScenarios = new Dictionary<Guid, LaunchScenarioOptions> { [Guid.Empty] = new() } }));
        Assert.False(LaunchScenarioOptions.ValidSettings(old with { WorkspaceScenarios = new Dictionary<Guid, LaunchScenarioOptions> { [Guid.NewGuid()] = null! } }));
    }
    [Fact] public async Task Scenario_settings_persist_backup_and_restore_without_schema_change()
    {
        var root = Path.Combine(Path.GetTempPath(), "SHIFT-scenario-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            var store = new SqliteStore(Path.Combine(root, "shift.db")); await store.InitializeAsync(); var w = Sample(); await store.SaveAsync(w);
            var options = new LaunchScenarioOptions { Preflight = PreflightPolicy.SkipErrors, OnLaunchError = LaunchErrorPolicy.Stop };
            await store.SaveSettingsAsync(new() { WorkspaceScenarios = new Dictionary<Guid, LaunchScenarioOptions> { [w.Id] = options } });
            Assert.Equal(options, (await store.LoadSettingsAsync()).WorkspaceScenarios[w.Id]);
            var imported = WorkspaceTransfer.Import(WorkspaceTransfer.Export(w));
            Assert.False((await store.LoadSettingsAsync()).WorkspaceScenarios.ContainsKey(imported.Id));
            Assert.False((await store.LoadSettingsAsync()).WorkspaceScenarios.ContainsKey(w.Duplicate("Copy").Id));
            Assert.Empty(await store.HistoryAsync());
            var backup = await store.BackupAsync(Path.Combine(root, "backups"));
            Assert.NotEmpty(backup);
            await store.SaveSettingsAsync(new());
            await store.RestoreAsync(backup, Path.Combine(root, "safety"));
            Assert.Equal(options, (await store.LoadSettingsAsync()).WorkspaceScenarios[w.Id]);
            await Assert.ThrowsAsync<ValidationException>(() => store.SaveSettingsAsync(new() { WorkspaceScenarios = new Dictionary<Guid, LaunchScenarioOptions> { [w.Id] = new() { Preflight = (PreflightPolicy)25 } } }));
            Assert.Equal(options, (await store.LoadSettingsAsync()).WorkspaceScenarios[w.Id]);
        }
        finally { SqliteConnection.ClearAllPools(); Directory.Delete(root, true); }
    }
    [Fact] public async Task Inspection_observations_are_saved_without_claiming_dispatch_readiness()
    {
        var w = Sample(); var run = await new LaunchCoordinator(new Store(), new Launcher(), new Log()).RunAsync(w, new Consent(),
            scenario: new() { Preflight = PreflightPolicy.Continue }, doctor: new Doctor(w));
        Assert.Equal(ItemStatus.Success, run.Items[0].Status);
        Assert.StartsWith("Scenario.PreflightObservation: ", run.Items[0].Error);
    }
    [Fact] public async Task Read_only_doctor_does_not_prevent_a_later_dispatch_failure()
    {
        var w = Sample(); var launcher = new Launcher { Fail = true };
        var doctor = new Doctor(w) { Override = Report(w) with { Items = w.Items.Select(i => new DoctorItemResult(i.Id, i.Name, DoctorState.Ready, [new(DoctorState.Ready, "Doctor.PathReady")])).ToArray() } };
        var run = await new LaunchCoordinator(new Store(), launcher, new Log()).RunAsync(w, new Consent(),
            scenario: new() { Preflight = PreflightPolicy.StopOnErrors, OnLaunchError = LaunchErrorPolicy.Stop }, doctor: doctor);
        Assert.Single(launcher.Items); Assert.Equal(RunStatus.PartialFailure, run.Status);
    }
    [Fact] public async Task Concurrent_start_rejected_while_preflight_pending()
    {
        var w = Sample(); var doctor = new BlockingDoctor(w); var coordinator = new LaunchCoordinator(new Store(), new Launcher(), new Log());
        var first = coordinator.RunAsync(w, new Consent(), scenario: new() { Preflight = PreflightPolicy.Continue }, doctor: doctor);
        await doctor.Started.Task;
        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.RunAsync(w, new Consent()));
        doctor.Release.SetResult(); await first;
    }
    private sealed class BlockingDoctor(Workspace w) : IWorkspaceDoctor
    {
        public TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously), Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<DoctorReport> CheckAsync(Workspace workspace, CancellationToken token = default) { Started.SetResult(); await Release.Task.WaitAsync(token); return Report(w); }
    }
    private sealed class Doctor(Workspace w) : IWorkspaceDoctor
    {
        public int Calls; public Action? Before; public DoctorReport? Override;
        public Task<DoctorReport> CheckAsync(Workspace workspace, CancellationToken token = default) { Calls++; Before?.Invoke(); return Task.FromResult(Override ?? Report(w)); }
    }
    private sealed class Launcher : IItemLauncher
    {
        public List<Guid> Items = []; public bool Fail;
        public Task LaunchAsync(WorkspaceItem item, CancellationToken token) { Items.Add(item.Id); if (Fail && Items.Count == 1) throw new IOException("Missing"); return Task.CompletedTask; }
    }
    private sealed class Consent : ICommandConsent
    {
        public bool Called; public bool Allowed = true;
        public Task<bool> ConfirmAsync(IReadOnlyList<WorkspaceItem> commands, CancellationToken token) { Called = true; return Task.FromResult(Allowed); }
    }
    private sealed class Store : IRunStore
    {
        public LaunchRun? Last; public int FailAt; private int saves;
        public Task SaveRunAsync(LaunchRun run, CancellationToken token = default) { if (++saves == FailAt) throw new IOException("Disk unavailable"); Last = run; return Task.CompletedTask; }
        public Task<IReadOnlyList<LaunchRun>> HistoryAsync(int limit = 100, CancellationToken token = default) => Task.FromResult<IReadOnlyList<LaunchRun>>([]);
        public Task RecoverInterruptedAsync(CancellationToken token = default) => Task.CompletedTask;
    }
    private sealed class Log : IAppLog { public void Info(string m) {} public void Error(string m, Exception e) {} }
}
