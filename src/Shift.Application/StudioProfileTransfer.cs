using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Diagnostics.CodeAnalysis;
using Shift.Domain;

namespace Shift.Application;

/// <summary>
/// Bounded, declarative Studio profile exchange. A pack is a configuration, never a backup:
/// Windows settings, files, display identifiers, workspace links and runtime state are excluded.
/// Reading or creating a pack never opens a file, starts a widget or changes the desktop.
/// </summary>
public static class StudioProfileTransfer
{
    public const int MaxBytes = 2 * 1024 * 1024;
    public const int SchemaVersion = 1;
    public const string Format = "SHIFT.StudioProfile";
    public const string WallpapersExcluded = "Studio.Pack.WallpapersExcluded";
    public const string RuntimeExcluded = "Studio.Pack.RuntimeExcluded";
    public const string DisplayFallback = "Studio.Pack.DisplayFallback";
    public const string InitialNotesIncluded = "Studio.Pack.InitialNotesIncluded";

    private static readonly JsonSerializerOptions Json = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            MaxDepth = 16,
            AllowDuplicateProperties = false,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
        };
        options.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }

    /// <summary>
    /// Exports used widget definitions and normalized geometry. Initial note templates are opt-in;
    /// current desktop notes and timer progress are never read or included. Library-only definitions
    /// are omitted. Display IDs, topmost and click-through state are intentionally not portable.
    /// </summary>
    public static string Export(DesktopProfile profile, IReadOnlyList<WidgetDefinition> definitions,
        bool includeInitialNotes = false)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(definitions);
        StudioValidator.Validate(profile);
        Require(profile.Widgets.Count > 0, "A Studio profile pack must contain widgets.");

        // Snapshot caller-owned views before constructing a portable graph; never alter source records.
        var instances = profile.Widgets.ToArray();
        var source = definitions.ToArray();
        var usedIds = instances.Select(w => w.DefinitionId).Distinct().ToArray();
        var portableDefinitions = new List<PackDefinition>();
        var keys = new Dictionary<Guid, int>();
        foreach (var id in usedIds)
        {
            var matches = source.Where(d => d?.Id == id).ToArray();
            Require(matches.Length == 1, "Studio profile widget references are missing or ambiguous.");
            var definition = matches[0];
            StudioValidator.Validate(definition);
            var key = portableDefinitions.Count + 1;
            keys.Add(id, key);
            portableDefinitions.Add(new PackDefinition
            {
                Key = key, Title = definition.Title, Kind = definition.Kind,
                Style = definition.Style with { },
                Content = definition.Content with { InitialNotes = includeInitialNotes ? definition.Content.InitialNotes : "" }
            });
        }
        var pack = new Pack
        {
            Format = Format, SchemaVersion = SchemaVersion, InitialNotesIncluded = includeInitialNotes,
            Profile = new PackProfile
            {
                Name = profile.Name, Description = profile.Description, Enabled = profile.Enabled,
                Widgets = instances.Select(w => new PackInstance
                {
                    DefinitionKey = keys[w.DefinitionId], Visible = w.Visible,
                    Layout = new PortableLayout { X = w.Layout.X, Y = w.Layout.Y, Width = w.Layout.Width, Height = w.Layout.Height }
                }).ToArray()
            },
            Definitions = portableDefinitions.ToArray()
        };
        var bytes = JsonSerializer.SerializeToUtf8Bytes(pack, Json);
        Require(bytes.Length <= MaxBytes, "Studio profile pack exceeds the supported size.");
        return Encoding.UTF8.GetString(bytes);
    }

    public static StudioPresetDraft Import(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        Require(json.Length > 0 && json.Length <= MaxBytes && Encoding.UTF8.GetByteCount(json) <= MaxBytes,
            "Studio profile pack exceeds the supported size.");
        return Import(Encoding.UTF8.GetBytes(json));
    }

    /// <summary>Returns fresh insert-only identities suitable for IStudioPresetStore. No data is saved.</summary>
    public static StudioPresetDraft Import(ReadOnlySpan<byte> utf8)
    {
        Require(utf8.Length > 0 && utf8.Length <= MaxBytes, "Studio profile pack exceeds the supported size.");
        // Accept ordinary UTF-8 files saved with a BOM by Windows editors. The total
        // file bound still includes those bytes; no other encoding is inferred.
        if (utf8.StartsWith(new byte[] { 0xEF, 0xBB, 0xBF })) utf8 = utf8[3..];
        try
        {
            var pack = JsonSerializer.Deserialize<Pack>(utf8, Json);
            Require(pack is not null && pack.Format == Format && pack.SchemaVersion == SchemaVersion,
                "Unsupported Studio profile pack format or version.");
            Require(pack!.Profile is not null && pack.Definitions is not null
                && pack.Definitions.Length is >= 1 and <= 64
                && pack.Profile.Widgets is not null && pack.Profile.Widgets.Length is >= 1 and <= 64,
                "A Studio profile pack must contain 1–64 widgets and definitions.");
            Require(pack.Definitions!.All(d => d is not null && d.Key is >= 1 and <= 64)
                && pack.Definitions.Select(d => d.Key).Distinct().Count() == pack.Definitions.Length,
                "Studio profile pack contains invalid or duplicate definition keys.");
            Require(pack.Profile!.Widgets!.All(w => w is not null && w.Layout is not null)
                && pack.Definitions.Select(d => d.Key).ToHashSet().SetEquals(pack.Profile.Widgets.Select(w => w.DefinitionKey)),
                "Studio profile pack widget references do not match its definitions.");

            var definitions = new List<WidgetDefinition>();
            var identities = new Dictionary<int, Guid>();
            foreach (var source in pack.Definitions)
            {
                Require(source.Style is not null && source.Content is not null,
                    "Studio profile pack appearance and content are required.");
                Require(pack.InitialNotesIncluded || source.Content!.InitialNotes == "",
                    "Studio profile pack note privacy metadata does not match its content.");
                var definition = new WidgetDefinition
                {
                    Title = source.Title, Kind = source.Kind,
                    Style = source.Style! with { }, Content = source.Content! with { }
                };
                StudioValidator.Validate(definition);
                definitions.Add(definition);
                identities.Add(source.Key, definition.Id);
            }
            var profile = new DesktopProfile
            {
                Name = pack.Profile.Name, Description = pack.Profile.Description, Enabled = pack.Profile.Enabled,
                Widgets = Array.AsReadOnly(pack.Profile.Widgets.Select(w => new WidgetInstance
                {
                    DefinitionId = identities[w.DefinitionKey], Visible = w.Visible,
                    Interaction = WidgetInteraction.Edit,
                    Layout = new WidgetLayout
                    {
                        MonitorId = "", X = w.Layout.X, Y = w.Layout.Y,
                        Width = w.Layout.Width, Height = w.Layout.Height, AlwaysOnTop = false
                    }
                }).ToArray())
            };
            StudioValidator.Validate(profile);
            var warnings = new List<string> { WallpapersExcluded, RuntimeExcluded, DisplayFallback };
            if (pack.InitialNotesIncluded && definitions.Any(d => d.Content.InitialNotes.Length > 0)) warnings.Add(InitialNotesIncluded);
            return new(profile, definitions.AsReadOnly(), warnings.AsReadOnly());
        }
        catch (JsonException ex) { throw new InvalidDataException("Invalid declarative Studio profile pack.", ex); }
    }

    private static void Require([DoesNotReturnIf(false)] bool valid, string message)
    {
        if (!valid) throw new InvalidDataException(message);
    }

    // Deliberately separate from persistence models. Unknown members at every level are rejected;
    // there is no field capable of restoring wallpaper paths, processes, commands or workspace links.
    private sealed class Pack
    {
        public required string Format { get; init; }
        public required int SchemaVersion { get; init; }
        public required bool InitialNotesIncluded { get; init; }
        public required PackProfile Profile { get; init; }
        public required PackDefinition[] Definitions { get; init; }
    }
    private sealed class PackProfile
    {
        public required string Name { get; init; }
        public required string Description { get; init; }
        public required bool Enabled { get; init; }
        public required PackInstance[] Widgets { get; init; }
    }
    private sealed class PackDefinition
    {
        public required int Key { get; init; }
        public required string Title { get; init; }
        public required WidgetKind Kind { get; init; }
        public required WidgetStyle Style { get; init; }
        public required WidgetContent Content { get; init; }
    }
    private sealed class PackInstance
    {
        public required int DefinitionKey { get; init; }
        public required PortableLayout Layout { get; init; }
        public required bool Visible { get; init; }
    }
    private sealed class PortableLayout
    {
        public required double X { get; init; }
        public required double Y { get; init; }
        public required double Width { get; init; }
        public required double Height { get; init; }
    }
}
