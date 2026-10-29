using System.Text;
using System.Text.Json.Nodes;
using Shift.Application;
using Shift.Domain;
using Shift.Infrastructure;
using Xunit;

namespace Shift.Tests;

public sealed class StudioProfileTransferTests
{
    private static (DesktopProfile Profile, WidgetDefinition[] Definitions) Source()
    {
        var definitions = Enum.GetValues<WidgetKind>().Select(kind => new WidgetDefinition
        {
            Kind = kind, Title = "Виджет " + kind + " 💜", Revision = 7,
            Style = StudioTemplates.For("Forest") with { TemplateId = "Мой зелёный", Opacity = .85, FontSize = 35 },
            Content = new WidgetContent { InitialNotes = "Личные заметки", FocusMinutes = 42, ShowSeconds = true }
        }).ToArray();
        var instances = definitions.Select((d, i) => new WidgetInstance
        {
            DefinitionId = d.Id, Visible = i != 1, Interaction = WidgetInteraction.ClickThrough,
            Layout = new WidgetLayout { MonitorId = @"\\?\DISPLAY#PrivateMonitor", X = i * .1, Y = .2, Width = 360, Height = 210, AlwaysOnTop = true }
        }).ToList();
        instances.Add(instances[0] with { Id = Guid.NewGuid(), Layout = instances[0].Layout with { X = .6 } });
        return (new DesktopProfile
        {
            Name = "Моё окружение 💜", Description = "Профиль для работы", Revision = 9,
            WorkspaceId = Guid.NewGuid(), LastAppliedAt = DateTimeOffset.UtcNow,
            ChangeWallpaperPosition = true, WallpaperPosition = WallpaperPlacement.Span,
            Wallpapers = [new("private display", @"C:\Private\Wallpaper.png")], Widgets = instances
        }, definitions);
    }
    private static string Pack(bool notes = false)
    {
        var source = Source(); return StudioProfileTransfer.Export(source.Profile, source.Definitions, notes);
    }
    private static string Change(string json, Action<JsonObject> edit)
    {
        var value = JsonNode.Parse(json)!.AsObject(); edit(value); return value.ToJsonString();
    }

    [Fact]
    public void Roundtrip_preserves_real_widget_types_styles_content_order_shared_definitions_and_normalized_geometry()
    {
        var source = Source();
        var draft = StudioProfileTransfer.Import(StudioProfileTransfer.Export(source.Profile, source.Definitions));
        StudioValidator.Validate(draft.Profile);
        Assert.Equal(source.Profile.Name, draft.Profile.Name); Assert.Equal(source.Profile.Description, draft.Profile.Description);
        Assert.Equal(4, draft.Definitions.Count); Assert.Equal(5, draft.Profile.Widgets.Count);
        Assert.Equal(draft.Profile.Widgets[0].DefinitionId, draft.Profile.Widgets[4].DefinitionId);
        for (var i = 0; i < 5; i++)
        {
            var before = source.Profile.Widgets[i]; var after = draft.Profile.Widgets[i];
            Assert.Equal(before.Visible, after.Visible);
            Assert.Equal(before.Layout with { MonitorId = "", AlwaysOnTop = false }, after.Layout);
            Assert.Equal(WidgetInteraction.Edit, after.Interaction);
            var definition = Assert.Single(draft.Definitions, d => d.Id == after.DefinitionId);
            var original = Assert.Single(source.Definitions, d => d.Id == before.DefinitionId);
            Assert.Equal(original.Style, definition.Style); Assert.Equal(original.Kind, definition.Kind);
            Assert.Equal(original.Content with { InitialNotes = "" }, definition.Content);
            Assert.Equal(original.Title, definition.Title); Assert.Equal(0, definition.Revision);
        }
    }

