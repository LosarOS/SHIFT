using System.Globalization;
using Shift.Application;
using Shift.Domain;
using Xunit;

namespace Shift.Tests;

public sealed class StudioNotesExportTests
{
    private static readonly CultureInfo Ru = CultureInfo.GetCultureInfo("ru-RU");
    private static readonly CultureInfo En = CultureInfo.GetCultureInfo("en-US");
    private static WidgetDefinition Note(string title = "Заметки", string initial = "initial") => new()
    { Title = title, Kind = WidgetKind.Notes, Content = new() { InitialNotes = initial } };
    private static DesktopProfile Profile(params WidgetInstance[] instances) => new() { Name = "Фокус 日本語", Widgets = instances };
    private static WidgetInstance Place(WidgetDefinition definition, bool visible = true) => new() { DefinitionId = definition.Id, Visible = visible };
    private static WidgetRuntimeState State(WidgetInstance instance, string text) => new() { InstanceId = instance.Id, Notes = text };

    [Fact]
    public void UnicodeAndWhitespaceArePreservedVerbatim()
    {
        var note = Note(); var instance = Place(note);
        const string text = "\n  Привет, 世界 👩‍💻\r\n\tважное\n\n";
        var result = StudioNotesExport.Export(Profile(instance), [note], [State(instance, text)], Ru);
        Assert.Contains(text, result);
        Assert.Contains("Фокус 日本語", result);
        Assert.Contains("Экземпляр: " + instance.Id, result);
        Assert.DoesNotContain("initial", result);
    }

    [Fact]
    public void SharedDefinitionKeepsDifferentInstanceNotesInProfileOrder()
    {
        var note = Note(); var first = Place(note); var second = Place(note);
        var entries = StudioNotesExport.GetNotes(Profile(second, first), [note], [State(first, "first"), State(second, "second")]);
        Assert.Equal([second.Id, first.Id], entries.Select(e => e.InstanceId));
        Assert.Equal(["second", "first"], entries.Select(e => e.Text));
        Assert.All(entries, e => Assert.False(e.UsesInitialText));
    }

    [Fact]
    public void SavedEmptyNoteOverridesInitialText()
    {
        var note = Note(initial: "secret initial"); var instance = Place(note);
        var entry = Assert.Single(StudioNotesExport.GetNotes(Profile(instance), [note], [State(instance, "")]));
        Assert.Equal("", entry.Text); Assert.False(entry.UsesInitialText);
        Assert.DoesNotContain("secret initial", StudioNotesExport.Export(Profile(instance), [note], [State(instance, "")], En));
    }

    [Fact]
    public void InitialTextIsUsedOnlyWhenNoStateExists()
    {
        var note = Note(initial: "first launch text"); var instance = Place(note);
        var entry = Assert.Single(StudioNotesExport.GetNotes(Profile(instance), [note], []));
        Assert.True(entry.UsesInitialText); Assert.Equal("first launch text", entry.Text);
        Assert.Contains("Initial text", StudioNotesExport.Export(Profile(instance), [note], [], En));
    }

    [Fact]
    public void HiddenNotesAreIncludedWithClearAnnotation()
    {
        var note = Note(); var instance = Place(note, false);
        var entry = Assert.Single(StudioNotesExport.GetNotes(Profile(instance), [note], [State(instance, "hidden text")]));
        Assert.False(entry.Visible);
        Assert.Contains("Скрытый виджет", StudioNotesExport.Export(Profile(instance), [note], [State(instance, "hidden text")], Ru));
    }

    [Fact]
    public void OtherWidgetsAndUnrelatedNotesAreNeverExported()
    {
        var note = Note(); var instance = Place(note);
        var otherNote = Note("private other profile", "private seed"); var otherInstance = Place(otherNote);
        var timer = new WidgetDefinition { Kind = WidgetKind.FocusTimer, Title = "private timer", Content = new() { InitialNotes = "private timer text" } };
        var timerInstance = Place(timer);
        var result = StudioNotesExport.Export(Profile(instance, timerInstance), [note, otherNote, timer],
            [State(instance, "selected text"), State(otherInstance, "private runtime"), State(timerInstance, "private timer state")], En);
        Assert.Contains("selected text", result); Assert.DoesNotContain("private", result);
    }

