using Microsoft.Data.Sqlite;
using Shift.Application;
using Shift.Domain;
using Shift.Infrastructure;
using Xunit;

namespace Shift.Tests;

public sealed class StudioStyleCompatibilityTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "shift-style-contract-" + Guid.NewGuid().ToString("N"));
    public static IEnumerable<object[]> OfferedFonts => StudioValidator.SafeFonts.Select(font => new object[] { font });

    [Theory]
    [MemberData(nameof(OfferedFonts))]
    public async Task Every_offered_font_survives_editor_storage_and_profile_sharing(string font)
    {
        var store = new SqliteStore(Path.Combine(root, "shift.db")); await store.InitializeAsync();
        var definition = new WidgetDefinition { Title = "Мой стиль", Style = new() { FontFamily = font, BorderWidth = 8, Shadow = false } };
        StudioValidator.Validate(definition);
        var saved = await store.SaveWidgetEditAsync(definition, null);
        var profile = await store.SaveProfileAsync(new() { Name = "Шрифты", Widgets = [new() { DefinitionId = saved.Id }] });
        var imported = StudioProfileTransfer.Import(StudioProfileTransfer.Export(profile, await store.ListDefinitionsAsync()));
        var transferred = await store.SavePresetAsync(imported);
        var definitions = await store.ListDefinitionsAsync();
        Assert.Equal(definition.Style, definitions.Single(d => d.Id == saved.Id).Style);
        Assert.Equal(definition.Style, definitions.Single(d => d.Id == transferred.Widgets.Single().DefinitionId).Style);
        Assert.Equal(2, definitions.Count);
        Assert.False((await store.LoadStudioSettingsAsync()).Enabled);
    }

    [Theory]
    [InlineData(65)]
    [InlineData(100)]
    public async Task Custom_style_names_accepted_by_domain_are_not_rejected_by_storage(int length)
    {
        var store = new SqliteStore(Path.Combine(root, "shift.db")); await store.InitializeAsync();
        var definition = new WidgetDefinition { Title = "Пользовательский стиль", Style = new() { TemplateId = new string('Я', length) } };
        StudioValidator.Validate(definition);
        await store.SaveWidgetEditAsync(definition, null);
        var profile = new DesktopProfile { Name = "Стиль", Widgets = [new() { DefinitionId = definition.Id }] };
        var draft = StudioProfileTransfer.Import(StudioProfileTransfer.Export(profile, await store.ListDefinitionsAsync()));
        await store.SavePresetAsync(draft);
        Assert.All(await store.ListDefinitionsAsync(), d => Assert.Equal(definition.Style.TemplateId, d.Style.TemplateId));
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
}