    [Fact]
    public void Export_omits_private_monitor_paths_wallpapers_workspace_ids_runtime_metadata_and_notes_by_default()
    {
        var source = Source(); var json = StudioProfileTransfer.Export(source.Profile, source.Definitions);
        Assert.DoesNotContain("Wallpaper.png", json); Assert.DoesNotContain("PrivateMonitor", json);
        Assert.DoesNotContain(source.Profile.Id.ToString(), json); Assert.DoesNotContain(source.Profile.WorkspaceId!.Value.ToString(), json);
        Assert.DoesNotContain("Revision", json); Assert.DoesNotContain("LastApplied", json);
        Assert.DoesNotContain("Running", json); Assert.DoesNotContain("RemainingSeconds", json);
        Assert.DoesNotContain("AlwaysOnTop", json); Assert.DoesNotContain("Interaction", json);
        Assert.DoesNotContain("MonitorId", json); Assert.DoesNotContain("ImagePath", json);
        Assert.All(source.Definitions, d => Assert.DoesNotContain(d.Id.ToString(), json));
        var document = JsonNode.Parse(json)!;
        Assert.False(document["InitialNotesIncluded"]!.GetValue<bool>());
        Assert.All(document["Definitions"]!.AsArray(), d => Assert.Equal("", d!["Content"]!["InitialNotes"]!.GetValue<string>()));
    }

    [Fact]
    public void Including_initial_note_templates_requires_explicit_export_option_and_is_disclosed_on_import()
    {
        var source = Source(); var draft = StudioProfileTransfer.Import(StudioProfileTransfer.Export(source.Profile, source.Definitions, true));
        Assert.All(draft.Definitions, d => Assert.Equal("Личные заметки", d.Content.InitialNotes));
        Assert.Contains(StudioProfileTransfer.InitialNotesIncluded, draft.Warnings);
        Assert.DoesNotContain(StudioProfileTransfer.InitialNotesIncluded, StudioProfileTransfer.Import(Pack()).Warnings);
    }

