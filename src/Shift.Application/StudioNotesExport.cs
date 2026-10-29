using System.Globalization;
using System.Text;
using Shift.Domain;

namespace Shift.Application;

/// <summary>A snapshot of one note; runtime state belongs to an instance, not its shared definition.</summary>
public sealed record StudioNoteExportEntry(Guid InstanceId, string Title, string Text, bool Visible, bool UsesInitialText);

/// <summary>
/// Builds a local plain-text export without reading files, changing state or contacting a service.
/// The caller must flush pending runtime changes before obtaining the supplied snapshots.
/// </summary>
public static class StudioNotesExport
{
    public const int MaxBytes = 8 * 1024 * 1024;
    public const int MaxSuppliedRecords = 4096;

    public static IReadOnlyList<StudioNoteExportEntry> GetNotes(DesktopProfile profile,
        IReadOnlyList<WidgetDefinition> definitions, IReadOnlyList<WidgetRuntimeState> states)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(definitions);
        ArgumentNullException.ThrowIfNull(states);
        StudioValidator.Validate(profile);
        if (definitions.Count > MaxSuppliedRecords || states.Count > MaxSuppliedRecords)
            throw new InvalidDataException("Studio note export exceeds the supported record count.");

        // Snapshot collections before constructing the result. Never return user-owned mutable lists.
        var definitionSnapshot = definitions.ToArray();
        var stateSnapshot = states.ToArray();
        if (definitionSnapshot.Any(d => d is null || d.Id == Guid.Empty)
            || definitionSnapshot.Select(d => d.Id).Distinct().Count() != definitionSnapshot.Length)
            throw new InvalidDataException("Studio note export contains duplicate or invalid widget definitions.");
        if (stateSnapshot.Any(s => s is null || s.InstanceId == Guid.Empty)
            || stateSnapshot.Select(s => s.InstanceId).Distinct().Count() != stateSnapshot.Length)
            throw new InvalidDataException("Studio note export contains duplicate or invalid instance states.");

        var byDefinition = definitionSnapshot.ToDictionary(d => d.Id);
        var byInstance = stateSnapshot.ToDictionary(s => s.InstanceId);
        var notes = new List<StudioNoteExportEntry>();
        foreach (var instance in profile.Widgets.ToArray())
        {
            if (!byDefinition.TryGetValue(instance.DefinitionId, out var definition))
                throw new InvalidDataException("A widget definition required by this profile is missing.");
            StudioValidator.Validate(definition);
            if (definition.Kind != WidgetKind.Notes) continue;
            var hasState = byInstance.TryGetValue(instance.Id, out var state);
            if (hasState) StudioValidator.Validate(state!);
            notes.Add(new(instance.Id, definition.Title, hasState ? state!.Notes : definition.Content.InitialNotes,
                instance.Visible, !hasState));
        }
        return notes.AsReadOnly();
    }

    public static string Export(DesktopProfile profile, IReadOnlyList<WidgetDefinition> definitions,
        IReadOnlyList<WidgetRuntimeState> states, CultureInfo? culture = null)
    {
        var notes = GetNotes(profile, definitions, states);
        var ru = (culture ?? CultureInfo.CurrentCulture).TwoLetterISOLanguageName == "ru";
        var output = new StringBuilder();
        output.AppendLine(ru ? "SHIFT Studio — заметки" : "SHIFT Studio — notes");
        output.AppendLine(profile.Name);
        output.AppendLine();
        if (notes.Count == 0)
            output.AppendLine(ru ? "В этом профиле нет виджетов заметок." : "This profile has no Notes widgets.");
        foreach (var note in notes)
        {
            output.AppendLine("────────────────────────────────────────");
            output.AppendLine(note.Title);
            output.Append(ru ? "Экземпляр: " : "Instance: ").AppendLine(note.InstanceId.ToString("D"));
            if (!note.Visible) output.AppendLine(ru ? "Скрытый виджет" : "Hidden widget");
            if (note.UsesInitialText)
                output.AppendLine(ru ? "Начальный текст — сохранённого состояния ещё нет" : "Initial text — no saved state yet");
            output.AppendLine();
            output.Append(note.Text); // Preserve all note characters, including leading/trailing newlines.
            output.AppendLine();
            output.AppendLine();
        }
        var text = output.ToString();
        if (Encoding.UTF8.GetByteCount(text) > MaxBytes)
            throw new InvalidDataException("Studio note export exceeds the supported size.");
        return text;
    }
}
