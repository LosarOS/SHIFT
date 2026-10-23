using Shift.Domain;
namespace Shift.Application;

/// <summary>One instance is shared by profile apply and recovery. Windows offers no atomic desktop transaction.</summary>
public sealed class StudioOperationGate
{
    internal SemaphoreSlim Semaphore { get; } = new(1, 1);
}

public sealed class DesktopProfileService(IStudioStore store, IWallpaperService wallpapers, IStudioImageLibrary images,
    IWidgetRuntime runtime, IAppLog log, StudioOperationGate gate) : IDesktopProfileService, IStudioRecoveryService
{
    private static bool Same(string? left, string? right) => string.Equals(left?.Replace('\\', '/'), right?.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase);
    private static string Error(Exception ex) => ex.Message.Length > 1800 ? ex.Message[..1800] : ex.Message;
    private static bool Incomplete(StudioRecoveryJournal j) => j.Status is StudioApplyStatus.Preparing or StudioApplyStatus.Applying or StudioApplyStatus.Interrupted or StudioApplyStatus.PartiallyApplied
        || j.Status == StudioApplyStatus.Failed && (j.AttemptedMonitorIds.Count > 0 || j.PositionAttempted || j.WidgetsAttempted);
    private async Task EnsureEnabledAsync(CancellationToken token)
    { if (!(await store.LoadStudioSettingsAsync(token)).Enabled) throw new InvalidOperationException("Studio is disabled."); }

    public async Task<StudioApplyResult> ApplyAsync(Guid profileId, bool wallpaperConsent, CancellationToken token = default)
    {
        await gate.Semaphore.WaitAsync(token);
        StudioRecoveryJournal? journal = null;
        var errors = new List<string>(); var successes = 0;
        try
        {
            token.ThrowIfCancellationRequested();
            var settings = await store.LoadStudioSettingsAsync(token);
            StudioValidator.Validate(settings);
            if (!settings.Enabled) throw new InvalidOperationException("Studio is disabled.");
            if ((await store.ListRecoveryJournalsAsync(token)).Any(Incomplete))
                throw new InvalidOperationException("Resolve the unfinished Studio operation before applying another profile.");
            var profiles = await store.ListProfilesAsync(token);
            var profile = profiles.SingleOrDefault(p => p.Id == profileId) ?? throw new InvalidOperationException("Desktop profile was not found.");
            StudioValidator.Validate(profile);
            if (!profile.Enabled) throw new InvalidOperationException("This desktop profile is disabled.");
            var definitions = await store.ListDefinitionsAsync(token);
            foreach (var instance in profile.Widgets)
            {
                var definition = definitions.SingleOrDefault(d => d.Id == instance.DefinitionId) ?? throw new InvalidOperationException("A widget definition is missing.");
                StudioValidator.Validate(definition);
            }
            var changesWindows = profile.Wallpapers.Count > 0 || profile.ChangeWallpaperPosition;
            if (changesWindows && !wallpaperConsent)
                throw new InvalidOperationException("Changing Windows wallpaper requires current explicit static-image confirmation.");
            var preparedWallpapers = new List<WallpaperAssignment>();
            foreach (var assignment in profile.Wallpapers)
            {
                var managedPath = await images.ImportAsync(assignment.ImagePath, token);
                await images.ValidateAsync(managedPath, token);
                preparedWallpapers.Add(assignment with { ImagePath = managedPath });
            }
            profile = profile with { Wallpapers = preparedWallpapers };
            var before = changesWindows ? await wallpapers.CaptureAsync(token) : new WallpaperSnapshot([], WallpaperPlacement.Fill, true);
            if (changesWindows && (!before.CanRestore || before.SlideshowActive))
                throw new InvalidOperationException(before.Limitation ?? "The current wallpaper configuration cannot be restored safely.");
            foreach (var assignment in profile.Wallpapers)
                if (!before.Wallpapers.Any(w => w.MonitorId == assignment.MonitorId)) throw new InvalidOperationException("A selected display is no longer available.");
            var recoveryWallpapers = new List<WallpaperAssignment>();
            if (changesWindows)
                foreach (var assignment in before.Wallpapers)
                {
                    var copy = await images.ImportAsync(assignment.ImagePath, token);
                    await images.ValidateAsync(copy, token);
                    recoveryWallpapers.Add(assignment with { ImagePath = copy });
                }
            var previous = profiles.FirstOrDefault(p => p.Id == settings.ActiveProfileId);
            journal = new StudioRecoveryJournal
            {
                ProfileId = profile.Id, PreviousProfileId = settings.ActiveProfileId,
                PreviousProfileSnapshot = previous,
                PreviousDefinitions = previous is null ? [] : definitions.Where(d => previous.Widgets.Any(w => w.DefinitionId == d.Id)).ToArray(),
                Before = before, RecoveryWallpapers = recoveryWallpapers,
                Intended = new(profile.Wallpapers.ToArray(), profile.ChangeWallpaperPosition ? profile.WallpaperPosition : before.Position, true),
                Status = StudioApplyStatus.Applying
            };
            StudioValidator.Validate(journal);
            await store.SaveRecoveryJournalAsync(journal, token); // No external mutation before this durable boundary.
            if (changesWindows && wallpaperConsent && !settings.WallpaperConsentGranted)
            {
                settings = (await store.LoadStudioSettingsAsync(token)) with { WallpaperConsentGranted = true };
                await store.SaveStudioSettingsAsync(settings, token);
            }
            foreach (var assignment in profile.Wallpapers)
            {
                token.ThrowIfCancellationRequested(); await EnsureEnabledAsync(token);
                journal = journal with { AttemptedMonitorIds = journal.AttemptedMonitorIds.Append(assignment.MonitorId).ToArray() };
                await store.SaveRecoveryJournalAsync(journal, token);
                try
                {
                    await wallpapers.SetWallpaperAsync(assignment, token);
                    journal = journal with { AppliedMonitorIds = journal.AppliedMonitorIds.Append(assignment.MonitorId).ToArray() };
                    successes++;
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { errors.Add(Error(ex)); log.Error("Studio wallpaper apply failed", ex); }
                await store.SaveRecoveryJournalAsync(journal with { Errors = errors.ToArray() }, CancellationToken.None);
            }
            if (profile.ChangeWallpaperPosition)
            {
                token.ThrowIfCancellationRequested(); await EnsureEnabledAsync(token);
                journal = journal with { PositionAttempted = true };
                await store.SaveRecoveryJournalAsync(journal, token);
                try { await wallpapers.SetPositionAsync(profile.WallpaperPosition, token); journal = journal with { PositionApplied = true }; successes++; }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { errors.Add(Error(ex)); log.Error("Studio wallpaper placement failed", ex); }
                await store.SaveRecoveryJournalAsync(journal with { Errors = errors.ToArray() }, CancellationToken.None);
            }
            token.ThrowIfCancellationRequested(); await EnsureEnabledAsync(token);
            journal = journal with { WidgetsAttempted = true };
            await store.SaveRecoveryJournalAsync(journal, token);
            try
            {
                await runtime.ApplyAsync(profile, definitions, token); successes++;
                var current = await store.LoadStudioSettingsAsync(CancellationToken.None);
                if (!current.Enabled) { await runtime.StopAsync(CancellationToken.None); throw new InvalidOperationException("Studio was disabled while the profile was applying."); }
                await store.SaveStudioSettingsAsync(current with { ActiveProfileId = profile.Id }, CancellationToken.None);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { errors.Add(Error(ex)); log.Error("Studio widget apply failed", ex); }
            if (errors.Count == 0)
            {
                var current = (await store.ListProfilesAsync(CancellationToken.None)).FirstOrDefault(p => p.Id == profile.Id);
                if (current is not null) await store.SaveProfileAsync(current with { LastAppliedAt = DateTimeOffset.UtcNow }, CancellationToken.None);
            }
            var status = errors.Count == 0 ? StudioApplyStatus.Applied : successes > 0 ? StudioApplyStatus.PartiallyApplied : StudioApplyStatus.Failed;
            await store.SaveRecoveryJournalAsync(journal with { Status = status, Errors = errors.ToArray() }, CancellationToken.None);
            return new(journal.Id, status, errors);
        }
        catch (Exception ex) when (journal is not null)
        {
            errors.Add(Error(ex)); log.Error("Studio apply interrupted; recovery journal retained", ex);
            var touched = journal.AttemptedMonitorIds.Count > 0 || journal.PositionAttempted || journal.WidgetsAttempted;
            var status = ex is OperationCanceledException ? StudioApplyStatus.Interrupted : touched ? StudioApplyStatus.PartiallyApplied : StudioApplyStatus.Failed;
            try { await store.SaveRecoveryJournalAsync(journal with { Status = status, Errors = errors.ToArray() }, CancellationToken.None); }
            catch (Exception persist) { log.Error("Studio recovery journal update failed; prior intent retained", persist); }
            return new(journal.Id, status, errors);
        }
        finally { gate.Semaphore.Release(); }
    }

    public async Task<IReadOnlyList<StudioRecoveryJournal>> GetPendingAsync(CancellationToken token = default)
        => (await store.ListRecoveryJournalsAsync(token)).Where(Incomplete).OrderByDescending(j => j.CreatedAt).ToArray();

    public async Task KeepCurrentAsync(Guid journalId, CancellationToken token = default)
    {
        await gate.Semaphore.WaitAsync(token);
        try
        {
            var journal = (await store.ListRecoveryJournalsAsync(token)).SingleOrDefault(j => j.Id == journalId) ?? throw new InvalidOperationException("Recovery record was not found.");
            await store.SaveRecoveryJournalAsync(journal with { Status = StudioApplyStatus.Kept }, token);
        }
        finally { gate.Semaphore.Release(); }
    }

    public async Task<StudioRestoreResult> RestoreAsync(Guid journalId, bool overwriteConflicts = false, CancellationToken token = default)
    {
        await gate.Semaphore.WaitAsync(token);
        var conflicts = new List<string>(); var errors = new List<string>();
        try
        {
            var journals = await store.ListRecoveryJournalsAsync(token);
            var journal = journals.SingleOrDefault(j => j.Id == journalId) ?? throw new InvalidOperationException("Recovery record was not found.");
            StudioValidator.Validate(journal);
            if (journal.Status == StudioApplyStatus.Restored) return new(true, [], []);
            if (journal.Status == StudioApplyStatus.Kept) return new(false, [], ["This operation was explicitly acknowledged as kept."]);
            var hasWindowsChanges = journal.AttemptedMonitorIds.Count > 0 || journal.PositionAttempted;
            var recoveryCopies = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var copy in journal.RecoveryWallpapers)
            {
                try
                {
                    var resolved = await images.ImportAsync(copy.ImagePath, token);
                    await images.ValidateAsync(resolved, token);
                    recoveryCopies[copy.MonitorId] = resolved;
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { log.Error("Studio recovery copy unavailable; original will be checked", ex); }
            }
            bool AlreadyRestored(string monitorId, string actual, string original) => Same(actual, original)
                || recoveryCopies.TryGetValue(monitorId, out var copy) && Same(actual, copy);
            var current = hasWindowsChanges ? await wallpapers.CaptureAsync(token) : journal.Before;
            if (hasWindowsChanges && (!current.CanRestore || current.SlideshowActive)) return new(false, [current.Limitation ?? "Windows wallpaper state cannot be compared safely."], []);
            foreach (var id in journal.AttemptedMonitorIds.Distinct())
            {
                var actual = current.Wallpapers.FirstOrDefault(w => w.MonitorId == id);
                var before = journal.Before.Wallpapers.FirstOrDefault(w => w.MonitorId == id);
                var expected = journal.Intended.Wallpapers.FirstOrDefault(w => w.MonitorId == id);
                if (actual is null || before is null || expected is null) conflicts.Add("Display unavailable or recovery snapshot incomplete: " + id);
                else if (!Same(actual.ImagePath, expected.ImagePath) && !AlreadyRestored(id, actual.ImagePath, before.ImagePath)) conflicts.Add("Wallpaper changed after SHIFT: " + id);
            }
            if (journal.PositionAttempted && current.Position != journal.Intended.Position && current.Position != journal.Before.Position) conflicts.Add("Wallpaper placement changed after SHIFT.");
            var settings = await store.LoadStudioSettingsAsync(token);
            if (journal.WidgetsAttempted && (settings.ActiveProfileId != journal.ProfileId && settings.ActiveProfileId != journal.PreviousProfileId
                || journals.Any(j => j.Id != journal.Id && j.CreatedAt > journal.CreatedAt && j.WidgetsAttempted && j.Status is not (StudioApplyStatus.Restored or StudioApplyStatus.Kept))))
                conflicts.Add("A newer Studio widget layout is active.");
            if (conflicts.Count > 0 && !overwriteConflicts) return new(false, conflicts, []);
            // Persist recovery intent. Repeated restore safely recognizes already-restored before-values.
            journal = journal with { Status = StudioApplyStatus.Interrupted, Errors = ["Restore in progress; verify current desktop before retrying."] };
            await store.SaveRecoveryJournalAsync(journal, token);
            foreach (var id in journal.AttemptedMonitorIds.Distinct())
            {
                token.ThrowIfCancellationRequested();
                var before = journal.Before.Wallpapers.FirstOrDefault(w => w.MonitorId == id);
                var expected = journal.Intended.Wallpapers.FirstOrDefault(w => w.MonitorId == id);
                var freshSnapshot = await wallpapers.CaptureAsync(token);
                if (!freshSnapshot.CanRestore || freshSnapshot.SlideshowActive) { conflicts.Add("Windows wallpaper state changed during restore: " + id); continue; }
                var fresh = freshSnapshot.Wallpapers.FirstOrDefault(w => w.MonitorId == id);
                if (before is null || expected is null || fresh is null) { errors.Add("Display unavailable: " + id); continue; }
                if (AlreadyRestored(id, fresh.ImagePath, before.ImagePath)) continue;
                if (!overwriteConflicts && !Same(fresh.ImagePath, expected.ImagePath)) { conflicts.Add("Wallpaper changed during restore: " + id); continue; }
                try
                {
                    var target = before;
                    try { await images.ValidateAsync(before.ImagePath, token); }
                    catch (OperationCanceledException) { throw; }
                    catch
                    {
                        if (!recoveryCopies.TryGetValue(id, out var copy)) throw new IOException("Original wallpaper and managed recovery copy are unavailable: " + id);
                        await images.ValidateAsync(copy, token);
                        target = before with { ImagePath = copy };
                    }
                    await wallpapers.SetWallpaperAsync(target, token);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { errors.Add(Error(ex)); log.Error("Studio wallpaper restore failed", ex); }
            }
            if (journal.PositionAttempted)
            {
                var fresh = await wallpapers.CaptureAsync(token);
                if (!fresh.CanRestore || fresh.SlideshowActive) conflicts.Add("Windows wallpaper state changed during placement restore.");
                else if (fresh.Position != journal.Before.Position)
                {
                    if (!overwriteConflicts && fresh.Position != journal.Intended.Position) conflicts.Add("Wallpaper placement changed during restore.");
                    else try { await wallpapers.SetPositionAsync(journal.Before.Position, token); }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex) { errors.Add(Error(ex)); log.Error("Studio placement restore failed", ex); }
                }
            }
            if (journal.WidgetsAttempted)
            {
                try
                {
                    settings = await store.LoadStudioSettingsAsync(token);
                    if (settings.Enabled && journal.PreviousProfileSnapshot is not null)
                        await runtime.ApplyAsync(journal.PreviousProfileSnapshot, journal.PreviousDefinitions, token);
                    else await runtime.StopAsync(token);
                    await store.SaveStudioSettingsAsync((await store.LoadStudioSettingsAsync(CancellationToken.None)) with { ActiveProfileId = journal.PreviousProfileId }, CancellationToken.None);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { errors.Add(Error(ex)); log.Error("Studio widget restore failed", ex); }
            }
            var restored = conflicts.Count == 0 || overwriteConflicts;
            restored &= errors.Count == 0;
            await store.SaveRecoveryJournalAsync(journal with { Status = restored ? StudioApplyStatus.Restored : StudioApplyStatus.Interrupted, Errors = errors.Concat(conflicts).Take(100).ToArray() }, CancellationToken.None);
            return new(restored, conflicts, errors);
        }
        catch (Exception ex)
        {
            log.Error("Studio restore incomplete; no automatic retry", ex); errors.Add(Error(ex));
            return new(false, conflicts, errors);
        }
        finally { gate.Semaphore.Release(); }
    }
}
