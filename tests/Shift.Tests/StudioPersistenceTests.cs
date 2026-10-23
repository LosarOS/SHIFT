using Microsoft.Data.Sqlite;
using Shift.Domain;
using Shift.Infrastructure;
using Xunit;
namespace Shift.Tests;

public sealed class StudioPersistenceTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "shift-studio-db-" + Guid.NewGuid().ToString("N"));
    private async Task<SqliteStore> Store(string name="shift.db")
    {
        var store=new SqliteStore(Path.Combine(root,name)); await store.InitializeAsync();return store;
    }
    private static async Task<object?> Sql(string path,string sql)
    {
        await using var db=new SqliteConnection(new SqliteConnectionStringBuilder {DataSource=path,Pooling=false}.ToString());await db.OpenAsync();
        using var cmd=db.CreateCommand();cmd.CommandText=sql;return await cmd.ExecuteScalarAsync();
    }
    private async Task<(SqliteStore Store, Workspace Workspace)> Legacy()
    {
        var store=await Store();var workspace=new Workspace {Name="Существующий RC2",Tags=["Работа"],Items=[new() {Name="Documentation",Kind=ItemKind.Url,Target="https://example.com"}]};
        await store.SaveAsync(workspace);await store.SaveSettingsAsync(new() {Language="en",Theme=AppTheme.Forest});
        await store.SaveRunAsync(new(Guid.NewGuid(),workspace.Id,workspace.Name,DateTimeOffset.UtcNow,null,RunStatus.Running,[]));
        await Sql(store.DatabasePath,"""
            DROP TABLE StudioWidgetState;DROP TABLE StudioInstances;DROP TABLE StudioDefinitions;DROP TABLE StudioSettings;
            DROP TABLE StudioWallpapers;DROP TABLE StudioProfiles;DROP TABLE StudioRecovery;PRAGMA user_version=1;
            """);
        return(store,workspace);
    }
    private async Task<(WidgetDefinition Definition, DesktopProfile Profile)> Profile(SqliteStore store)
    {
        var definition=await store.SaveDefinitionAsync(new(){Title="Локальная заметка",Kind=WidgetKind.Notes});
        var profile=await store.SaveProfileAsync(new(){Name="Development",Wallpapers=[new("display-1","C:\\Фон\\изображение.png")],Widgets=[new(){DefinitionId=definition.Id}]});
        return(definition,profile);
    }
    [Fact]
    public async Task Migration_captures_v1_before_schema_change_and_preserves_workspace_settings_and_history()
    {
        var (store,workspace)=await Legacy(); await store.InitializeAsync();
        Assert.Equal(2L,await Sql(store.DatabasePath,"PRAGMA user_version"));
        Assert.NotNull(store.LastMigrationBackupPath);Assert.True(File.Exists(store.LastMigrationBackupPath));
        Assert.Equal(1L,await Sql(store.LastMigrationBackupPath!,"PRAGMA user_version"));
        Assert.Equal(0L,await Sql(store.LastMigrationBackupPath!,"SELECT count(*) FROM sqlite_master WHERE name='StudioProfiles'"));
        Assert.Equal(workspace.Id,Assert.Single(await store.ListAsync()).Id);Assert.Equal("en",(await store.LoadSettingsAsync()).Language);
        Assert.Equal(RunStatus.Running,Assert.Single(await store.HistoryAsync()).Status);
        Assert.Empty(await store.ListProfilesAsync());Assert.False((await store.LoadStudioSettingsAsync()).Enabled);
        await store.InitializeAsync();Assert.Single(Directory.GetFiles(Path.Combine(root,"migration-backups"),"*.db"));
    }
    [Fact]
    public async Task Unversioned_nonempty_database_is_refused_without_creating_shift_schema()
    {
        Directory.CreateDirectory(root);var path=Path.Combine(root,"unknown.db");await Sql(path,"CREATE TABLE OwnerData(Value TEXT);INSERT INTO OwnerData VALUES('keep')");
        await Assert.ThrowsAsync<InvalidDataException>(()=>new SqliteStore(path).InitializeAsync());
        Assert.Equal("keep",await Sql(path,"SELECT Value FROM OwnerData"));Assert.Equal(0L,await Sql(path,"PRAGMA user_version"));
        Assert.Equal(0L,await Sql(path,"SELECT count(*) FROM sqlite_master WHERE name='Workspaces'"));
    }
    [Fact]
    public async Task Failed_migration_backup_leaves_v1_schema_and_data_untouched()
    {
        var(store,workspace)=await Legacy();File.WriteAllText(Path.Combine(root,"migration-backups"),"owned obstruction");
        await Assert.ThrowsAnyAsync<IOException>(()=>store.InitializeAsync());
        Assert.Equal(1L,await Sql(store.DatabasePath,"PRAGMA user_version"));Assert.Equal(workspace.Id,Assert.Single(await store.ListAsync()).Id);
        Assert.Equal(0L,await Sql(store.DatabasePath,"SELECT count(*) FROM sqlite_master WHERE name='StudioProfiles'"));
    }
    [Fact]
    public async Task Schema_collision_rolls_back_entire_migration_and_retains_pre_migration_snapshot()
    {
        var(store,_)=await Legacy();await Sql(store.DatabasePath,"CREATE TABLE StudioProfiles(Unexpected INTEGER)");
        await Assert.ThrowsAsync<SqliteException>(()=>store.InitializeAsync());
        Assert.Equal(1L,await Sql(store.DatabasePath,"PRAGMA user_version"));Assert.NotNull(store.LastMigrationBackupPath);
        Assert.Equal(0L,await Sql(store.DatabasePath,"SELECT count(*) FROM sqlite_master WHERE name='StudioDefinitions'"));
        Assert.Single(await store.ListAsync());
    }
    [Fact]
    public async Task Profile_edits_preserve_existing_notes_timer_and_instance_identity()
    {
        var store=await Store();var(_,profile)=await Profile(store);var instance=Assert.Single(profile.Widgets);
        var state=await store.SaveWidgetStateAsync(new(){InstanceId=instance.Id,Notes="Не терять",Timer=new(){Running=true,RemainingSeconds=75,EndsAtUtc=DateTimeOffset.UtcNow.AddSeconds(75),SavedAtUtc=DateTimeOffset.UtcNow}});
        var saved=await store.SaveProfileAsync(profile with{Name="Updated",Widgets=[instance with{Layout=instance.Layout with{X=.25,Width=500}}]});
        Assert.Equal(2,saved.Revision);Assert.Equal(state,await store.GetWidgetStateAsync(instance.Id));
        var read=Assert.Single(await store.ListProfilesAsync());Assert.Equal(.25,Assert.Single(read.Widgets).Layout.X);
        Assert.Equal("C:\\Фон\\изображение.png",Assert.Single(read.Wallpapers).ImagePath);
    }
    [Fact]
    public async Task Stale_profile_definition_and_state_saves_cannot_overwrite_or_recreate_newer_data()
    {
        var store=await Store();var(definition,profile)=await Profile(store);var instance=profile.Widgets[0];
        await store.SaveDefinitionAsync(definition with{Title="new title"});await Assert.ThrowsAsync<InvalidOperationException>(()=>store.SaveDefinitionAsync(definition));
        await store.SaveProfileAsync(profile with{Name="new name"});await Assert.ThrowsAsync<InvalidOperationException>(()=>store.SaveProfileAsync(profile));
        var state=await store.SaveWidgetStateAsync(new(){InstanceId=instance.Id,Notes="first"});await store.SaveWidgetStateAsync(state with{Notes="newer"});
        await Assert.ThrowsAsync<InvalidOperationException>(()=>store.SaveWidgetStateAsync(state));Assert.Equal("newer",(await store.GetWidgetStateAsync(instance.Id))!.Notes);
        await store.DeleteProfileAsync(profile.Id);await Assert.ThrowsAsync<InvalidOperationException>(()=>store.SaveProfileAsync(profile));
        Assert.Empty(await store.ListProfilesAsync());
    }
    [Fact]
    public async Task Definition_delete_is_restricted_while_used_and_profile_delete_keeps_recovery_journal()
    {
        var store=await Store();var(definition,profile)=await Profile(store);
        var journal=new StudioRecoveryJournal{ProfileId=profile.Id,Before=new([new("display","before.jpg")],WallpaperPlacement.Fill,true),Intended=new([new("display","after.jpg")],WallpaperPlacement.Fill,true),AttemptedMonitorIds=["display"]};
        await store.SaveRecoveryJournalAsync(journal);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>store.DeleteDefinitionAsync(definition.Id));
        await store.SaveStudioSettingsAsync(new(){Enabled=true,ActiveProfileId=profile.Id});
        await store.DeleteProfileAsync(profile.Id);Assert.Null((await store.LoadStudioSettingsAsync()).ActiveProfileId);
        var restoredJournal=Assert.Single(await store.ListRecoveryJournalsAsync());Assert.Equal(journal.Id,restoredJournal.Id);Assert.Equal(journal.Before.Wallpapers.ToArray(),restoredJournal.Before.Wallpapers.ToArray());Assert.Equal(journal.Intended.Wallpapers.ToArray(),restoredJournal.Intended.Wallpapers.ToArray());Assert.Equal(journal.AttemptedMonitorIds.ToArray(),restoredJournal.AttemptedMonitorIds.ToArray());await store.DeleteDefinitionAsync(definition.Id);Assert.Empty(await store.ListDefinitionsAsync());
    }
    [Fact]
    public async Task Workspace_delete_detaches_profile_and_disables_auto_apply_without_deleting_history()
    {
        var store=await Store();var workspace=new Workspace{Name="Work"};await store.SaveAsync(workspace);
        var p=await store.SaveProfileAsync(new(){Name="Visual",WorkspaceId=workspace.Id,ApplyOnWorkspaceLaunch=true});
        await store.SaveRunAsync(new(Guid.NewGuid(),workspace.Id,workspace.Name,DateTimeOffset.UtcNow,DateTimeOffset.UtcNow,RunStatus.Completed,[]));
        await store.SaveAsync(workspace.Duplicate("Copy"));await store.DeleteAsync(workspace.Id);
        var detached=Assert.Single(await store.ListProfilesAsync());Assert.Null(detached.WorkspaceId);Assert.False(detached.ApplyOnWorkspaceLaunch);Assert.Equal(p.Revision+1,detached.Revision);Assert.Single(await store.HistoryAsync());
    }
    [Fact]
    public async Task Only_one_enabled_automatic_profile_per_workspace_is_allowed()
    {
        var store=await Store();var workspace=new Workspace{Name="One visual choice"};await store.SaveAsync(workspace);
        var first=await store.SaveProfileAsync(new(){Name="First",WorkspaceId=workspace.Id,ApplyOnWorkspaceLaunch=true});
        var second=new DesktopProfile{Name="Second",WorkspaceId=workspace.Id,ApplyOnWorkspaceLaunch=true};
        await Assert.ThrowsAsync<InvalidOperationException>(()=>store.SaveProfileAsync(second));
        second=await store.SaveProfileAsync(second with{Enabled=false});
        await Assert.ThrowsAsync<InvalidOperationException>(()=>store.SaveProfileAsync(second with{Enabled=true}));
        await store.SaveProfileAsync(first with{ApplyOnWorkspaceLaunch=false});await store.SaveProfileAsync(second with{Enabled=true});
        Assert.Single(await store.ListProfilesAsync(),p=>p.Enabled&&p.ApplyOnWorkspaceLaunch);
    }
    [Fact]
    public async Task Invalid_child_reference_rolls_back_profile_and_wallpaper_changes()
    {
        var store=await Store();var(_,profile)=await Profile(store);
        await Assert.ThrowsAsync<SqliteException>(()=>store.SaveProfileAsync(profile with{Name="must roll back",Wallpapers=[],Widgets=[new(){DefinitionId=Guid.NewGuid()}]}));
        var read=Assert.Single(await store.ListProfilesAsync());Assert.Equal(profile.Name,read.Name);Assert.Equal(profile.Revision,read.Revision);Assert.Single(read.Wallpapers);Assert.Single(read.Widgets);
    }
    [Fact]
    public async Task Runtime_state_cannot_cross_profiles_and_removed_instance_state_is_deleted()
    {
        var store=await Store();var(definition,profile)=await Profile(store);var instance=profile.Widgets[0];
        await store.SaveWidgetStateAsync(new(){InstanceId=instance.Id,Notes="private local note"});
        await Assert.ThrowsAsync<InvalidOperationException>(()=>store.SaveProfileAsync(new(){Name="Other",Widgets=[instance]}));Assert.Single(await store.ListProfilesAsync());
        await store.SaveProfileAsync(profile with{Widgets=[]});Assert.Null(await store.GetWidgetStateAsync(instance.Id));
        await Assert.ThrowsAsync<SqliteException>(()=>store.SaveWidgetStateAsync(new(){InstanceId=Guid.NewGuid()}));
    }
    [Fact]
    public async Task V2_backup_restore_preserves_studio_state_and_pre_restore_safety_copy()
    {
        var store=await Store();var(_,profile)=await Profile(store);var instance=profile.Widgets[0];
        await store.SaveWidgetStateAsync(new(){InstanceId=instance.Id,Notes="original note"});await store.SaveStudioSettingsAsync(new(){Enabled=true,ActiveProfileId=profile.Id,WallpaperConsentGranted=true});
        var backup=await store.BackupAsync(Path.Combine(root,"snapshots"));var state=(await store.GetWidgetStateAsync(instance.Id))!;await store.SaveWidgetStateAsync(state with{Notes="current note"});
        var result=await store.RestoreAsync(backup,Path.Combine(root,"safety"));Assert.Equal("original note",(await store.GetWidgetStateAsync(instance.Id))!.Notes);
        var safety=new SqliteStore(result.SafetyBackupPath);Assert.Equal("current note",(await safety.GetWidgetStateAsync(instance.Id))!.Notes);Assert.Equal(profile.Id,(await store.LoadStudioSettingsAsync()).ActiveProfileId);
    }
    [Fact]
    public async Task V1_backup_restore_migrates_and_retains_workspace_data()
    {
        var(legacy,workspace)=await Legacy();var backup=await legacy.BackupAsync(Path.Combine(root,"legacy"));var target=await Store("target.db");
        await target.RestoreAsync(backup,Path.Combine(root,"target-safety"));Assert.Equal(2L,await Sql(target.DatabasePath,"PRAGMA user_version"));
        Assert.Equal(workspace.Id,Assert.Single(await target.ListAsync()).Id);Assert.Empty(await target.ListProfilesAsync());
        Assert.Equal(RunStatus.Interrupted,Assert.Single(await target.HistoryAsync()).Status);
    }
    [Fact]
    public async Task V1_restore_migration_backup_failure_does_not_replace_working_database()
    {
        var(legacy,_)=await Legacy();var backup=await legacy.BackupAsync(Path.Combine(root,"legacy"));var target=await Store("target.db");
        await target.SaveAsync(new(){Name="Current must survive"});File.WriteAllText(Path.Combine(root,"migration-backups"),"owned obstruction");
        await Assert.ThrowsAnyAsync<IOException>(()=>target.RestoreAsync(backup,Path.Combine(root,"safety")));
        Assert.Equal(2L,await Sql(target.DatabasePath,"PRAGMA user_version"));Assert.Equal("Current must survive",Assert.Single(await target.ListAsync()).Name);
    }
    [Fact]
    public async Task V1_restore_schema_collision_does_not_replace_working_database()
    {
        var(legacy,_)=await Legacy();await Sql(legacy.DatabasePath,"CREATE TABLE StudioProfiles(Unexpected INTEGER)");var backup=await legacy.BackupAsync(Path.Combine(root,"legacy"));
        var target=await Store("target.db");await target.SaveAsync(new(){Name="Current must survive"});
        await Assert.ThrowsAsync<SqliteException>(()=>target.RestoreAsync(backup,Path.Combine(root,"safety")));
        Assert.Equal(2L,await Sql(target.DatabasePath,"PRAGMA user_version"));Assert.Equal("Current must survive",Assert.Single(await target.ListAsync()).Name);
    }
    [Theory]
    [InlineData("UPDATE StudioDefinitions SET Style='{\"Opacity\":-1}'")]
    [InlineData("UPDATE StudioInstances SET Layout='{\"X\":20}'")]
    [InlineData("DROP TABLE StudioWidgetState")]
    [InlineData("CREATE TRIGGER corrupt AFTER UPDATE ON Workspaces BEGIN DELETE FROM LaunchRuns; END")]
    [InlineData("UPDATE StudioDefinitions SET Content='{\"ExecutablePath\":\"cmd.exe\"}'")]
    public async Task Corrupt_or_executable_backup_is_rejected_before_replacement(string sql)
    {
        var store=await Store();await Profile(store);var backup=await store.BackupAsync(Path.Combine(root,"bad-backup"));await Sql(backup,sql);
        await store.SaveAsync(new(){Name="Keep current workspace"});
        await Assert.ThrowsAnyAsync<Exception>(()=>store.RestoreAsync(backup,Path.Combine(root,"safety")));Assert.Equal("Keep current workspace",Assert.Single(await store.ListAsync()).Name);
    }
    [Fact]
    public async Task Journal_updates_preserve_identity_and_do_not_apply_anything_during_initialize()
    {
        var store=await Store();var j=new StudioRecoveryJournal{ProfileId=Guid.NewGuid(),AttemptedMonitorIds=["monitor"]};await store.SaveRecoveryJournalAsync(j);
        await store.SaveRecoveryJournalAsync(j with{Status=StudioApplyStatus.Applying,AppliedMonitorIds=["monitor"]});await store.InitializeAsync();
        Assert.Equal(StudioApplyStatus.Applying,Assert.Single(await store.ListRecoveryJournalsAsync()).Status);
        await Assert.ThrowsAsync<InvalidDataException>(()=>store.SaveRecoveryJournalAsync(j with{ProfileId=Guid.NewGuid()}));
    }
    [Fact]
    public async Task Local_store_serializes_competing_revision_saves()
    {
        using var store=new LocalDataStore(Path.Combine(root,"local.db"));await store.InitializeAsync();var p=await store.SaveProfileAsync(new(){Name="Atomic"});
        var outcomes=await Task.WhenAll(Enumerable.Range(0,8).Select(async i=>{try{await store.SaveProfileAsync(p with{Name="Edit "+i});return true;}catch(InvalidOperationException){return false;}}));
        Assert.Single(outcomes,x=>x);Assert.Equal(2,Assert.Single(await store.ListProfilesAsync()).Revision);
    }
    [Fact]
    public async Task Invalid_or_cancelled_writes_leave_no_partial_studio_records()
    {
        var store=await Store();await Assert.ThrowsAsync<InvalidDataException>(()=>store.SaveDefinitionAsync(new(){Style=new(){Opacity=double.NaN}}));
        await Assert.ThrowsAsync<InvalidDataException>(()=>store.SaveProfileAsync(new(){Widgets=[null!]}));
        using var cancel=new CancellationTokenSource();cancel.Cancel();await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>store.SaveProfileAsync(new(),cancel.Token));
        Assert.Empty(await store.ListDefinitionsAsync());Assert.Empty(await store.ListProfilesAsync());
    }
    public void Dispose()
    {
        SqliteConnection.ClearAllPools();if(Directory.Exists(root))Directory.Delete(root,true);
    }
}