    [Fact]
    public void ExportDoesNotLeakProfileDescriptionPathsOrWorkspaceBinding()
    {
        var note = Note(); var instance = Place(note);
        var profile = Profile(instance) with { Description = "private description", WorkspaceId = Guid.NewGuid(),
            Wallpapers = [new("display", "C:\\private\\wallpaper.png")] };
        var result = StudioNotesExport.Export(profile, [note], [], En);
        Assert.DoesNotContain(profile.Description, result); Assert.DoesNotContain(profile.WorkspaceId.ToString()!, result);
        Assert.DoesNotContain("wallpaper", result); Assert.DoesNotContain("display", result);
    }

    [Fact]
    public void MissingDefinitionFailsInsteadOfSilentlyOmittingData()
    {
        var note = Note();
        Assert.Throws<InvalidDataException>(() => StudioNotesExport.Export(Profile(Place(note)), [], [], En));
    }

    [Fact]
    public void DuplicateDefinitionIdentityFails()
    {
        var note = Note();
        Assert.Throws<InvalidDataException>(() => StudioNotesExport.Export(Profile(Place(note)), [note, note with { Title = "different" }], [], En));
    }

    [Fact]
    public void DuplicateInstanceStateFails()
    {
        var note = Note(); var instance = Place(note);
        Assert.Throws<InvalidDataException>(() => StudioNotesExport.Export(Profile(instance), [note], [State(instance, "one"), State(instance, "two")], En));
    }

    [Fact]
    public void CorruptNoteCannotBeExportedAsSuccess()
    {
        var note = Note(); var instance = Place(note);
        Assert.Throws<ValidationException>(() => StudioNotesExport.Export(Profile(instance), [note], [State(instance, "invalid\0text")], En));
    }

    [Fact]
    public void OverlongInitialNoteIsRejected()
    {
        var note = Note(initial: new string('x', 20001));
        Assert.Throws<ValidationException>(() => StudioNotesExport.Export(Profile(Place(note)), [note], [], En));
    }

    [Fact]
    public void NullOrEmptySuppliedIdentitiesFailClearly()
    {
        Assert.Throws<InvalidDataException>(() => StudioNotesExport.GetNotes(Profile(), [null!], []));
        Assert.Throws<InvalidDataException>(() => StudioNotesExport.GetNotes(Profile(), [], [new() { InstanceId = Guid.Empty }]));
    }

    [Fact]
    public void ExcessiveSuppliedRecordsAreBounded()
    {
        var notes = Enumerable.Range(0, StudioNotesExport.MaxSuppliedRecords + 1).Select(_ => Note()).ToArray();
        Assert.Throws<InvalidDataException>(() => StudioNotesExport.GetNotes(Profile(), notes, []));
    }

    [Fact]
    public void AllSupportedNotesFitWithoutTruncation()
    {
        var note = Note(initial: new string('界', 20000));
        var instances = Enumerable.Range(0, 64).Select(_ => Place(note)).ToArray();
        var result = StudioNotesExport.Export(Profile(instances), [note], [], Ru);
        Assert.Equal(64 * 20000, result.Count(c => c == '界'));
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(result) <= StudioNotesExport.MaxBytes);
    }

    [Theory]
    [InlineData("ru-RU", "В этом профиле нет виджетов заметок.")]
    [InlineData("en-US", "This profile has no Notes widgets.")]
    public void EmptyProfileHasHonestLocalizedMessage(string culture, string expected)
        => Assert.Contains(expected, StudioNotesExport.Export(Profile(), [], [], CultureInfo.GetCultureInfo(culture)));

    [Fact]
    public void ExportDoesNotMutateSourceCollectionsOrRecords()
    {
        var note = Note(); var instance = Place(note); var state = State(instance, "saved");
        var instances = new List<WidgetInstance> { instance }; var definitions = new List<WidgetDefinition> { note };
        var states = new List<WidgetRuntimeState> { state }; var profile = Profile() with { Widgets = instances };
        var exported = StudioNotesExport.GetNotes(profile, definitions, states);
        Assert.Same(instance, Assert.Single(instances)); Assert.Same(note, Assert.Single(definitions)); Assert.Same(state, Assert.Single(states));
        instances.Clear(); definitions.Clear(); states.Clear();
        Assert.Equal("saved", Assert.Single(exported).Text);
    }
}
