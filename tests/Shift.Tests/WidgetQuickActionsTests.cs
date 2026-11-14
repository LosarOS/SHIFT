using Microsoft.Data.Sqlite;
using Shift.Application;
using Shift.Domain;
using Shift.Infrastructure;
using Xunit;

namespace Shift.Tests;

public sealed class WidgetQuickActionsTests
{
    private static WidgetDefinition Definition(params WidgetQuickAction[] actions) => new()
    { Title = "Quick actions", Kind = WidgetKind.QuickActions, Content = new() { Actions = new(actions) } };

    [Fact] public void KindNumbersStayStableAndLegacyEmptyContentHasValueEquality()
    {
        Assert.Equal(3, (int)WidgetKind.Notes); Assert.Equal(4, (int)WidgetKind.QuickActions);
        Assert.Equal(new WidgetContent(), new WidgetContent());
        var definition = Definition(new WidgetQuickAction { Title = "Документы", Kind = WidgetQuickActionKind.Folder, Target = @"C:\Документы" });
        var imported = WidgetTransfer.Import(WidgetTransfer.Export(definition));
        Assert.NotEqual(definition.Id, imported.Id); Assert.Equal(definition.Content, imported.Content);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("file:///C:/test.txt")]
    [InlineData("https://")]
    public void UnsafeOrMalformedUrlsAreRejected(string target) => Assert.Throws<ValidationException>(() =>
        StudioValidator.Validate(Definition(new WidgetQuickAction { Title = "Link", Kind = WidgetQuickActionKind.Url, Target = target })));

    [Theory]
    [InlineData(@"C:\test.cmd")]
    [InlineData(@"C:\test.ps1")]
    [InlineData(@"C:\test.exe")]
    public void FileActionCannotSmuggleExecutableTargets(string target) => Assert.Throws<ValidationException>(() =>
        StudioValidator.Validate(Definition(new WidgetQuickAction { Title = "File", Kind = WidgetQuickActionKind.File, Target = target })));

    [Fact] public void WorkspaceReferenceCannotCarryTargetAndCannotBecomeALaunchItem()
    {
        var action = new WidgetQuickAction { Title = "Work", WorkspaceId = Guid.NewGuid() };
        WidgetQuickActions.Validate(action);
        Assert.Throws<InvalidOperationException>(() => WidgetQuickActions.ToItem(action));
        Assert.Throws<ValidationException>(() => WidgetQuickActions.Validate(action with { Target = "cmd.exe" }));
        Assert.Throws<ValidationException>(() => WidgetQuickActions.Validate(action with { WorkspaceId = Guid.Empty }));
        Assert.Throws<ValidationException>(() => WidgetQuickActions.Validate(action with { Kind = (WidgetQuickActionKind)999 }));
    }

    [Fact] public void BoundsAndDeclarativeImportRejectCommandsWithoutDispatch()
    {
        var link = new WidgetQuickAction { Title = "Docs", Kind = WidgetQuickActionKind.Url, Target = "https://example.test/документы" };
        StudioValidator.Validate(Definition(link));
        Assert.Throws<ValidationException>(() => StudioValidator.Validate(Definition(Enumerable.Repeat(link, 13).ToArray())));
        var json = WidgetTransfer.Export(Definition(link)).Replace("\"Actions\": [", "\"Command\": \"cmd.exe\", \"Actions\": [");
        Assert.Throws<InvalidDataException>(() => WidgetTransfer.Import(json));
    }

    [Fact] public void ProfilePackExcludesLocalDestinationsAndWorkspaceIds()
    {
        var definition = Definition(new WidgetQuickAction { Title = "Private", WorkspaceId = Guid.NewGuid() });
        var profile = new DesktopProfile { Name = "Profile", Widgets = [new() { DefinitionId = definition.Id }] };
        var pack = StudioProfileTransfer.Export(profile, [definition]);
        Assert.DoesNotContain(definition.Content.Actions[0].WorkspaceId!.Value.ToString(), pack);
        Assert.Empty(StudioProfileTransfer.Import(pack).Definitions[0].Content.Actions);
    }

    [Fact] public async Task V2MigrationKeepsReferencesNotesAndDefinitionsAndCreatesValidatedBackup()
    {
        var folder = Path.Combine(Path.GetTempPath(), "shift-actions-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        try
        {
            var store = new SqliteStore(Path.Combine(folder, "shift.db")); await store.InitializeAsync();
            var old = await store.SaveDefinitionAsync(new() { Title = "Legacy Notes", Kind = WidgetKind.Notes });
            var profile = await store.SaveProfileAsync(new() { Name = "Keep", Widgets = [new() { DefinitionId = old.Id }] });
            await store.SaveWidgetStateAsync(new() { InstanceId = profile.Widgets[0].Id, Notes = "Не терять" });
            await using (var db = new SqliteConnection("Data Source=" + store.DatabasePath))
            {
                await db.OpenAsync(); using var cmd = db.CreateCommand();
                cmd.CommandText = "PRAGMA foreign_keys=OFF; BEGIN; CREATE TABLE LegacyDefinitions(Id TEXT PRIMARY KEY,SchemaVersion INTEGER NOT NULL CHECK(SchemaVersion=1),Title TEXT NOT NULL,Kind INTEGER NOT NULL CHECK(Kind BETWEEN 0 AND 3),Style TEXT NOT NULL CHECK(json_valid(Style)),Content TEXT NOT NULL CHECK(json_valid(Content)),Revision INTEGER NOT NULL CHECK(Revision>0)); INSERT INTO LegacyDefinitions SELECT * FROM StudioDefinitions; DROP TABLE StudioDefinitions; ALTER TABLE LegacyDefinitions RENAME TO StudioDefinitions; PRAGMA user_version=2; COMMIT;";
                await cmd.ExecuteNonQueryAsync();
            }
            await store.InitializeAsync(); Assert.NotNull(store.LastMigrationBackupPath);
            await using (var backupDb = new SqliteConnection("Data Source=" + store.LastMigrationBackupPath))
            { await backupDb.OpenAsync(); using var version = backupDb.CreateCommand(); version.CommandText = "PRAGMA user_version"; Assert.Equal(2L, await version.ExecuteScalarAsync()); }
            Assert.Equal(old, Assert.Single(await store.ListDefinitionsAsync()));
            Assert.Equal("Не терять", (await store.GetWidgetStateAsync(profile.Widgets[0].Id))!.Notes);
            var quick = await store.SaveDefinitionAsync(Definition(new WidgetQuickAction { Title = "Docs", Kind = WidgetQuickActionKind.Url, Target = "https://example.test" }));
            Assert.Equal(WidgetKind.QuickActions, quick.Kind);
            await using var inspected = new SqliteConnection("Data Source=" + store.DatabasePath); await inspected.OpenAsync();
            using var check = inspected.CreateCommand(); check.CommandText = "PRAGMA user_version"; Assert.Equal(3L, await check.ExecuteScalarAsync());
            check.CommandText = "PRAGMA foreign_key_check"; Assert.Null(await check.ExecuteScalarAsync());
        }
        finally { SqliteConnection.ClearAllPools(); Directory.Delete(folder, true); }
    }
}