    [Fact]
    public void Repeated_imports_are_new_inert_graphs_and_do_not_mutate_source_or_expose_mutable_collections()
    {
        var source = Source(); var originalInstances = source.Profile.Widgets.ToArray();
        var json = StudioProfileTransfer.Export(source.Profile, source.Definitions);
        var first = StudioProfileTransfer.Import(json); var second = StudioProfileTransfer.Import(Encoding.UTF8.GetBytes(json));
        var ids = new[] { first, second }.SelectMany(d => new[] { d.Profile.Id }.Concat(d.Definitions.Select(w => w.Id)).Concat(d.Profile.Widgets.Select(w => w.Id))).ToArray();
        Assert.Equal(ids.Length, ids.Distinct().Count());
        Assert.DoesNotContain(Guid.Empty, ids);
        Assert.All(new[] { first, second }, d =>
        {
            Assert.Null(d.Profile.WorkspaceId); Assert.False(d.Profile.ApplyOnWorkspaceLaunch);
            Assert.Empty(d.Profile.Wallpapers); Assert.False(d.Profile.ChangeWallpaperPosition);
            Assert.Null(d.Profile.LastAppliedAt); Assert.Equal(0, d.Profile.Revision);
            Assert.Contains(StudioProfileTransfer.DisplayFallback, d.Warnings);
            Assert.Contains(StudioProfileTransfer.RuntimeExcluded, d.Warnings);
            Assert.Contains(StudioProfileTransfer.WallpapersExcluded, d.Warnings);
        });
        Assert.Throws<NotSupportedException>(() => ((IList<WidgetDefinition>)first.Definitions).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<WidgetInstance>)first.Profile.Widgets).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<string>)first.Warnings).Clear());
        Assert.Equal(originalInstances, source.Profile.Widgets);
        Assert.All(source.Definitions, d => Assert.Equal("Личные заметки", d.Content.InitialNotes));
    }

    [Fact]
    public void Library_only_definitions_are_not_exported_and_disabled_profiles_remain_disabled()
    {
        var source = Source(); var extra = new WidgetDefinition { Title = "Не передавать" };
        var draft = StudioProfileTransfer.Import(StudioProfileTransfer.Export(source.Profile with { Enabled = false }, [.. source.Definitions, extra]));
        Assert.Equal(4, draft.Definitions.Count); Assert.False(draft.Profile.Enabled);
        Assert.DoesNotContain(draft.Definitions, d => d.Title == extra.Title);
    }

    [Theory]
    [InlineData("root")][InlineData("profile")][InlineData("instance")][InlineData("layout")]
    [InlineData("definition")][InlineData("style")][InlineData("content")]
    public void Unknown_fields_at_every_level_are_rejected_including_executable_paths(string level)
    {
        var json = Change(Pack(), root =>
        {
            var target = level switch
            {
                "root" => root,
                "profile" => root["Profile"]!,
                "instance" => root["Profile"]!["Widgets"]![0]!,
                "layout" => root["Profile"]!["Widgets"]![0]!["Layout"]!,
                "definition" => root["Definitions"]![0]!,
                "style" => root["Definitions"]![0]!["Style"]!,
                "content" => root["Definitions"]![0]!["Content"]!,
                _ => throw new ArgumentOutOfRangeException(nameof(level))
            };
            target["ExecutablePath"] = @"C:\Windows\System32\cmd.exe";
        });
        Assert.Throws<InvalidDataException>(() => StudioProfileTransfer.Import(json));
    }

    [Theory]
    [InlineData("null")][InlineData("{}")][InlineData("[]")][InlineData("not json")]
    [InlineData("{\"Format\":\"SHIFT.StudioProfile\",\"Format\":\"SHIFT.StudioProfile\"}")]
    public void Empty_truncated_wrong_root_and_duplicate_json_properties_are_rejected(string json)
        => Assert.Throws<InvalidDataException>(() => StudioProfileTransfer.Import(json));

    [Theory]
    [InlineData("version")][InlineData("format")][InlineData("missing_required")][InlineData("null_profile")]
    [InlineData("null_definitions")][InlineData("null_definition")][InlineData("duplicate_key")][InlineData("unused_definition")]
    [InlineData("missing_reference")][InlineData("null_instance")][InlineData("null_layout")][InlineData("empty_widgets")]
    [InlineData("null_style")][InlineData("null_content")][InlineData("privacy_mismatch")]
    [InlineData("numeric_kind")][InlineData("unknown_kind")][InlineData("bad_size")][InlineData("bad_name")]
    public void Invalid_graphs_are_rejected_without_returning_a_partial_draft(string fault)
    {
        var json = Change(Pack(), root =>
        {
            switch (fault)
            {
                case "version": root["SchemaVersion"] = 999; break;
                case "format": root["Format"] = "Unknown"; break;
                case "missing_required": root.Remove("InitialNotesIncluded"); break;
                case "null_profile": root["Profile"] = null; break;
                case "null_definitions": root["Definitions"] = null; break;
                case "null_definition": root["Definitions"]![0] = null; break;
                case "duplicate_key": root["Definitions"]![1]!["Key"] = 1; break;
                case "unused_definition": root["Profile"]!["Widgets"]!.AsArray().RemoveAt(1); break;
                case "missing_reference": root["Profile"]!["Widgets"]![0]!["DefinitionKey"] = 999; break;
                case "null_instance": root["Profile"]!["Widgets"]![0] = null; break;
                case "null_layout": root["Profile"]!["Widgets"]![0]!["Layout"] = null; break;
                case "empty_widgets": root["Profile"]!["Widgets"] = new JsonArray(); break;
                case "null_style": root["Definitions"]![0]!["Style"] = null; break;
                case "null_content": root["Definitions"]![0]!["Content"] = null; break;
                case "privacy_mismatch": root["Definitions"]![0]!["Content"]!["InitialNotes"] = "Hidden"; break;
                case "numeric_kind": root["Definitions"]![0]!["Kind"] = 1; break;
                case "unknown_kind": root["Definitions"]![0]!["Kind"] = "Command"; break;
                case "bad_size": root["Profile"]!["Widgets"]![0]!["Layout"]!["Width"] = 100000; break;
                case "bad_name": root["Profile"]!["Name"] = ""; break;
                default: throw new ArgumentOutOfRangeException(nameof(fault));
            }
        });
        var failure = Record.Exception(() => StudioProfileTransfer.Import(json));
        Assert.True(failure is InvalidDataException or ValidationException, failure?.ToString() ?? "Expected validation failure.");
    }

    [Fact]
    public void Duplicate_nested_json_keys_and_excessive_nesting_are_rejected()
    {
        var json = Pack().Replace("\"Width\": 360", "\"Width\": 360, \"Width\": 160", StringComparison.Ordinal);
        Assert.NotEqual(Pack(), json);
        Assert.Throws<InvalidDataException>(() => StudioProfileTransfer.Import(json));
        Assert.Throws<InvalidDataException>(() => StudioProfileTransfer.Import(new string('[', 30) + "0" + new string(']', 30)));
    }

    [Fact]
    public void Byte_size_not_just_character_count_is_enforced_before_parsing()
    {
        Assert.Throws<InvalidDataException>(() => StudioProfileTransfer.Import(""));
        Assert.Throws<InvalidDataException>(() => StudioProfileTransfer.Import(new byte[StudioProfileTransfer.MaxBytes + 1]));
        var unicode = new string('я', StudioProfileTransfer.MaxBytes / 2 + 1);
        Assert.True(unicode.Length < StudioProfileTransfer.MaxBytes);
        Assert.Throws<InvalidDataException>(() => StudioProfileTransfer.Import(unicode));
        Assert.Throws<InvalidDataException>(() => StudioProfileTransfer.Import(new byte[] { 0xff, 0xfe, 0 }));
    }

    [Fact]
    public void Maximum_widget_count_is_supported_but_excessive_count_or_large_export_is_rejected()
    {
        var definitions = Enumerable.Range(0, 64).Select(i => new WidgetDefinition { Kind = WidgetKind.Notes, Title = "Notes " + i }).ToArray();
        var profile = new DesktopProfile { Widgets = definitions.Select(d => new WidgetInstance { DefinitionId = d.Id }).ToArray() };
        var draft = StudioProfileTransfer.Import(StudioProfileTransfer.Export(profile, definitions));
        Assert.Equal(64, draft.Definitions.Count); Assert.Equal(64, draft.Profile.Widgets.Count);
        var json = Change(StudioProfileTransfer.Export(profile, definitions), root => root["Profile"]!["Widgets"]!.AsArray().Add(root["Profile"]!["Widgets"]![0]!.DeepClone()));
        Assert.Throws<InvalidDataException>(() => StudioProfileTransfer.Import(json));
        definitions = definitions.Select(d => d with { Content = new() { InitialNotes = new string('я', 20000) } }).ToArray();
        Assert.Throws<InvalidDataException>(() => StudioProfileTransfer.Export(profile, definitions, true));
    }

    [Fact]
    public void Missing_ambiguous_or_invalid_source_graphs_cannot_be_exported()
    {
        var source = Source();
        Assert.Throws<InvalidDataException>(() => StudioProfileTransfer.Export(source.Profile, []));
        Assert.Throws<InvalidDataException>(() => StudioProfileTransfer.Export(source.Profile, [.. source.Definitions, source.Definitions[0]]));
        Assert.Throws<InvalidDataException>(() => StudioProfileTransfer.Export(source.Profile with { Widgets = [] }, source.Definitions));
        Assert.Throws<ValidationException>(() => StudioProfileTransfer.Export(source.Profile with { Widgets = [source.Profile.Widgets[0] with { Layout = new() { X = double.NaN } }] }, source.Definitions));
    }

    [Fact]
    public async Task Imported_draft_saves_atomically_without_altering_existing_data_settings_or_runtime_state()
    {
        var root = Path.Combine(Path.GetTempPath(), "shift-profile-pack-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var store = new LocalDataStore(Path.Combine(root, "shift.db")); await store.InitializeAsync();
            var ownerDefinition = await store.SaveDefinitionAsync(new() { Kind = WidgetKind.Notes, Title = "Owner" });
            var owner = await store.SaveProfileAsync(new() { Name = "Owner", Widgets = [new() { DefinitionId = ownerDefinition.Id }] });
            var state = await store.SaveWidgetStateAsync(new() { InstanceId = owner.Widgets[0].Id, Notes = "Не терять" });
            var settings = new StudioSettings { Enabled = true, ActiveProfileId = owner.Id, WallpaperConsentGranted = true };
            await store.SaveStudioSettingsAsync(settings);
            var draft = StudioProfileTransfer.Import(Pack());
            Assert.Single(await store.ListProfilesAsync()); // Parsing itself did not persist or activate anything.
            var saved = await ((IStudioPresetStore)store).SavePresetAsync(draft);
            Assert.Equal(2, (await store.ListProfilesAsync()).Count); Assert.Equal(5, (await store.ListDefinitionsAsync()).Count);
            Assert.Equal(settings, await store.LoadStudioSettingsAsync());
            Assert.Equal(state, await store.GetWidgetStateAsync(owner.Widgets[0].Id));
            foreach (var widget in saved.Widgets) Assert.Null(await store.GetWidgetStateAsync(widget.Id));
            Assert.Empty(await store.ListRecoveryJournalsAsync());
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
