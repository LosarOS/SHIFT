using Microsoft.Data.Sqlite;
using Shift.Application;
using Shift.Domain;
using Shift.Infrastructure;
using Xunit;

namespace Shift.Tests;

public class WorkspaceCompositionPersistenceTests
{
    [Fact] public async Task ReviewedAppendPersistsWithoutChangingExistingMetadataOrHistory()
    {
        var root = Path.Combine(Path.GetTempPath(), "SHIFT-compose-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var store = new SqliteStore(Path.Combine(root, "shift.db"));
            await store.InitializeAsync();
            var original = new Workspace { Name = "Local work", Description = "Привет\nkeep", Tags = ["project"], IsFavorite = true,
                Items = [new() { Name = "Existing", Kind = ItemKind.Url, Target = "https://existing.example/", Order = 8, Note = "keep" }] };
            await store.SaveAsync(original);
            var current = Assert.Single(await store.ListAsync());
            var preview = WorkspaceComposer.Preview(current, "https://one.example/\nhttps://two.example/", ItemKind.Url);
            Assert.Single((await store.ListAsync())[0].Items);
            Assert.Empty(await store.HistoryAsync());
            var updated = WorkspaceComposer.Apply(current, preview, [2], 250, false);
            await store.SaveAsync(updated);
            var reopened = Assert.Single(await new SqliteStore(store.DatabasePath).ListAsync());
            Assert.Equal(2, reopened.Items.Count); Assert.Equal("Привет\nkeep", reopened.Description);
            Assert.True(reopened.IsFavorite); Assert.Equal(new[] { "project" }, reopened.Tags);
            Assert.Equal(current.Items[0].Id, reopened.Items[0].Id); Assert.Equal("keep", reopened.Items[0].Note);
            Assert.Equal("https://two.example/", reopened.Items[1].Target); Assert.Equal(250, reopened.Items[1].DelayMs); Assert.False(reopened.Items[1].Enabled);
            Assert.Empty(await store.HistoryAsync());
            Assert.Throws<ValidationException>(() => WorkspaceComposer.Apply(reopened, preview, [2]));
            Assert.Equal(2, (await store.ListAsync())[0].Items.Count);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(root, true);
        }
    }
}
