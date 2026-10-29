using System.Text.Json;
using Shift.Application;
using Shift.Domain;
using Shift.Infrastructure;

/// <summary>Inspection of an existing, GUID-owned disposable Windows acceptance database only.</summary>
internal static class StudioSharingAcceptance
{
    private const string ExpectedName = "RC5 Перенос";
    private const string CancelledName = "RC5 Отменённый перенос";
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private static Guid Id(int n) => new($"00000000-0000-4000-8000-{n:D12}");

    public static async Task RunAsync(string stage, SqliteStore store, string data)
    {
        if (!OperatingSystem.IsWindows() || Environment.GetEnvironmentVariable("GITHUB_ACTIONS") != "true"
            || !File.Exists(store.DatabasePath)) throw new InvalidOperationException("Existing disposable Windows database required.");
        var baselinePath = Path.Combine(data, "studio-sharing-before.json");
        var packPath = Path.Combine(data, "studio-sharing-export.json");
        var notesPath = Path.Combine(data, "studio-sharing-notes.txt");
        var profiles = await store.ListProfilesAsync(); var definitions = await store.ListDefinitionsAsync();
        var source = profiles.Single(p => p.Id == Id(104));
        if (source.Widgets.Count != 4 || (await store.LoadStudioSettingsAsync()).ActiveProfileId != Id(110))
            throw new InvalidOperationException("Sharing probe requires the four-widget source and unchanged active ten-widget environment.");

        var exported = await ReadOwnedText(packPath, StudioProfileTransfer.MaxBytes);
        var imported = StudioProfileTransfer.Import(exported);
        using var document = JsonDocument.Parse(exported);
        var exportedStrings = Strings(document.RootElement).ToArray();
        if (document.RootElement.GetProperty("InitialNotesIncluded").GetBoolean())
            throw new InvalidOperationException("Default export included initial note text without opt-in.");
        if (imported.Profile.Widgets.Count != 4 || imported.Definitions.Count != 4 || imported.Definitions.Any(d => d.Content.InitialNotes.Length != 0)
            || exportedStrings.Any(value => value.Contains("Initial CI note", StringComparison.Ordinal)
                || value.Contains("Native CI note — Unicode", StringComparison.Ordinal)
                || value.Contains(source.Id.ToString(), StringComparison.OrdinalIgnoreCase))
            || source.Wallpapers.Any(w => exportedStrings.Any(value => value.Contains(w.ImagePath, StringComparison.OrdinalIgnoreCase)))
            || source.Widgets.Any(w => exportedStrings.Any(value => value.Contains(w.Id.ToString(), StringComparison.OrdinalIgnoreCase)
                || value.Contains(w.DefinitionId.ToString(), StringComparison.OrdinalIgnoreCase))))
            throw new InvalidOperationException("Native profile export did not preserve four declarations or excluded private paths, identities and notes.");
        var noteText = await ReadOwnedText(notesPath, 8 * 1024 * 1024);
        var noteStates = new List<WidgetRuntimeState>();
        foreach (var instance in source.Widgets)
            if (await store.GetWidgetStateAsync(instance.Id) is { } state) noteStates.Add(state);
        var expectedNoteText = StudioNotesExport.Export(source, definitions, noteStates, System.Globalization.CultureInfo.GetCultureInfo("ru-RU"));
        if (noteText != expectedNoteText) throw new InvalidOperationException("Saved Notes file differs from the complete authoritative export buffer.");
        if (!noteText.Contains("Native CI note — Unicode", StringComparison.Ordinal) || !noteText.Contains(source.Name, StringComparison.Ordinal))
            throw new InvalidOperationException("Actual note export is missing the saved Unicode text or source profile heading.");
        if ((await store.GetWidgetStateAsync(Id(4003)))?.Notes != "Native CI note — Unicode")
            throw new InvalidOperationException("Sharing changed the saved owner note.");

        if (stage == "sharing-before")
        {
            if (File.Exists(baselinePath) || profiles.Any(p => p.Name is ExpectedName or CancelledName))
                throw new InvalidOperationException("Sharing acceptance baseline already exists or target names are in use.");
            var baseline = new Baseline(profiles.Select(p => p.Id).ToArray(), definitions.Select(d => d.Id).ToArray(),
                await ProtectedData(store, profiles, definitions), exported, noteText);
            await File.WriteAllTextAsync(baselinePath, JsonSerializer.Serialize(baseline, Json));
            Console.WriteLine(JsonSerializer.Serialize(new { status = "passed", check = "sharing_baseline_and_privacy", profile_count = profiles.Count, definition_count = definitions.Count }, Json));
            return;
        }
        var before = JsonSerializer.Deserialize<Baseline>(await ReadOwnedText(baselinePath, 8 * 1024 * 1024), Json)
            ?? throw new InvalidOperationException("Missing sharing baseline.");
        if (before.Exported != exported || before.NoteText != noteText || profiles.Any(p => p.Name == CancelledName))
            throw new InvalidOperationException("Sharing changed its captured export or persisted a cancelled draft.");
        var originalProfiles = profiles.Where(p => before.ProfileIds.Contains(p.Id)).ToArray();
        var originalDefinitions = definitions.Where(d => before.DefinitionIds.Contains(d.Id)).ToArray();
        if (await ProtectedData(store, originalProfiles, originalDefinitions) != before.ProtectedData)
            throw new InvalidOperationException("Sharing changed existing profiles, definitions, workspace/commands, history, settings or owner note.");

        if (stage == "sharing-readonly")
        {
            if (profiles.Count != before.ProfileIds.Length || definitions.Count != before.DefinitionIds.Length)
                throw new InvalidOperationException("Invalid review or cancelled preview wrote Studio records.");
            Console.WriteLine(JsonSerializer.Serialize(new { status = "passed", check = "sharing_preview_cancel_readonly" }, Json));
            return;
        }
        if (stage != "sharing-check") throw new ArgumentException("Unknown sharing stage.");
        var created = profiles.Single(p => p.Name == ExpectedName);
        var newDefinitions = definitions.Where(d => !before.DefinitionIds.Contains(d.Id)).ToArray();
        if (profiles.Count != before.ProfileIds.Length + 1 || definitions.Count != before.DefinitionIds.Length + 4
            || before.ProfileIds.Contains(created.Id) || created.Revision != 1 || created.Widgets.Count != 4
            || created.WorkspaceId is not null || created.ApplyOnWorkspaceLaunch || created.ChangeWallpaperPosition
            || created.Wallpapers.Count != 0 || created.LastAppliedAt is not null || newDefinitions.Length != 4
            || newDefinitions.Any(d => d.Revision != 1) || newDefinitions.Select(d => d.Id).Distinct().Count() != 4)
            throw new InvalidOperationException("Native import did not atomically save one fresh, inert profile and four fresh definitions.");
        var oldInstanceIds = originalProfiles.SelectMany(p => p.Widgets).Select(w => w.Id).ToHashSet();
        foreach (var instance in created.Widgets)
        {
            var actual = newDefinitions.Single(d => d.Id == instance.DefinitionId);
            var expected = imported.Definitions.Single(d => d.Kind == actual.Kind);
            StudioValidator.Validate(instance.Layout);
            if (oldInstanceIds.Contains(instance.Id) || instance.Interaction != WidgetInteraction.Edit || instance.Layout.AlwaysOnTop
                || actual.Title != expected.Title || actual.Style != expected.Style || actual.Content != expected.Content
                || await store.GetWidgetStateAsync(instance.Id) is not null)
                throw new InvalidOperationException("Imported identities, appearance, content or runtime-state isolation differ from the reviewed pack.");
        }
        if (!newDefinitions.Select(d => d.Id).ToHashSet().SetEquals(created.Widgets.Select(w => w.DefinitionId)))
            throw new InvalidOperationException("Imported profile does not exclusively own its four imported definitions.");
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            schema_version = 1, status = "passed", profile_id = created.Id,
            profiles_added = 1, definitions_added = 4, owner_data_unchanged = true,
            no_automatic_apply = true, runtime_state_excluded = true, unicode_notes_export_verified = true,
            manual_acceptance = false
        }, Json));
    }

    private static async Task<string> ProtectedData(SqliteStore store, IReadOnlyList<DesktopProfile> profiles,
        IReadOnlyList<WidgetDefinition> definitions) => JsonSerializer.Serialize(new
        {
            Profiles = profiles.OrderBy(p => p.Id), Definitions = definitions.OrderBy(d => d.Id),
            Workspaces = await store.ListAsync(), History = await store.HistoryAsync(), Settings = await store.LoadSettingsAsync(),
            StudioSettings = await store.LoadStudioSettingsAsync(), Journals = await store.ListRecoveryJournalsAsync(),
            SavedOwnerNote = await store.GetWidgetStateAsync(Id(4003))
        }, Json);

    private static async Task<string> ReadOwnedText(string path, int limit)
    {
        var file = new FileInfo(path);
        if (!file.Exists || file.Length <= 0 || file.Length > limit) throw new InvalidOperationException("Missing or oversized sharing evidence.");
        return await File.ReadAllTextAsync(path);
    }

    private static IEnumerable<string> Strings(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.String) yield return value.GetString()!;
        else if (value.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in value.EnumerateObject()) foreach (var text in Strings(property.Value)) yield return text;
        }
        else if (value.ValueKind == JsonValueKind.Array)
            foreach (var item in value.EnumerateArray()) foreach (var text in Strings(item)) yield return text;
    }

    private sealed record Baseline(Guid[] ProfileIds, Guid[] DefinitionIds, string ProtectedData, string Exported, string NoteText);
}
