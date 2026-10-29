using System.Text;
using Shift.Application;
using Shift.Domain;
using Xunit;

namespace Shift.Tests;

public sealed class StudioProfileEncodingTests
{
    [Fact]
    public void Windows_utf8_bom_file_can_be_imported_without_altering_unicode()
    {
        var definition = new WidgetDefinition { Title = "Часы 日本語" };
        var profile = new DesktopProfile { Name = "Среда", Widgets = [new() { DefinitionId = definition.Id }] };
        var json = StudioProfileTransfer.Export(profile, [definition]);
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(json)).ToArray();
        var draft = StudioProfileTransfer.Import(bytes);
        Assert.Equal("Среда", draft.Profile.Name);
        Assert.Equal("Часы 日本語", Assert.Single(draft.Definitions).Title);
        Assert.Equal("Среда", StudioProfileTransfer.Import("\uFEFF" + json).Profile.Name);
    }

    [Fact]
    public void Bom_only_and_utf16_are_rejected_as_invalid_profile_data()
    {
        Assert.Throws<InvalidDataException>(() => StudioProfileTransfer.Import(new byte[] { 0xEF, 0xBB, 0xBF }));
        Assert.Throws<InvalidDataException>(() => StudioProfileTransfer.Import(Encoding.Unicode.GetBytes("{}")));
    }
}
