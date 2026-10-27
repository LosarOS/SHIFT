using Microsoft.Data.Sqlite;
using Shift.Application;
using Shift.Domain;
using Shift.Infrastructure;
using Xunit;

namespace Shift.Tests;

public sealed class StudioWidgetEditorPersistenceTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "shift-widget-edit-" + Guid.NewGuid().ToString("N"));
    private string PathToDatabase => Path.Combine(root, "shift.db");
    private async Task<(SqliteStore Store, WidgetDefinition Definition, DesktopProfile Profile)> Fixture()
    {
        var store = new SqliteStore(PathToDatabase); await store.InitializeAsync();
        var definition = await store.SaveDefinitionAsync(new() { Title = "Исходная заметка", Kind = WidgetKind.Notes });
        var profile = await store.SaveProfileAsync(new() { Name = "Исходный профиль", Widgets = [new() { DefinitionId = definition.Id }] });
        await store.SaveWidgetStateAsync(new() { InstanceId = profile.Widgets[0].Id, Notes = "Личный текст", Timer = new() { RemainingSeconds = 123 } });
        return (store, definition, profile);
    }
    private static async Task<DesktopProfile> Profile(SqliteStore store) => Assert.Single(await store.ListProfilesAsync());
    private static async Task<WidgetDefinition> Definition(SqliteStore store) => Assert.Single(await store.ListDefinitionsAsync());
    [Fact]
    public async Task Stale_profile_rolls_back_an_existing_definition_edit_and_preserves_newer_profile()
    {
        var (store, definition, staleProfile) = await Fixture();
        var newest = await store.SaveProfileAsync(staleProfile with { Name = "Новая версия" });
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.SaveWidgetEditAsync(definition with { Title = "Не сохранять" }, staleProfile));
        Assert.Equal(definition, await Definition(store));
        var actual = await Profile(store); Assert.Equal(newest.Name, actual.Name); Assert.Equal(newest.Revision, actual.Revision);
        Assert.Equal("Личный текст", (await store.GetWidgetStateAsync(actual.Widgets[0].Id))!.Notes);
    }
    [Fact]
    public async Task Stale_profile_rolls_back_a_new_definition_instead_of_leaving_an_orphan()
    {
        var (store, original, staleProfile) = await Fixture();
        await store.SaveProfileAsync(staleProfile with { Description = "Newer" });
        var added = new WidgetDefinition { Title = "Новая запись" };
        var target = staleProfile with { Widgets = [.. staleProfile.Widgets, new() { DefinitionId = added.Id }] };
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.SaveWidgetEditAsync(added, target));
        Assert.Equal(original, await Definition(store)); Assert.Equal(2, (await Profile(store)).Revision);
    }
    [Fact]
    public async Task Stale_definition_cannot_update_the_profile_or_overwrite_the_newer_definition()
    {
        var (store, staleDefinition, profile) = await Fixture();
        var newest = await store.SaveDefinitionAsync(staleDefinition with { Title = "Новая версия" });
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.SaveWidgetEditAsync(staleDefinition with { Title = "Старый редактор" }, profile with { Name = "Не сохранять" }));
        Assert.Equal(newest, await Definition(store)); Assert.Equal(profile.Name, (await Profile(store)).Name); Assert.Equal(profile.Revision, (await Profile(store)).Revision);
    }
    [Fact]
    public async Task Editing_retained_instance_placement_preserves_its_notes_and_timer_state()
    {
        var (store, definition, profile) = await Fixture(); var instance = profile.Widgets[0];
        var state = await store.GetWidgetStateAsync(instance.Id);
        var target = profile with { Widgets = [instance with { Layout = instance.Layout with { X = .22, Width = 400 } }] };
        var saved = await store.SaveWidgetEditAsync(definition with { Title = "Изменено" }, target);
        Assert.Equal(2, saved.Revision); Assert.Equal("Изменено", saved.Title);
        var actual = await Profile(store); Assert.Equal(profile.Revision + 1, actual.Revision);
        Assert.Equal(instance.Id, actual.Widgets[0].Id); Assert.Equal(.22, actual.Widgets[0].Layout.X); Assert.Equal(400, actual.Widgets[0].Layout.Width);
        Assert.Equal(state, await store.GetWidgetStateAsync(instance.Id));
    }
    [Fact]
    public async Task New_definition_and_new_placement_commit_together_without_changing_other_instances()
    {
        var (store, original, profile) = await Fixture(); var added = new WidgetDefinition { Title = "Часы" };
        var placement = new WidgetInstance { DefinitionId = added.Id };
        var saved = await store.SaveWidgetEditAsync(added, profile with { Widgets = [.. profile.Widgets, placement] });
        Assert.Equal(1, saved.Revision); Assert.Equal(2, (await store.ListDefinitionsAsync()).Count);
        var actual = await Profile(store); Assert.Equal(2, actual.Widgets.Count); Assert.Equal(placement.Id, actual.Widgets[1].Id);
        Assert.Equal("Личный текст", (await store.GetWidgetStateAsync(profile.Widgets[0].Id))!.Notes);
        Assert.Contains(await store.ListDefinitionsAsync(), d => d == original);
    }
    [Fact]
    public async Task Foreign_reference_failure_rolls_back_definition_profile_and_cascaded_note_deletion()
    {
        var (store, definition, profile) = await Fixture(); var originalState = await store.GetWidgetStateAsync(profile.Widgets[0].Id);
        var target = profile with { Name = "Must roll back", Widgets = [new() { DefinitionId = definition.Id }, new() { DefinitionId = Guid.NewGuid() }] };
        await Assert.ThrowsAsync<SqliteException>(() => store.SaveWidgetEditAsync(definition with { Title = "Must roll back" }, target));
        Assert.Equal(definition, await Definition(store)); var actual = await Profile(store);
        Assert.Equal(profile.Revision, actual.Revision); Assert.Equal(profile.Name, actual.Name); Assert.Equal(profile.Widgets[0].Id, Assert.Single(actual.Widgets).Id);
        Assert.Equal(originalState, await store.GetWidgetStateAsync(profile.Widgets[0].Id));
    }
    [Fact]
    public async Task Definition_only_save_uses_the_serialized_optional_interface_without_touching_profile_or_settings()
    {
        var (_, definition, profile) = await Fixture();
        using var store = new LocalDataStore(PathToDatabase);
        var saved = await ((IStudioWidgetEditorStore)store).SaveWidgetEditAsync(definition with { Title = "  Только библиотека  " }, null);
        Assert.Equal("Только библиотека", saved.Title); Assert.Equal(2, saved.Revision);
        Assert.Equal(profile.Revision, Assert.Single(await store.ListProfilesAsync()).Revision);
        Assert.False((await store.LoadStudioSettingsAsync()).Enabled); Assert.Empty(await store.ListRecoveryJournalsAsync());
    }
    [Fact]
    public async Task A_target_that_does_not_include_the_edited_definition_is_rejected_without_any_save()
    {
        var (store, definition, profile) = await Fixture();
        var target = profile with { Widgets = [new() { DefinitionId = Guid.NewGuid() }] };
        await Assert.ThrowsAsync<InvalidDataException>(() => store.SaveWidgetEditAsync(definition with { Title = "No" }, target));
        Assert.Equal(definition, await Definition(store)); Assert.Equal(profile.Revision, (await Profile(store)).Revision);
    }
    [Fact]
    public async Task Cancellation_does_not_commit_either_half_of_the_edit()
    {
        var (store, definition, profile) = await Fixture(); using var cancel = new CancellationTokenSource(); cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.SaveWidgetEditAsync(definition with { Title = "No" }, profile with { Name = "No" }, cancel.Token));
        Assert.Equal(definition, await Definition(store)); Assert.Equal(profile.Name, (await Profile(store)).Name); Assert.Equal(profile.Revision, (await Profile(store)).Revision);
    }
    [Fact]
    public async Task A_deleted_profile_is_not_recreated_and_the_new_definition_is_not_left_behind()
    {
        var (store, original, profile) = await Fixture(); await store.DeleteProfileAsync(profile.Id);
        var added = new WidgetDefinition { Title = "Not persisted" };
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.SaveWidgetEditAsync(added, profile with { Widgets = [new() { DefinitionId = added.Id }] }));
        Assert.Empty(await store.ListProfilesAsync()); Assert.Equal(original, await Definition(store));
    }
    [Fact]
    public async Task Existing_definition_identity_cannot_be_adopted_as_a_new_definition()
    {
        var (store, definition, profile) = await Fixture();
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.SaveWidgetEditAsync(definition with { Revision = 0, Title = "No" }, profile));
        Assert.Equal(definition, await Definition(store)); Assert.Equal(profile.Revision, (await Profile(store)).Revision);
    }
    [Fact]
    public async Task A_new_profile_is_supported_with_the_same_revision_and_foreign_key_rules()
    {
        var store = new SqliteStore(PathToDatabase); await store.InitializeAsync();
        var definition = new WidgetDefinition { Title = "New" };
        var profile = new DesktopProfile { Name = "New", Widgets = [new() { DefinitionId = definition.Id }] };
        var saved = await store.SaveWidgetEditAsync(definition, profile);
        Assert.Equal(1, saved.Revision); Assert.Equal(1, (await Profile(store)).Revision); Assert.Equal(definition.Id, (await Profile(store)).Widgets[0].DefinitionId);
    }
    [Fact]
    public async Task Invalid_definition_cannot_change_the_profile()
    {
        var (store, definition, profile) = await Fixture();
        await Assert.ThrowsAsync<InvalidDataException>(() => store.SaveWidgetEditAsync(definition with { Style = definition.Style with { Opacity = double.NaN } }, profile with { Name = "No" }));
        Assert.Equal(definition, await Definition(store)); Assert.Equal(profile.Name, (await Profile(store)).Name);
    }
    public void Dispose()
    {
        SqliteConnection.ClearAllPools(); if (Directory.Exists(root)) Directory.Delete(root, true);
    }
}
