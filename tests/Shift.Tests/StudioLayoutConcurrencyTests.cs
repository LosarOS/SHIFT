using Microsoft.Data.Sqlite;
using Shift.Application;
using Shift.Domain;
using Shift.Infrastructure;
using Xunit;

namespace Shift.Tests;

public sealed class StudioLayoutConcurrencyTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "shift-layout-cas-" + Guid.NewGuid().ToString("N"));
    private async Task<(SqliteStore Store, DesktopProfile Profile)> CreateAsync(int count = 1)
    {
        var store = new SqliteStore(Path.Combine(root, "shift.db")); await store.InitializeAsync();
        var definition = await store.SaveDefinitionAsync(new() { Title = "Local notes", Kind = WidgetKind.Notes });
        var profile = await store.SaveProfileAsync(new() { Name = "Original", Widgets = Enumerable.Range(0, count)
            .Select(_ => new WidgetInstance { DefinitionId = definition.Id }).ToArray() });
        foreach (var widget in profile.Widgets)
            await store.SaveWidgetStateAsync(new() { InstanceId = widget.Id, Notes = "Must survive — Я\nSecond line" });
        return (store, profile);
    }
    private static StudioLayoutEdit Move(WidgetInstance widget, double x = .25) => new(widget,
        widget.Layout with { X = x }, widget.Interaction);
    private static async Task<DesktopProfile> ReadAsync(SqliteStore store)
        => Assert.Single(await store.ListProfilesAsync());

    [Fact]
    public async Task MetadataBetweenReadAndLayoutWriteDoesNotInvalidateConfigurationOrLoseNotes()
    {
        var (store, before) = await CreateAsync();
        var at = new DateTimeOffset(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);
        Assert.True(await store.RecordProfileAppliedAsync(before.Id, at));
        Assert.Equal(before.Revision, (await ReadAsync(store)).Revision);
        var result = await store.SaveLayoutEditsAsync(before.Id, [Move(before.Widgets[0])]);
        Assert.Empty(result.Conflicts); Assert.Single(result.Saved);
        var after = await ReadAsync(store);
        Assert.Equal(at, after.LastAppliedAt); Assert.Equal(.25, after.Widgets[0].Layout.X);
        Assert.Equal(before.Revision + 1, after.Revision);
        Assert.Equal("Must survive — Я\nSecond line", (await store.GetWidgetStateAsync(before.Widgets[0].Id))!.Notes);
    }

    [Fact]
    public async Task RecordingOlderOrEqualInstantsKeepsMonotonicTimeWithoutRevisionChurn()
    {
        var (store, before) = await CreateAsync();
        var at = new DateTimeOffset(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);
        await store.RecordProfileAppliedAsync(before.Id, at);
        await store.RecordProfileAppliedAsync(before.Id, at.AddHours(-1));
        await store.RecordProfileAppliedAsync(before.Id, at.ToOffset(TimeSpan.FromHours(3)));
        var after = await ReadAsync(store);
        Assert.Equal(at, after.LastAppliedAt); Assert.Equal(before.Revision, after.Revision);
    }

    [Fact]
    public async Task AnEditorOpenedBeforeMetadataWritePreservesTheNewTimestamp()
    {
        var (store, editorSnapshot) = await CreateAsync();
        var at = DateTimeOffset.UtcNow;
        await store.RecordProfileAppliedAsync(editorSnapshot.Id, at);
        var saved = await store.SaveProfileAsync(editorSnapshot with { Name = "Editor title" });
        Assert.Equal(at, saved.LastAppliedAt); Assert.Equal(at, (await ReadAsync(store)).LastAppliedAt);
        Assert.Equal("Editor title", saved.Name); Assert.Equal(editorSnapshot.Revision + 1, saved.Revision);
    }

    [Fact]
    public async Task RecordingApplyCannotRecreateAConcurrentlyDeletedProfile()
    {
        var (store, profile) = await CreateAsync();
        await store.DeleteProfileAsync(profile.Id);
        Assert.False(await store.RecordProfileAppliedAsync(profile.Id, DateTimeOffset.UtcNow));
        Assert.Empty(await store.ListProfilesAsync());
        var result = await store.SaveLayoutEditsAsync(profile.Id, [Move(profile.Widgets[0])]);
        Assert.Empty(result.Saved); Assert.Equal([profile.Widgets[0].Id], result.Conflicts);
        Assert.Empty(await store.ListProfilesAsync());
    }

    [Fact]
    public async Task UnrelatedEditorChangesAndOtherInstancesSurviveTheAtomicLayoutWrite()
    {
        var (store, before) = await CreateAsync(2);
        var first = before.Widgets[0]; var other = before.Widgets[1];
        await store.SaveProfileAsync(before with { Name = "External title", Description = "External description",
            Widgets = [first, other with { Layout = other.Layout with { Y = .6 } }] });
        var result = await store.SaveLayoutEditsAsync(before.Id, [Move(first)]);
        Assert.Empty(result.Conflicts);
        var after = await ReadAsync(store);
        Assert.Equal("External title", after.Name); Assert.Equal("External description", after.Description);
        Assert.Equal(.25, after.Widgets[0].Layout.X); Assert.Equal(.6, after.Widgets[1].Layout.Y);
    }

    [Fact]
    public async Task SameInstanceEditorChangeRejectsStaleLiveGeometryOnEveryAttempt()
    {
        var (store, before) = await CreateAsync();
        var widget = before.Widgets[0];
        await store.SaveProfileAsync(before with { Widgets = [widget with { Layout = widget.Layout with { X = .8 } }] });
        for (var i = 0; i < 2; i++)
        {
            var result = await store.SaveLayoutEditsAsync(before.Id, [Move(widget)]);
            Assert.Empty(result.Saved); Assert.Equal([widget.Id], result.Conflicts);
        }
        Assert.Equal(.8, (await ReadAsync(store)).Widgets[0].Layout.X);
    }

    [Fact]
    public async Task PartialBatchWritesCompatibleInstanceAndLeavesConflictingOneUntouched()
    {
        var (store, before) = await CreateAsync(2);
        var first = before.Widgets[0]; var second = before.Widgets[1];
        var external = await store.SaveProfileAsync(before with { Widgets =
            [first with { Interaction = WidgetInteraction.Desktop }, second] });
        var result = await store.SaveLayoutEditsAsync(before.Id, [Move(first), Move(second, .4)]);
        Assert.Equal(first.Id, Assert.Single(result.Conflicts));
        Assert.Equal(second.Id, Assert.Single(result.Saved).Id);
        var after = await ReadAsync(store);
        Assert.Equal(WidgetInteraction.Desktop, after.Widgets[0].Interaction); Assert.Equal(first.Layout, after.Widgets[0].Layout);
        Assert.Equal(.4, after.Widgets[1].Layout.X); Assert.Equal(external.Revision + 1, after.Revision);
        Assert.Equal(1, (await store.GetWidgetStateAsync(first.Id))!.Revision);
    }

    [Fact]
    public async Task AnAlreadyAcknowledgedIntentDoesNotBumpConfigurationAgain()
    {
        var (store, before) = await CreateAsync(); var intent = Move(before.Widgets[0]);
        var first = await store.SaveLayoutEditsAsync(before.Id, [intent]);
        var revision = (await ReadAsync(store)).Revision;
        var again = await store.SaveLayoutEditsAsync(before.Id, [intent]);
        Assert.Equal(first.Saved.ToArray(), again.Saved.ToArray()); Assert.Empty(again.Conflicts);
        Assert.Equal(revision, (await ReadAsync(store)).Revision);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HiddenOrReplacedInstancesAreNotWrittenByAnOldWindow(bool replaceDefinition)
    {
        var (store, before) = await CreateAsync(); var widget = before.Widgets[0];
        var next = replaceDefinition
            ? widget with { DefinitionId = (await store.SaveDefinitionAsync(new() { Title = "Replacement", Kind = WidgetKind.Clock })).Id }
            : widget with { Visible = false };
        await store.SaveProfileAsync(before with { Widgets = [next] });
        var result = await store.SaveLayoutEditsAsync(before.Id, [Move(widget)]);
        Assert.Empty(result.Saved); Assert.Equal([widget.Id], result.Conflicts);
        Assert.Equal(next, (await ReadAsync(store)).Widgets[0]);
    }

    [Fact]
    public async Task RemovedInstanceIsNotReinsertedByItsStaleWindow()
    {
        var (store, before) = await CreateAsync();
        await store.SaveProfileAsync(before with { Widgets = [] });
        var result = await store.SaveLayoutEditsAsync(before.Id, [Move(before.Widgets[0])]);
        Assert.Empty(result.Saved); Assert.Single(result.Conflicts);
        Assert.Empty((await ReadAsync(store)).Widgets);
        Assert.Null(await store.GetWidgetStateAsync(before.Widgets[0].Id));
    }

    [Fact]
    public async Task ForeignProfileIdCannotMoveAnInstance()
    {
        var (store, before) = await CreateAsync();
        var foreign = await store.SaveProfileAsync(new() { Name = "Different profile" });
        var result = await store.SaveLayoutEditsAsync(foreign.Id, [Move(before.Widgets[0])]);
        Assert.Empty(result.Saved); Assert.Single(result.Conflicts);
        Assert.Equal(before.Widgets[0].Layout, (await store.ListProfilesAsync()).Single(p => p.Id == before.Id).Widgets[0].Layout);
    }

    [Fact]
    public async Task InvalidDuplicateBatchCannotPartiallyWrite()
    {
        var (store, before) = await CreateAsync(); var intent = Move(before.Widgets[0]);
        await Assert.ThrowsAsync<InvalidDataException>(() => store.SaveLayoutEditsAsync(before.Id, [intent, intent]));
        Assert.Equal(before.Revision, (await ReadAsync(store)).Revision);
        Assert.Equal(before.Widgets[0].Layout, (await ReadAsync(store)).Widgets[0].Layout);
    }

    [Fact]
    public async Task CancelledRequestsDoNotWriteMetadataOrLayout()
    {
        var (store, before) = await CreateAsync();
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.RecordProfileAppliedAsync(before.Id, DateTimeOffset.UtcNow, cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.SaveLayoutEditsAsync(before.Id, [Move(before.Widgets[0])], cancellation.Token));
        var after = await ReadAsync(store); Assert.Null(after.LastAppliedAt);
        Assert.Equal(before.Revision, after.Revision); Assert.Equal(before.Widgets[0], after.Widgets[0]);
    }

    [Fact]
    public async Task WholeProfileCasStillRejectsAStaleConfigurationEditor()
    {
        var (store, before) = await CreateAsync();
        await store.SaveProfileAsync(before with { Name = "Newer editor" });
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.SaveProfileAsync(before with { Name = "Stale editor" }));
        Assert.Equal("Newer editor", (await ReadAsync(store)).Name);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
}
