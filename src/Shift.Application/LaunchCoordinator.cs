using Shift.Domain;
namespace Shift.Application;

/// <summary>One launch per process. All state is persisted before publishing progress.</summary>
public sealed class LaunchCoordinator(IRunStore store, IItemLauncher launcher, IAppLog log)
{
    private readonly SemaphoreSlim gate = new(1, 1);

    public async Task<LaunchRun> RunAsync(Workspace workspace, ICommandConsent consent,
        IProgress<LaunchProgress>? progress = null, CancellationToken token = default,
        LaunchScenarioOptions? scenario = null, IWorkspaceDoctor? doctor = null)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        WorkspaceValidator.Validate(workspace);
        workspace = workspace with { Items = Array.AsReadOnly(workspace.Items.ToArray()), Tags = Array.AsReadOnly(workspace.Tags.ToArray()) };
        scenario ??= new LaunchScenarioOptions();
        scenario.Validate();
        if (scenario.Preflight != PreflightPolicy.Off && doctor is null)
            throw new ArgumentException("Preflight requires Workspace Doctor.", nameof(doctor));
        if (!await gate.WaitAsync(0, token))
            throw new InvalidOperationException("A workspace launch is already in progress.");
        var ordered = workspace.Items.OrderBy(i => i.Order).ThenBy(i => i.Id).ToArray();
        var run = new LaunchRun(Guid.NewGuid(), workspace.Id, workspace.Name, DateTimeOffset.UtcNow, null,
            RunStatus.Running, ordered.Select(i => new LaunchRunItem(i.Id, i.Name, i.Kind, i.Order)).ToArray());
        async Task PersistAsync()
        {
            // Cancellation must not prevent final state from reaching durable history.
            await store.SaveRunAsync(run, CancellationToken.None);
            progress?.Report(new LaunchProgress(run));
        }
        void Update(Guid id, Func<LaunchRunItem, LaunchRunItem> change) =>
            run = run with
            {
                Items = run.Items.Select(i => i.ItemId == id ? change(i) : i).ToArray()
            };
        try
        {
            await PersistAsync();
            var rejected = new Dictionary<Guid, DoctorItemResult>();
            if (scenario.Preflight != PreflightPolicy.Off)
            {
                var report = await doctor!.CheckAsync(workspace, token);
                token.ThrowIfCancellationRequested();
                if (report.WorkspaceId != workspace.Id || report.Items.Count != ordered.Length
                    || report.Items.Select(i => i.ItemId).Distinct().Count() != ordered.Length
                    || report.Items.Any(i => !ordered.Any(item => item.Id == i.ItemId) || !Enum.IsDefined(i.State)))
                    throw new InvalidDataException("Workspace Doctor returned an invalid snapshot.");
                log.Info($"Run {run.Id}: preflight Ready={report.Ready}, Warning={report.Warnings}, Error={report.Errors}, Unknown={report.Unknown}.");
                foreach (var result in report.Items.Where(i => i.State != DoctorState.Ready && ordered.Any(item => item.Id == i.ItemId && item.Enabled)))
                    Update(result.ItemId, state => state with { Error = "Scenario.PreflightObservation: " + string.Join(", ", result.Findings.Select(f => f.Code)) });
                rejected = report.Items.Where(i => i.State == DoctorState.Error && ordered.Any(item => item.Id == i.ItemId && item.Enabled))
                    .ToDictionary(i => i.ItemId);
                if (scenario.Preflight == PreflightPolicy.StopOnErrors && rejected.Count > 0)
                {
                    foreach (var (id, result) in rejected)
                        Update(id, state => state with { Status = ItemStatus.Failed, Error = PreflightError(result), FinishedAt = DateTimeOffset.UtcNow });
                    run = run with { Status = RunStatus.PartialFailure };
                    return await FinishAsync("Scenario.PreflightStopped");
                }
                if (scenario.Preflight == PreflightPolicy.SkipErrors)
                    foreach (var (id, result) in rejected)
                        Update(id, state => state with { Status = ItemStatus.Skipped, Error = PreflightError(result), FinishedAt = DateTimeOffset.UtcNow });
                await PersistAsync();
            }
            var commands = ordered.Where(i => i.Enabled && i.Kind == ItemKind.Command
                && !(scenario.Preflight == PreflightPolicy.SkipErrors && rejected.ContainsKey(i.Id))).ToArray();
            if (commands.Length > 0 && !await consent.ConfirmAsync(commands, token))
            {
                run = run with
                {
                    Status = RunStatus.Declined
                };
                return await FinishAsync("Command execution was declined.");
            }
            foreach (var item in ordered)
            {
                token.ThrowIfCancellationRequested();
                if (scenario.Preflight == PreflightPolicy.SkipErrors && rejected.ContainsKey(item.Id)) continue;
                if (!item.Enabled)
                {
                    Update(item.Id, s => s with { Status = ItemStatus.Skipped, FinishedAt = DateTimeOffset.UtcNow });
                    await PersistAsync();
                    continue;
                }
                // Storage failures are orchestration failures, never ordinary item failures.
                // Stop before dispatch if the running state cannot be persisted.
                await Task.Delay(item.DelayMs, token);
                Update(item.Id, s => s with { Status = ItemStatus.Running, StartedAt = DateTimeOffset.UtcNow });
                await PersistAsync();
                try
                {
                    await launcher.LaunchAsync(item, token);
                    Update(item.Id, s => s with { Status = ItemStatus.Success, FinishedAt = DateTimeOffset.UtcNow });
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                catch (Exception ex)
                {
                    log.Error($"Launch item {item.Id} failed.", ex);
                    Update(item.Id, s => s with { Status = ItemStatus.Failed, Error = ex.Message, FinishedAt = DateTimeOffset.UtcNow });
                }
                await PersistAsync();
                if (scenario.OnLaunchError == LaunchErrorPolicy.Stop && run.Items.Any(i => i.ItemId == item.Id && i.Status == ItemStatus.Failed))
                {
                    run = run with { Status = RunStatus.PartialFailure };
                    return await FinishAsync("Scenario.LaunchErrorStopped");
                }
            }
            run = run with
            {
                Status = run.Items.Any(i => i.Status == ItemStatus.Failed) || (scenario.Preflight == PreflightPolicy.SkipErrors && rejected.Count > 0) ? RunStatus.PartialFailure : RunStatus.Completed
            };
            return await FinishAsync(null);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            run = run with
            {
                Status = RunStatus.Cancelled
            };
            return await FinishAsync("Launch cancelled before dispatch.");
        }
        catch (Exception ex)
        {
            log.Error($"Launch run {run.Id} interrupted.", ex);
            run = run with
            {
                Status = RunStatus.Interrupted
            };
            try
            {
                await FinishAsync("Launch interrupted. Check the local log.");
            }
            catch (Exception persistenceError) { log.Error("Could not persist interrupted state; recovery will run on next startup.", persistenceError); }
            throw;
        }
        finally { gate.Release(); }

        static string PreflightError(DoctorItemResult result) => "Scenario.PreflightError: "
            + string.Join(", ", result.Findings.Where(f => f.State == DoctorState.Error).Select(f => f.Code));

        async Task<LaunchRun> FinishAsync(string? reason)
        {
            run = run with
            {
                FinishedAt = DateTimeOffset.UtcNow,
                Items = run.Items.Select(i => i.Status is ItemStatus.Pending or ItemStatus.Running
                    ? i with
                    {
                        Status = ItemStatus.Skipped,
                        Error = reason,
                        FinishedAt = DateTimeOffset.UtcNow
                    } : i).ToArray()
            };
            await PersistAsync();
            log.Info($"Run {run.Id}: {run.Status}.");
            return run;
        }
    }
}
