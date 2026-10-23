using Shift.Application;
using Shift.Domain;
using Xunit;
namespace Shift.Tests;

public class StudioApplicationTests
{
    private sealed class MemoryStore : IStudioStore
    {
        public List<DesktopProfile> Profiles = [];
        public List<WidgetDefinition> Definitions = [];
        public Dictionary<Guid, StudioRecoveryJournal> Journals = [];
        public StudioSettings Settings = new() { Enabled = true };
        public int JournalWrites; public int FailJournalWrite;
        public Task<IReadOnlyList<DesktopProfile>> ListProfilesAsync(CancellationToken token = default) => Task.FromResult<IReadOnlyList<DesktopProfile>>(Profiles.ToArray());
        public Task<DesktopProfile> SaveProfileAsync(DesktopProfile profile, CancellationToken token = default) { Profiles.RemoveAll(p => p.Id == profile.Id); Profiles.Add(profile with { Revision = profile.Revision + 1 }); return Task.FromResult(Profiles[^1]); }
        public Task DeleteProfileAsync(Guid id, CancellationToken token = default) { Profiles.RemoveAll(p => p.Id == id); return Task.CompletedTask; }
        public Task<IReadOnlyList<WidgetDefinition>> ListDefinitionsAsync(CancellationToken token = default) => Task.FromResult<IReadOnlyList<WidgetDefinition>>(Definitions.ToArray());
        public Task<WidgetDefinition> SaveDefinitionAsync(WidgetDefinition definition, CancellationToken token = default) { Definitions.Add(definition); return Task.FromResult(definition); }
        public Task DeleteDefinitionAsync(Guid id, CancellationToken token = default) => Task.CompletedTask;
        public Task<WidgetRuntimeState?> GetWidgetStateAsync(Guid id, CancellationToken token = default) => Task.FromResult<WidgetRuntimeState?>(null);
        public Task<WidgetRuntimeState> SaveWidgetStateAsync(WidgetRuntimeState state, CancellationToken token = default) => Task.FromResult(state);
        public Task<StudioSettings> LoadStudioSettingsAsync(CancellationToken token = default) => Task.FromResult(Settings);
        public Task SaveStudioSettingsAsync(StudioSettings settings, CancellationToken token = default) { Settings = settings; return Task.CompletedTask; }
        public Task<IReadOnlyList<StudioRecoveryJournal>> ListRecoveryJournalsAsync(CancellationToken token = default) => Task.FromResult<IReadOnlyList<StudioRecoveryJournal>>(Journals.Values.ToArray());
        public Task SaveRecoveryJournalAsync(StudioRecoveryJournal journal, CancellationToken token = default)
        { if (++JournalWrites == FailJournalWrite) throw new IOException("Injected durable write failure"); Journals[journal.Id] = journal; return Task.CompletedTask; }
    }
    private sealed class Wallpaper : IWallpaperService
    {
        public Dictionary<string, string> Values = new() { ["A"] = @"C:\original-a.png", ["B"] = @"C:\original-b.png" };
        public WallpaperPlacement Position = WallpaperPlacement.Fit;
        public bool Slideshow; public int Sets; public Action<WallpaperAssignment>? BeforeSet; public bool FailAfterMutation; public string? FailMonitor;
        public Task<WallpaperSnapshot> CaptureAsync(CancellationToken token = default) => Task.FromResult(new WallpaperSnapshot(Values.Select(p => new WallpaperAssignment(p.Key, p.Value)).ToArray(), Position, !Slideshow, Slideshow));
        public Task SetWallpaperAsync(WallpaperAssignment assignment, CancellationToken token = default)
        {
            BeforeSet?.Invoke(assignment); Sets++;
            if (FailMonitor == assignment.MonitorId && !FailAfterMutation) throw new IOException("Injected display failure");
            Values[assignment.MonitorId] = assignment.ImagePath;
            if (FailMonitor == assignment.MonitorId) throw new IOException("Ambiguous post-mutation failure");
            token.ThrowIfCancellationRequested(); return Task.CompletedTask;
        }
        public Task SetPositionAsync(WallpaperPlacement position, CancellationToken token = default) { Position = position; return Task.CompletedTask; }
    }
    private sealed class Images : IStudioImageLibrary
    {
        public string? ResolvedPath;
        public Task<string> ImportAsync(string path, CancellationToken token = default) => Task.FromResult(ResolvedPath ?? path);
        public Task ValidateAsync(string path, CancellationToken token = default) { if (path.EndsWith("missing.png")) throw new FileNotFoundException(); return Task.CompletedTask; }
    }
    private sealed class Runtime : IWidgetRuntime
    {
        public int WindowCount => 0; public int Applied; public int Stopped; public bool Fail;
        public DesktopProfile? LastProfile;
        public Task ApplyAsync(DesktopProfile profile, IReadOnlyList<WidgetDefinition> definitions, CancellationToken token = default)
        { Applied++; LastProfile = profile; if (Fail) throw new InvalidOperationException("Injected window failure"); return Task.CompletedTask; }
        public Task StopAsync(CancellationToken token = default) { Stopped++; return Task.CompletedTask; }
        public Task SetInteractionAsync(WidgetInteraction interaction, CancellationToken token = default) => Task.CompletedTask;
        public Task GatherAsync(CancellationToken token = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private sealed class Log : IAppLog { public void Info(string message) { } public void Error(string message, Exception exception) { } }
    private sealed class Fixture
    {
        public readonly MemoryStore Store = new(); public readonly Wallpaper Wallpaper = new(); public readonly Runtime Runtime = new();
        public readonly DesktopProfile Profile = new() { Name = "Development", Wallpapers = [new("A", @"C:\selected.png")] };
        public readonly Images Images = new();
        public readonly DesktopProfileService Service;
        public Fixture() { Store.Profiles.Add(Profile); Service = new(Store, Wallpaper, Images, Runtime, new Log(), new()); }
    }
    [Fact] public async Task ApplyPersistsIntentBeforeWindowsAndAcknowledgementAfter()
    {
        var f = new Fixture(); f.Wallpaper.BeforeSet = a => Assert.Contains(a.MonitorId, Assert.Single(f.Store.Journals.Values).AttemptedMonitorIds);
        var result = await f.Service.ApplyAsync(f.Profile.Id, true);
        Assert.Equal(StudioApplyStatus.Applied, result.Status); Assert.Equal(@"C:\selected.png", f.Wallpaper.Values["A"]);
        Assert.Equal(WallpaperPlacement.Fit, f.Wallpaper.Position); // No implicit position change.
        Assert.Equal(f.Profile.Id, f.Store.Settings.ActiveProfileId); Assert.True(f.Store.Settings.WallpaperConsentGranted);
        Assert.Equal(["A"], f.Store.Journals[result.JournalId].AppliedMonitorIds);
    }
    [Fact] public async Task ConsentAndDisabledStudioCannotMutateWindows()
    {
        var f = new Fixture(); await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.ApplyAsync(f.Profile.Id, false));
        f.Store.Settings = f.Store.Settings with { Enabled = false };
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.ApplyAsync(f.Profile.Id, true));
        Assert.Equal(0, f.Wallpaper.Sets); Assert.Empty(f.Store.Journals); Assert.Equal(0, f.Runtime.Applied);
    }
    [Fact] public async Task SlideshowOrMissingImageRefusesBeforeJournalOrMutation()
    {
        var f = new Fixture(); f.Wallpaper.Slideshow = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.ApplyAsync(f.Profile.Id, true));
        f.Wallpaper.Slideshow = false; f.Store.Profiles[0] = f.Profile with { Wallpapers = [new("A", @"C:\missing.png")] };
        await Assert.ThrowsAsync<FileNotFoundException>(() => f.Service.ApplyAsync(f.Profile.Id, true));
        Assert.Equal(0, f.Wallpaper.Sets); Assert.Empty(f.Store.Journals);
    }
    [Fact] public async Task WidgetOnlyProfileDoesNotRequireWallpaperConsentOrCapture()
    {
        var f = new Fixture(); f.Wallpaper.Slideshow = true; f.Store.Profiles[0] = f.Profile with { Wallpapers = [] };
        Assert.Equal(StudioApplyStatus.Applied, (await f.Service.ApplyAsync(f.Profile.Id, false)).Status);
        Assert.Equal(0, f.Wallpaper.Sets); Assert.Equal(1, f.Runtime.Applied);
    }
    [Fact] public async Task PartialFailureKeepsSuccessfulActionsAndStopsUnreviewedNextApply()
    {
        var f = new Fixture(); f.Store.Profiles[0] = f.Profile with { Wallpapers = [new("A", @"C:\a.png"), new("B", @"C:\b.png")] }; f.Wallpaper.FailMonitor = "B";
        var result = await f.Service.ApplyAsync(f.Profile.Id, true);
        Assert.Equal(StudioApplyStatus.PartiallyApplied, result.Status); Assert.Equal(@"C:\a.png", f.Wallpaper.Values["A"]); Assert.Equal(@"C:\original-b.png", f.Wallpaper.Values["B"]);
        Assert.Single(await f.Service.GetPendingAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.ApplyAsync(f.Profile.Id, true));
        await f.Service.KeepCurrentAsync(result.JournalId); Assert.Empty(await f.Service.GetPendingAsync());
    }
    [Fact] public async Task DurableWriteFailureBeforeMutationCannotTouchWindows()
    {
        var f = new Fixture(); f.Store.FailJournalWrite = 1;
        var result = await f.Service.ApplyAsync(f.Profile.Id, true);
        Assert.Equal(StudioApplyStatus.Failed, result.Status); Assert.Equal(0, f.Wallpaper.Sets); Assert.Equal(0, f.Runtime.Applied);
    }
    [Fact] public async Task PostMutationAcknowledgementFailureRetainsRecoverableIntent()
    {
        var f = new Fixture(); f.Store.FailJournalWrite = 3;
        var result = await f.Service.ApplyAsync(f.Profile.Id, true);
        Assert.Equal(StudioApplyStatus.PartiallyApplied, result.Status); Assert.Equal(0, f.Runtime.Applied);
        Assert.Contains("A", f.Store.Journals[result.JournalId].AttemptedMonitorIds);
        Assert.True((await f.Service.RestoreAsync(result.JournalId)).Restored); Assert.Equal(@"C:\original-a.png", f.Wallpaper.Values["A"]);
    }
    [Fact] public async Task CancellationAfterPossibleDispatchIsRecordedWithoutAutomaticRollback()
    {
        var f = new Fixture(); using var cts = new CancellationTokenSource(); f.Wallpaper.BeforeSet = _ => cts.Cancel();
        var result = await f.Service.ApplyAsync(f.Profile.Id, true, cts.Token);
        Assert.Equal(StudioApplyStatus.Interrupted, result.Status); Assert.Equal(@"C:\selected.png", f.Wallpaper.Values["A"]); Assert.Equal(0, f.Runtime.Applied);
        Assert.Single(await f.Service.GetPendingAsync()); Assert.Equal(1, f.Wallpaper.Sets);
    }
    [Fact] public async Task UserWallpaperConflictDoesNotOverwriteOrRestoreUntouchedDisplays()
    {
        var f = new Fixture(); var result = await f.Service.ApplyAsync(f.Profile.Id, true);
        f.Wallpaper.Values["A"] = @"C:\user-choice.png"; f.Wallpaper.Values["B"] = @"C:\other-user-choice.png";
        var restore = await f.Service.RestoreAsync(result.JournalId);
        Assert.False(restore.Restored); Assert.NotEmpty(restore.Conflicts); Assert.Equal(@"C:\user-choice.png", f.Wallpaper.Values["A"]);
        Assert.True((await f.Service.RestoreAsync(result.JournalId, true)).Restored);
        Assert.Equal(@"C:\original-a.png", f.Wallpaper.Values["A"]); Assert.Equal(@"C:\other-user-choice.png", f.Wallpaper.Values["B"]);
    }
    [Fact] public async Task AmbiguousExternalMutationIsRecoverableAndRestoreIsIdempotent()
    {
        var f = new Fixture(); f.Wallpaper.FailMonitor = "A"; f.Wallpaper.FailAfterMutation = true;
        var result = await f.Service.ApplyAsync(f.Profile.Id, true);
        var journal = f.Store.Journals[result.JournalId]; Assert.Empty(journal.AppliedMonitorIds); Assert.Contains("A", journal.AttemptedMonitorIds);
        f.Wallpaper.FailMonitor = null;
        Assert.True((await f.Service.RestoreAsync(result.JournalId)).Restored); var count = f.Wallpaper.Sets;
        Assert.True((await f.Service.RestoreAsync(result.JournalId)).Restored); Assert.Equal(count, f.Wallpaper.Sets);
    }
    [Fact] public async Task WidgetFailureIsIndependentAndDoesNotDiscardWallpaperRecovery()
    {
        var f = new Fixture(); f.Runtime.Fail = true; var result = await f.Service.ApplyAsync(f.Profile.Id, true);
        Assert.Equal(StudioApplyStatus.PartiallyApplied, result.Status); Assert.True(f.Store.Journals[result.JournalId].WidgetsAttempted);
        f.Runtime.Fail = false; Assert.True((await f.Service.RestoreAsync(result.JournalId)).Restored); Assert.Equal(1, f.Runtime.Stopped);
    }
    [Fact] public async Task RepeatedProfilesRestoreInOrderWithoutLosingOriginalDesktop()
    {
        var f = new Fixture(); var first = await f.Service.ApplyAsync(f.Profile.Id, true);
        var secondProfile = new DesktopProfile { Wallpapers = [new("A", @"C:\second.png")] }; f.Store.Profiles.Add(secondProfile);
        var second = await f.Service.ApplyAsync(secondProfile.Id, true);
        Assert.False((await f.Service.RestoreAsync(first.JournalId)).Restored);
        Assert.True((await f.Service.RestoreAsync(second.JournalId)).Restored); Assert.Equal(@"C:\selected.png", f.Wallpaper.Values["A"]);
        Assert.True((await f.Service.RestoreAsync(first.JournalId)).Restored); Assert.Equal(@"C:\original-a.png", f.Wallpaper.Values["A"]);
    }
    [Fact] public async Task ManagedPortableImagePathIsResolvedBeforeJournalAndMutation()
    {
        var f = new Fixture(); f.Images.ResolvedPath = @"D:\portable\images\safe.png";
        var result = await f.Service.ApplyAsync(f.Profile.Id, true);
        Assert.Equal(f.Images.ResolvedPath, f.Wallpaper.Values["A"]);
        Assert.Equal(f.Images.ResolvedPath, f.Store.Journals[result.JournalId].Intended.Wallpapers[0].ImagePath);
    }
    [Fact] public async Task PreviousConsentDoesNotAuthorizeANewStaticWallpaperApply()
    {
        var f = new Fixture(); await f.Service.ApplyAsync(f.Profile.Id, true);
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.ApplyAsync(f.Profile.Id, false));
        Assert.Equal(1, f.Wallpaper.Sets);
    }
    [Fact] public async Task StartupPendingInspectionIsReadOnlyAndNeverReapplies()
    {
        var f = new Fixture(); var before = await f.Wallpaper.CaptureAsync();
        var journal = new StudioRecoveryJournal { ProfileId = f.Profile.Id, Status = StudioApplyStatus.Applying, Before = before, Intended = new(f.Profile.Wallpapers, before.Position, true), AttemptedMonitorIds = ["A"] };
        f.Store.Journals.Add(journal.Id, journal);
        Assert.Single(await f.Service.GetPendingAsync()); Assert.Equal(0, f.Wallpaper.Sets); Assert.Equal(0, f.Runtime.Applied); Assert.Equal(0, f.Store.JournalWrites);
    }
}
