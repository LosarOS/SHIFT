using Shift.Domain;
namespace Shift.Application;

public interface IStudioStore
{
    Task<IReadOnlyList<DesktopProfile>> ListProfilesAsync(CancellationToken token = default);
    Task<DesktopProfile> SaveProfileAsync(DesktopProfile profile, CancellationToken token = default);
    Task DeleteProfileAsync(Guid id, CancellationToken token = default);
    Task<IReadOnlyList<WidgetDefinition>> ListDefinitionsAsync(CancellationToken token = default);
    Task<WidgetDefinition> SaveDefinitionAsync(WidgetDefinition definition, CancellationToken token = default);
    Task DeleteDefinitionAsync(Guid id, CancellationToken token = default);
    Task<WidgetRuntimeState?> GetWidgetStateAsync(Guid instanceId, CancellationToken token = default);
    Task<WidgetRuntimeState> SaveWidgetStateAsync(WidgetRuntimeState state, CancellationToken token = default);
    Task<StudioSettings> LoadStudioSettingsAsync(CancellationToken token = default);
    Task SaveStudioSettingsAsync(StudioSettings settings, CancellationToken token = default);
    Task<IReadOnlyList<StudioRecoveryJournal>> ListRecoveryJournalsAsync(CancellationToken token = default);
    Task SaveRecoveryJournalAsync(StudioRecoveryJournal journal, CancellationToken token = default);
}
public interface IMonitorTopology
{
    Task<IReadOnlyList<MonitorIdentity>> GetMonitorsAsync(CancellationToken token = default);
}
public interface IWallpaperService
{
    Task<WallpaperSnapshot> CaptureAsync(CancellationToken token = default);
    Task SetWallpaperAsync(WallpaperAssignment assignment, CancellationToken token = default);
    Task SetPositionAsync(WallpaperPlacement position, CancellationToken token = default);
}
public interface IStudioImageLibrary
{
    Task<string> ImportAsync(string sourcePath, CancellationToken token = default);
    Task ValidateAsync(string imagePath, CancellationToken token = default);
}
public interface ISystemMetricsProvider
{
    SystemMetrics Sample();
}
public interface IWidgetRuntime : IAsyncDisposable
{
    int WindowCount { get; }
    Task ApplyAsync(DesktopProfile profile, IReadOnlyList<WidgetDefinition> definitions, CancellationToken token = default);
    Task StopAsync(CancellationToken token = default);
    Task SetInteractionAsync(WidgetInteraction interaction, CancellationToken token = default);
    Task GatherAsync(CancellationToken token = default);
}
public interface IDesktopProfileService
{
    Task<StudioApplyResult> ApplyAsync(Guid profileId, bool wallpaperConsent, CancellationToken token = default);
}
public interface IStudioRecoveryService
{
    Task<IReadOnlyList<StudioRecoveryJournal>> GetPendingAsync(CancellationToken token = default);
    Task<StudioRestoreResult> RestoreAsync(Guid journalId, bool overwriteConflicts = false, CancellationToken token = default);
    Task KeepCurrentAsync(Guid journalId, CancellationToken token = default);
}
