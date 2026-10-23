using Shift.Domain;
namespace Shift.Infrastructure;

public sealed partial class LocalDataStore
{
    public Task<IReadOnlyList<DesktopProfile>> ListProfilesAsync(CancellationToken token=default)=>ExecuteAsync(()=>inner.ListProfilesAsync(token),token);
    public Task<DesktopProfile> SaveProfileAsync(DesktopProfile profile,CancellationToken token=default)=>ExecuteAsync(()=>inner.SaveProfileAsync(profile,token),token);
    public Task DeleteProfileAsync(Guid id,CancellationToken token=default)=>ExecuteAsync(()=>inner.DeleteProfileAsync(id,token),token);
    public Task<IReadOnlyList<WidgetDefinition>> ListDefinitionsAsync(CancellationToken token=default)=>ExecuteAsync(()=>inner.ListDefinitionsAsync(token),token);
    public Task<WidgetDefinition> SaveDefinitionAsync(WidgetDefinition definition,CancellationToken token=default)=>ExecuteAsync(()=>inner.SaveDefinitionAsync(definition,token),token);
    public Task DeleteDefinitionAsync(Guid id,CancellationToken token=default)=>ExecuteAsync(()=>inner.DeleteDefinitionAsync(id,token),token);
    public Task<WidgetRuntimeState?> GetWidgetStateAsync(Guid id,CancellationToken token=default)=>ExecuteAsync(()=>inner.GetWidgetStateAsync(id,token),token);
    public Task<WidgetRuntimeState> SaveWidgetStateAsync(WidgetRuntimeState state,CancellationToken token=default)=>ExecuteAsync(()=>inner.SaveWidgetStateAsync(state,token),token);
    public Task<StudioSettings> LoadStudioSettingsAsync(CancellationToken token=default)=>ExecuteAsync(()=>inner.LoadStudioSettingsAsync(token),token);
    public Task SaveStudioSettingsAsync(StudioSettings settings,CancellationToken token=default)=>ExecuteAsync(()=>inner.SaveStudioSettingsAsync(settings,token),token);
    public Task<IReadOnlyList<StudioRecoveryJournal>> ListRecoveryJournalsAsync(CancellationToken token=default)=>ExecuteAsync(()=>inner.ListRecoveryJournalsAsync(token),token);
    public Task SaveRecoveryJournalAsync(StudioRecoveryJournal journal,CancellationToken token=default)=>ExecuteAsync(()=>inner.SaveRecoveryJournalAsync(journal,token),token);
}
