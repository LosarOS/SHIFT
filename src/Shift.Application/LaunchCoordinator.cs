using Shift.Domain;
namespace Shift.Application;

/// <summary>One launch per process. All state is persisted before publishing progress.</summary>
public sealed class LaunchCoordinator(IRunStore store, IItemLauncher launcher, IAppLog log)
{
    private readonly SemaphoreSlim gate = new(1, 1);

    public async Task<LaunchRun> RunAsync(Workspace workspace, ICommandConsent consent,
        IProgress<LaunchProgress>? progress = null, CancellationToken token = default)
    {
        WorkspaceValidator.Validate(workspace);
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
            var commands = ordered.Where(i => i.Enabled && i.Kind == ItemKind.Command).ToArray();
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
            }
            run = run with
            {
                Status = run.Items.Any(i => i.Status == ItemStatus.Failed) ? RunStatus.PartialFailure : RunStatus.Completed
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
