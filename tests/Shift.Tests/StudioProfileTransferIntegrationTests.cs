using System.Text.Json;
using Microsoft.Data.Sqlite;
using Shift.Application;
using Shift.Domain;
using Shift.Infrastructure;
using Xunit;

namespace Shift.Tests;

public sealed class StudioProfileTransferIntegrationTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "shift-pack-integration-" + Guid.NewGuid().ToString("N"));
    private string Database => Path.Combine(root, "shift.db");

    private async Task<SqliteStore> Store()
    {
        var store = new SqliteStore(Database); await store.InitializeAsync(); return store;
    }

    private static string ExportPack()
    {
        var clock = new WidgetDefinition { Title = "Часы", Kind = WidgetKind.Clock, Style = StudioTemplates.For("Neon") };
        var notes = new WidgetDefinition { Title = "Заметки", Kind = WidgetKind.Notes, Content = new() { InitialNotes = "Private template" } };
        return StudioProfileTransfer.Export(new DesktopProfile
        {
            Name = "Переданный профиль", WorkspaceId = Guid.NewGuid(),
            Widgets = [new() { DefinitionId = clock.Id }, new() { DefinitionId = notes.Id }],
            Wallpapers = [new("source display", @"C:\Private\wallpaper.png")]
        }, [clock, notes]);
    }

    private async Task<(Guid Definition, Guid Profile, Guid Instance)> SeedOwner(SqliteStore store)
    {
        var workspace = new Workspace
        {
            Name = "Рабочее пространство владельца", IsFavorite = true, Tags = ["Личное", "Работа"],
            Items = [new() { Name = "Команда только с подтверждением", Kind = ItemKind.Command, Target = "echo unchanged", Shell = CommandShell.Cmd }]
        };
        await store.SaveAsync(workspace);
        var started = DateTimeOffset.UtcNow.AddMinutes(-1);
        await store.SaveRunAsync(new(Guid.NewGuid(), workspace.Id, workspace.Name, started, started.AddSeconds(1), RunStatus.Declined,
            [new(workspace.Items[0].Id, workspace.Items[0].Name, ItemKind.Command, 0, ItemStatus.Skipped, "Declined")]));
        await store.SaveSettingsAsync(new() { Language = "en", Theme = AppTheme.Forest, Notifications = false, OnboardingComplete = true });
        var definition = await store.SaveDefinitionAsync(new() { Kind = WidgetKind.Notes, Title = "Owner notes" });
        var profile = await store.SaveProfileAsync(new()
        {
            Name = "Owner profile", WorkspaceId = workspace.Id,
            Widgets = [new() { DefinitionId = definition.Id }]
        });
        await store.SaveWidgetStateAsync(new() { InstanceId = profile.Widgets[0].Id, Notes = "Не перезаписывать" });
        await store.SaveStudioSettingsAsync(new() { Enabled = true, ActiveProfileId = profile.Id, WallpaperConsentGranted = true });
        return (definition.Id, profile.Id, profile.Widgets[0].Id);
    }

    private static async Task<string> OwnerSnapshot(SqliteStore store, (Guid Definition, Guid Profile, Guid Instance) owner)
        => JsonSerializer.Serialize(new
        {
            Workspaces = await store.ListAsync(), History = await store.HistoryAsync(),
            Settings = await store.LoadSettingsAsync(), StudioSettings = await store.LoadStudioSettingsAsync(),
            Profile = (await store.ListProfilesAsync()).Single(p => p.Id == owner.Profile),
            Definition = (await store.ListDefinitionsAsync()).Single(d => d.Id == owner.Definition),
            State = await store.GetWidgetStateAsync(owner.Instance), Journals = await store.ListRecoveryJournalsAsync()
        });

    private async Task<object?> Sql(string sql)
    {
        await using var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Database, Pooling = false }.ToString());
        await db.OpenAsync(); using var command = db.CreateCommand(); command.CommandText = sql;
        return await command.ExecuteScalarAsync();
    }

    [Fact]
    public async Task Export_import_save_preserves_owner_workspaces_commands_settings_history_and_active_runtime_state()
    {
        var store = await Store(); var owner = await SeedOwner(store);
        var before = await OwnerSnapshot(store, owner);
        var draft = StudioProfileTransfer.Import(ExportPack());
        Assert.Equal(before, await OwnerSnapshot(store, owner));
        var saved = await ((IStudioPresetStore)store).SavePresetAsync(draft);
        Assert.Equal(before, await OwnerSnapshot(store, owner));
        Assert.Equal(2, (await store.ListProfilesAsync()).Count);
        Assert.Equal(3, (await store.ListDefinitionsAsync()).Count);
        Assert.Equal(2, saved.Widgets.Count); Assert.Equal(1, saved.Revision);
        Assert.Null(saved.WorkspaceId); Assert.False(saved.ApplyOnWorkspaceLaunch); Assert.Empty(saved.Wallpapers);
        foreach (var instance in saved.Widgets)
        {
            var definition = Assert.Single(await store.ListDefinitionsAsync(), d => d.Id == instance.DefinitionId);
            Assert.Empty(definition.Content.InitialNotes);
            Assert.Null(await store.GetWidgetStateAsync(instance.Id));
        }
        Assert.Equal("ok", await Sql("PRAGMA integrity_check"));
        Assert.Null(await Sql("PRAGMA foreign_key_check"));
    }

    [Fact]
    public async Task Same_json_imported_three_times_creates_independent_graphs_without_reusing_any_identity()
    {
        var store = await Store(); var owner = await SeedOwner(store); var before = await OwnerSnapshot(store, owner);
        var json = ExportPack(); var saved = new List<DesktopProfile>();
        for (var i = 0; i < 3; i++) saved.Add(await store.SavePresetAsync(StudioProfileTransfer.Import(json)));
        var definitions = (await store.ListDefinitionsAsync()).Where(d => d.Id != owner.Definition).ToArray();
        Assert.Equal(6, definitions.Length); Assert.Equal(4, (await store.ListProfilesAsync()).Count);
        var identities = saved.SelectMany(p => new[] { p.Id }.Concat(p.Widgets.Select(w => w.Id)))
            .Concat(definitions.Select(d => d.Id)).ToArray();
        Assert.Equal(15, identities.Length); Assert.Equal(15, identities.Distinct().Count());
        Assert.All(saved, p => Assert.All(p.Widgets, w => Assert.Single(definitions, d => d.Id == w.DefinitionId)));
        Assert.Equal(before, await OwnerSnapshot(store, owner));
    }

    [Fact]
    public async Task Existing_profile_identity_collision_rolls_back_new_imported_definitions_without_overwriting_first_import()
    {
        var store = await Store(); var owner = await SeedOwner(store); var before = await OwnerSnapshot(store, owner);
        var json = ExportPack(); var first = await store.SavePresetAsync(StudioProfileTransfer.Import(json));
        var firstSnapshot = JsonSerializer.Serialize(new { Profiles = await store.ListProfilesAsync(), Definitions = await store.ListDefinitionsAsync() });
        var second = StudioProfileTransfer.Import(json);
        second = second with { Profile = second.Profile with { Id = first.Id } };
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.SavePresetAsync(second));
        Assert.Equal(firstSnapshot, JsonSerializer.Serialize(new { Profiles = await store.ListProfilesAsync(), Definitions = await store.ListDefinitionsAsync() }));
        Assert.Equal(before, await OwnerSnapshot(store, owner));
        Assert.DoesNotContain(await store.ListDefinitionsAsync(), d => second.Definitions.Any(incoming => incoming.Id == d.Id));
        // A rejected import remains a draft; it has not adopted the existing profile or its instance IDs.
        Assert.All(second.Profile.Widgets, w => Assert.DoesNotContain(first.Widgets, existing => existing.Id == w.Id));
    }

    [Fact]
    public async Task Late_instance_failure_rolls_back_entire_import_and_leaves_owner_graph_untouched()
    {
        var store = await Store(); var owner = await SeedOwner(store); var before = await OwnerSnapshot(store, owner);
        await Sql("CREATE TRIGGER RejectLaterPackInstance BEFORE INSERT ON StudioInstances WHEN NEW.SortOrder=1 BEGIN SELECT RAISE(ABORT,'injected late pack failure'); END;");
        var draft = StudioProfileTransfer.Import(ExportPack());
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.SavePresetAsync(draft));
        Assert.Equal(before, await OwnerSnapshot(store, owner));
        Assert.Equal(owner.Profile, Assert.Single(await store.ListProfilesAsync()).Id);
        Assert.Equal(owner.Definition, Assert.Single(await store.ListDefinitionsAsync()).Id);
        Assert.Equal(1L, await Sql("SELECT COUNT(*) FROM StudioInstances"));
        Assert.Equal(1L, await Sql("SELECT COUNT(*) FROM StudioWidgetState"));
    }

    [Fact]
    public async Task Canceled_save_of_valid_import_creates_no_studio_records_and_does_not_enable_or_activate_studio()
    {
        var store = await Store(); var draft = StudioProfileTransfer.Import(ExportPack());
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.SavePresetAsync(draft, cancellation.Token));
        Assert.Empty(await store.ListProfilesAsync()); Assert.Empty(await store.ListDefinitionsAsync());
        Assert.Empty(await store.ListRecoveryJournalsAsync());
        Assert.Equal(0L, await Sql("SELECT COUNT(*) FROM StudioInstances"));
        Assert.Equal(0L, await Sql("SELECT COUNT(*) FROM StudioWidgetState"));
        var settings = await store.LoadStudioSettingsAsync(); Assert.False(settings.Enabled); Assert.Null(settings.ActiveProfileId);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools(); if (Directory.Exists(root)) Directory.Delete(root, true);
    }
}
