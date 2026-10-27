using Microsoft.Data.Sqlite;
using Shift.Application;
using Shift.Domain;
using Shift.Infrastructure;
using Xunit;

namespace Shift.Tests;

public sealed class StudioPresetPersistenceTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "shift-preset-db-" + Guid.NewGuid().ToString("N"));
    private string DatabasePath => Path.Combine(root, "shift.db");
    private async Task<SqliteStore> Store()
    {
        var store = new SqliteStore(DatabasePath); await store.InitializeAsync(); return store;
    }
    private static StudioPresetDraft Draft()
    {
        var notes = new WidgetDefinition { Title = "  Мои заметки  ", Kind = WidgetKind.Notes, Content = new() { InitialNotes = "Локальный текст" } };
        var clock = new WidgetDefinition { Title = "Часы", Kind = WidgetKind.Clock };
        return new(new DesktopProfile { Name = "  Фокус  ", Widgets = [new() { DefinitionId = notes.Id }, new() { DefinitionId = clock.Id }] }, [notes, clock], []);
    }
    private async Task<object?> Sql(string sql)
    {
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = DatabasePath, Pooling = false }.ToString());
        await connection.OpenAsync(); using var command = connection.CreateCommand(); command.CommandText = sql; return await command.ExecuteScalarAsync();
    }
    [Fact]
    public async Task Serialized_store_roundtrip_preserves_safe_defaults_and_unicode_without_enabling_studio()
    {
        using var store = new LocalDataStore(DatabasePath); await store.InitializeAsync();
        var draft = Draft();
        var saved = await ((IStudioPresetStore)store).SavePresetAsync(draft);
        Assert.Equal("Фокус", saved.Name); Assert.Equal(1, saved.Revision);
        var loaded = Assert.Single(await store.ListProfilesAsync());
        Assert.Equal(saved, loaded with { Wallpapers = saved.Wallpapers, Widgets = saved.Widgets });
        Assert.Equal(draft.Profile.Widgets.Select(w => w.Id), loaded.Widgets.Select(w => w.Id));
        var definitions = await store.ListDefinitionsAsync(); Assert.Equal(2, definitions.Count);
        Assert.All(definitions, d => Assert.Equal(1, d.Revision));
        Assert.Contains(definitions, d => d.Title == "Мои заметки" && d.Content.InitialNotes == "Локальный текст");
        var settings = await store.LoadStudioSettingsAsync(); Assert.False(settings.Enabled); Assert.Null(settings.ActiveProfileId); Assert.False(settings.WallpaperConsentGranted);
        Assert.Null(loaded.WorkspaceId); Assert.False(loaded.ApplyOnWorkspaceLaunch); Assert.Empty(loaded.Wallpapers);
        Assert.Null(loaded.LastAppliedAt); Assert.Empty(await store.ListRecoveryJournalsAsync());
    }
    [Fact]
    public async Task Profile_collision_rolls_back_inserted_definitions_and_preserves_existing_notes_settings_and_history()
    {
        var store = await Store();
        var ownerDefinition = await store.SaveDefinitionAsync(new() { Title = "Owner", Kind = WidgetKind.Notes });
        var owner = await store.SaveProfileAsync(new() { Name = "Owner profile", Widgets = [new() { DefinitionId = ownerDefinition.Id }] });
        await store.SaveWidgetStateAsync(new() { InstanceId = owner.Widgets[0].Id, Notes = "Не потерять" });
        var studioSettings = new StudioSettings { Enabled = true, ActiveProfileId = owner.Id, WallpaperConsentGranted = true };
        await store.SaveStudioSettingsAsync(studioSettings);
        var workspace = new Workspace { Name = "Работа", Items = [new() { Name = "Docs", Kind = ItemKind.Url, Target = "https://example.com" }] };
        await store.SaveAsync(workspace);
        await store.SaveRunAsync(new(Guid.NewGuid(), workspace.Id, workspace.Name, DateTimeOffset.UtcNow, null, RunStatus.Running, []));
        var draft = Draft(); draft = draft with { Profile = draft.Profile with { Id = owner.Id } };
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.SavePresetAsync(draft));
        Assert.Equal(owner.Id, Assert.Single(await store.ListProfilesAsync()).Id);
        Assert.Equal(ownerDefinition, Assert.Single(await store.ListDefinitionsAsync()));
        Assert.Equal("Не потерять", (await store.GetWidgetStateAsync(owner.Widgets[0].Id))!.Notes);
        Assert.Equal(studioSettings, await store.LoadStudioSettingsAsync());
        Assert.Equal(workspace.Id, Assert.Single(await store.ListAsync()).Id); Assert.Single(await store.HistoryAsync());
    }
    [Fact]
    public async Task Definition_collision_does_not_overwrite_owner_definition_or_leave_a_partial_bundle()
    {
        var store = await Store(); var owner = await store.SaveDefinitionAsync(new() { Title = "Owner clock" });
        var draft = Draft(); var definitions = draft.Definitions.ToArray(); definitions[1] = definitions[1] with { Id = owner.Id };
        draft = draft with { Definitions = definitions, Profile = draft.Profile with { Widgets = [draft.Profile.Widgets[0], draft.Profile.Widgets[1] with { DefinitionId = owner.Id }] } };
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.SavePresetAsync(draft));
        Assert.Equal(owner, Assert.Single(await store.ListDefinitionsAsync())); Assert.Empty(await store.ListProfilesAsync());
    }
    [Fact]
    public async Task Instance_collision_rolls_back_new_profile_and_definitions_without_adopting_existing_instance()
    {
        var store = await Store(); var ownerDefinition = await store.SaveDefinitionAsync(new() { Title = "Owner clock" });
        var owner = await store.SaveProfileAsync(new() { Name = "Owner", Widgets = [new() { DefinitionId = ownerDefinition.Id }] });
        var draft = Draft(); draft = draft with { Profile = draft.Profile with { Widgets = [draft.Profile.Widgets[0], draft.Profile.Widgets[1] with { Id = owner.Widgets[0].Id }] } };
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.SavePresetAsync(draft));
        Assert.Equal(owner.Id, Assert.Single(await store.ListProfilesAsync()).Id);
        Assert.Equal(ownerDefinition, Assert.Single(await store.ListDefinitionsAsync()));
        Assert.Equal(owner.Widgets[0].Id, (await store.ListProfilesAsync())[0].Widgets[0].Id);
    }
    [Fact]
    public async Task Failure_in_later_instance_insert_rolls_back_entire_transaction()
    {
        var store = await Store();
        // Disposable fixture fault injection: failure occurs after definitions, profile and first instance were inserted.
        await Sql("CREATE TRIGGER RejectSecondPresetInstance BEFORE INSERT ON StudioInstances WHEN NEW.SortOrder=1 BEGIN SELECT RAISE(ABORT,'injected write failure'); END;");
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.SavePresetAsync(Draft()));
        Assert.Empty(await store.ListDefinitionsAsync()); Assert.Empty(await store.ListProfilesAsync());
        Assert.Equal(0L, await Sql("SELECT COUNT(*) FROM StudioInstances"));
    }
    [Theory]
    [InlineData("profile_revision")][InlineData("definition_revision")][InlineData("foreign_reference")]
    [InlineData("duplicate_definition")][InlineData("unused_definition")][InlineData("automatic")]
    [InlineData("wallpaper")][InlineData("workspace")][InlineData("null_definition")]
    [InlineData("invalid_style")][InlineData("empty_bundle")]
    public async Task Invalid_or_nonstarter_bundles_are_rejected_without_writes(string kind)
    {
        var store = await Store(); var d = Draft();
        d = kind switch
        {
            "profile_revision" => d with { Profile = d.Profile with { Revision = 1 } },
            "definition_revision" => d with { Definitions = [d.Definitions[0] with { Revision = 1 }, d.Definitions[1]] },
            "foreign_reference" => d with { Profile = d.Profile with { Widgets = [d.Profile.Widgets[0], d.Profile.Widgets[1] with { DefinitionId = Guid.NewGuid() }] } },
            "duplicate_definition" => d with { Definitions = [d.Definitions[0], d.Definitions[0]] },
            "unused_definition" => d with { Definitions = [.. d.Definitions, new WidgetDefinition { Title = "Orphan" }] },
            "automatic" => d with { Profile = d.Profile with { ApplyOnWorkspaceLaunch = true, WorkspaceId = Guid.NewGuid() } },
            "wallpaper" => d with { Profile = d.Profile with { Wallpapers = [new("display", @"C:\test.png")] } },
            "workspace" => d with { Profile = d.Profile with { WorkspaceId = Guid.NewGuid() } },
            "null_definition" => d with { Definitions = [null!, d.Definitions[1]] },
            "invalid_style" => d with { Definitions = [d.Definitions[0] with { Style = d.Definitions[0].Style with { Opacity = double.NaN } }, d.Definitions[1]] },
            "empty_bundle" => d with { Definitions = [], Profile = d.Profile with { Widgets = [] } },
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        await Assert.ThrowsAsync<InvalidDataException>(() => store.SavePresetAsync(d));
        Assert.Empty(await store.ListDefinitionsAsync()); Assert.Empty(await store.ListProfilesAsync());
    }
    [Fact]
    public async Task Precancelled_creation_does_not_even_create_the_database()
    {
        using var source = new CancellationTokenSource(); source.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new SqliteStore(DatabasePath).SavePresetAsync(Draft(), source.Token));
        Assert.False(File.Exists(DatabasePath));
    }
    public void Dispose()
    {
        SqliteConnection.ClearAllPools(); if (Directory.Exists(root)) Directory.Delete(root, true);
    }
}
