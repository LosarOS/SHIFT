using Shift.Application;
using Shift.Domain;
using Xunit;

namespace Shift.Tests;

public class WorkspaceComposerTests
{
    private static Workspace Space(params WorkspaceItem[] items) => new() { Name = "Work", Description = "keep\ndescription", Tags = ["dev"], IsFavorite = true, Items = items };
    private static WorkspaceItem Link(string target = "https://existing.example/") => new() { Name = "Existing", Kind = ItemKind.Url, Target = target, Order = 7, DelayMs = 25, Note = "keep" };

    [Fact] public void PreviewFreezesSourceAndNeverChangesExistingItems()
    {
        var items = new[] { Link() }; var tags = new[] { "original" };
        var source = Space(items) with { Tags = tags };
        var preview = WorkspaceComposer.Preview(source, "https://new.example/a", ItemKind.Url);
        items[0] = items[0] with { Name = "changed" }; tags[0] = "changed";
        Assert.Equal("Existing", preview.Source.Items[0].Name);
        Assert.Equal("original", preview.Source.Tags[0]);
        Assert.False(preview.Entries.Single().Item!.Enabled);
        Assert.Throws<NotSupportedException>(() => ((IList<WorkspaceItem>)preview.Source.Items)[0] = Link());
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r")]
    [InlineData("\r\n")]
    public void AllEditorLineEndingsPreserveOriginalRowNumbers(string newline)
    {
        var p = WorkspaceComposer.Preview(Space(), "  \"https://one.example/a\"  " + newline + newline + "https://two.example/b", ItemKind.Url);
        Assert.Equal(new[] { 1, 3 }, p.Entries.Select(e => e.Line));
        Assert.Equal(2, p.Ready);
        Assert.Equal("https://one.example/a", p.Entries[0].Target);
    }

    [Fact] public void UrlDuplicateComparisonPreservesCaseSensitivePathsAndQueries()
    {
        var p = WorkspaceComposer.Preview(Space(Link("https://EXAMPLE.org/A")), "https://example.org/A\nhttps://example.org/a\nhttps://example.org/a?q=X\nhttps://EXAMPLE.org/a?q=x\nhttps://example.org/a?q=X", ItemKind.Url);
        Assert.Equal(new[] { CompositionState.Duplicate, CompositionState.Ready, CompositionState.Ready, CompositionState.Ready, CompositionState.Duplicate }, p.Entries.Select(e => e.State));
    }

    [Fact] public void LocalPathsAreComparedAsWindowsTargetsAcrossSeparators()
    {
        var existing = new WorkspaceItem { Name = "Project", Kind = ItemKind.Folder, Target = @"C:\Projects\One" };
        var p = WorkspaceComposer.Preview(Space(existing), "c:/projects/ONE/\nC:\\Projects\\Two", ItemKind.Folder);
        Assert.Equal(1, p.Duplicates); Assert.Equal(1, p.Ready);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("file:///C:/data.txt")]
    [InlineData("ftp://example.org/file")]
    [InlineData("https://user:password@example.org/")]
    [InlineData("not a URL")]
    public void UnsafeOrUnsupportedUrlRowsCannotBeSelected(string target)
    {
        var p = WorkspaceComposer.Preview(Space(), target, ItemKind.Url);
        Assert.Equal(CompositionState.Invalid, p.Entries.Single().State);
        Assert.Throws<ValidationException>(() => WorkspaceComposer.Apply(p.Source, p, [1]));
    }

    [Theory]
    [InlineData(@"\\server\share\file.txt")]
    [InlineData(@"C:\data.txt:stream")]
    [InlineData(@"relative\file.txt")]
    [InlineData(@"C:\NUL.txt")]
    [InlineData(@"C:\script.cmd")]
    [InlineData(@"C:\setup.msi")]
    public void InvalidLocalTargetsAndExecutableDocumentsAreRejected(string target)
    {
        Assert.Equal(CompositionState.Invalid, WorkspaceComposer.Preview(Space(), target, ItemKind.File).Entries.Single().State);
    }

    [Theory]
    [InlineData(@"C:\Windows\System32\cmd.exe")]
    [InlineData(@"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe")]
    [InlineData(@"C:\app.exe --hidden")]
    public void ApplicationsDoNotInferShellsOrArguments(string target)
    {
        Assert.Equal(0, WorkspaceComposer.Preview(Space(), target, ItemKind.App).Ready);
    }

    [Theory]
    [InlineData(ItemKind.Command)]
    [InlineData((ItemKind)999)]
    public void CommandsAndUnknownKindsAreNotBulkInput(ItemKind kind) =>
        Assert.Throws<ValidationException>(() => WorkspaceComposer.Preview(Space(), "anything", kind));

    [Fact] public void QuotedUnicodeAppPathStaysLiteralWithoutInventedArguments()
    {
        var p = WorkspaceComposer.Preview(Space(), "\"C:\\Проекты\\Мой редактор.exe\"", ItemKind.App);
        var item = p.Entries.Single().Item!;
        Assert.Equal(1, p.Ready); Assert.Equal(@"C:\Проекты\Мой редактор.exe", item.Target);
        Assert.Equal("", item.Arguments); Assert.Equal("", item.WorkingDirectory);
        Assert.Equal("Мой редактор.exe", item.Name);
    }

    [Fact] public void LongUnicodeNamesDoNotSplitASurrogatePair()
    {
        var p = WorkspaceComposer.Preview(Space(), "C:\\" + new string('Я', 99) + "🙂.txt", ItemKind.File);
        var name = p.Entries.Single().Item!.Name;
        Assert.Equal(99, name.Length); Assert.False(char.IsHighSurrogate(name[^1]));
    }

    [Theory]
    [InlineData("")]
    [InlineData("\r\n \t\n")]
    [InlineData("https://example.org/\0")]
    public void EmptyAndNullContainingInputIsRejected(string text) =>
        Assert.Throws<ValidationException>(() => WorkspaceComposer.Preview(Space(), text, ItemKind.Url));

    [Fact] public void WholeInputAndNonemptyLineLimitsAreBounded()
    {
        Assert.Throws<ValidationException>(() => WorkspaceComposer.Preview(Space(), new string('x', WorkspaceComposer.MaximumInput + 1), ItemKind.File));
        Assert.Throws<ValidationException>(() => WorkspaceComposer.Preview(Space(), string.Join("\n", Enumerable.Repeat("https://example.org/", 101)), ItemKind.Url));
        var p = WorkspaceComposer.Preview(Space(), string.Join("\n", Enumerable.Range(0, 100).Select(n => "https://example.org/" + n)), ItemKind.Url);
        Assert.Equal(100, p.Ready);
    }

    [Fact] public void ApplyingSelectionPreservesMetadataAndExistingContentInDispatchOrder()
    {
        var original = Link();
        var source = Space(original);
        var p = WorkspaceComposer.Preview(source, "https://one.example/\nhttps://two.example/", ItemKind.Url);
        var updated = WorkspaceComposer.Apply(source, p, [2, 1], 500, true);
        Assert.Equal(source.Id, updated.Id); Assert.Equal(source.Name, updated.Name); Assert.Equal(source.Description, updated.Description);
        Assert.Equal(source.Tags, updated.Tags); Assert.Equal(source.CreatedAt, updated.CreatedAt); Assert.True(updated.IsFavorite);
        Assert.Equal(original with { Order = 0 }, updated.Items[0]);
        Assert.Equal(new[] { 0, 1, 2 }, updated.Items.Select(i => i.Order));
        Assert.All(updated.Items.Skip(1), i => { Assert.True(i.Enabled); Assert.Equal(500, i.DelayMs); });
        Assert.Equal("https://one.example/", updated.Items[1].Target);
        Assert.Single(source.Items); Assert.Equal(7, source.Items[0].Order);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("1,1")]
    [InlineData("99")]
    public void SelectionMustReferToDistinctReadyPreviewRows(string rows)
    {
        var p = WorkspaceComposer.Preview(Space(), "https://one.example/", ItemKind.Url);
        Assert.Throws<ValidationException>(() => WorkspaceComposer.Apply(p.Source, p, rows.Split(',').Select(int.Parse).ToArray()));
    }

    [Fact] public void DuplicateRowsAndEmptySelectionCannotBeCommitted()
    {
        var p = WorkspaceComposer.Preview(Space(), "https://one.example/\nhttps://one.example/", ItemKind.Url);
        Assert.Throws<ValidationException>(() => WorkspaceComposer.Apply(p.Source, p, [2]));
        Assert.Throws<ValidationException>(() => WorkspaceComposer.Apply(p.Source, p, []));
    }

    [Fact] public void WorkspaceLimitAndDelayBoundsApplyAtCommit()
    {
        var source = Space(Enumerable.Range(0, 99).Select(i => Link("https://example.org/" + i) with { Order = i }).ToArray());
        var p = WorkspaceComposer.Preview(source, "https://one.example/\nhttps://two.example/", ItemKind.Url);
        Assert.Equal(1, p.RemainingSlots);
        Assert.Throws<ValidationException>(() => WorkspaceComposer.Apply(source, p, [1, 2]));
        Assert.Throws<ValidationException>(() => WorkspaceComposer.Apply(source, p, [1], -1));
        Assert.Throws<ValidationException>(() => WorkspaceComposer.Apply(source, p, [1], 300001));
        Assert.Equal(100, WorkspaceComposer.Apply(source, p, [2], 300000).Items.Count);
    }

    [Fact] public void AnyConcurrentWorkspaceChangeMakesThePreviewStale()
    {
        var source = Space(Link()); var p = WorkspaceComposer.Preview(source, "https://new.example/", ItemKind.Url);
        Workspace[] changes = [source with { Name = "Other" }, source with { Id = Guid.NewGuid() },
            source with { Tags = ["other"] }, source with { UpdatedAt = source.UpdatedAt.AddTicks(1) },
            source with { Items = [source.Items[0] with { Enabled = false }] }];
        foreach (var changed in changes) Assert.Throws<ValidationException>(() => WorkspaceComposer.Apply(changed, p, [1]));
    }

    [Fact] public void ApplyingAnAlreadyCommittedPreviewDoesNotAppendItTwice()
    {
        var source = Space(); var p = WorkspaceComposer.Preview(source, "https://new.example/", ItemKind.Url);
        var updated = WorkspaceComposer.Apply(source, p, [1]);
        Assert.Throws<ValidationException>(() => WorkspaceComposer.Apply(updated, p, [1]));
    }

    [Fact] public void MissingLocalFileCanBePreparedBecauseThisIsNotAnExistenceCheck()
    {
        var p = WorkspaceComposer.Preview(Space(), @"Z:\never-probed\document.txt", ItemKind.File);
        Assert.Equal(1, p.Ready);
    }
}
