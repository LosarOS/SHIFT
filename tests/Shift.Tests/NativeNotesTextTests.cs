using Shift.App.Studio;
using Shift.Domain;
using Xunit;

namespace Shift.Tests;

public class NativeNotesTextTests
{
    [Theory]
    [InlineData("first\nsecond", "first\r\nsecond", "first\nsecond")]
    [InlineData("first\rsecond", "first\r\nsecond", "first\nsecond")]
    [InlineData("first\r\nsecond", "first\r\nsecond", "first\nsecond")]
    [InlineData("", "", "")]
    [InlineData("Заметка — 🙂", "Заметка — 🙂", "Заметка — 🙂")]
    public void EachAcceptedNewlineRendersAsNativeCrLfAndPersistsAsLf(string source, string rendered, string persisted)
    {
        Assert.Equal(rendered, WidgetPresentation.ToNativeNotesText(source));
        Assert.Equal(persisted, WidgetPresentation.FromNativeNotesText(rendered));
    }

    [Fact]
    public void MixedNewlinesTrailingBlankLinesAndWhitespaceArePreserved()
    {
        const string original = "  Я\r\n\tsecond\rthird\n\n";
        var rendered = WidgetPresentation.ToNativeNotesText(original);
        Assert.Equal("  Я\r\n\tsecond\r\nthird\r\n\r\n", rendered);
        Assert.Equal("  Я\n\tsecond\nthird\n\n", WidgetPresentation.FromNativeNotesText(rendered));
        Assert.Equal("  Я\r\n\tsecond\rthird\n\n", original);
    }

    [Fact]
    public void NativeExpansionCannotPushAValidBoundaryNoteBeyondPersistenceLimit()
    {
        var source = string.Concat(Enumerable.Repeat("Я\r", 10000));
        Assert.Equal(20000, source.Length);
        var rendered = WidgetPresentation.ToNativeNotesText(source);
        Assert.Equal(30000, rendered.Length);
        var stored = WidgetPresentation.FromNativeNotesText(rendered);
        Assert.Equal(20000, stored.Length);
        Assert.Equal(source.Replace('\r', '\n'), stored);
        StudioValidator.Validate(new WidgetRuntimeState { InstanceId = Guid.NewGuid(), Notes = stored });
    }

    [Fact]
    public void RepeatedRenderAndRestoreDoNotAddBlankLines()
    {
        const string source = "one\r\ntwo\r\n";
        var rendered = WidgetPresentation.ToNativeNotesText(source);
        Assert.Equal(rendered, WidgetPresentation.ToNativeNotesText(rendered));
        var stored = WidgetPresentation.FromNativeNotesText(rendered);
        Assert.Equal(stored, WidgetPresentation.FromNativeNotesText(stored));
        Assert.Equal(rendered, WidgetPresentation.ToNativeNotesText(stored));
    }

    [Fact]
    public void UnrelatedUnicodeSeparatorsAndWhitespaceAreNotRewritten()
    {
        const string source = "a\u0085b\u2028c\u2029d\v\f\t  ";
        Assert.Equal(source, WidgetPresentation.ToNativeNotesText(source));
        Assert.Equal(source, WidgetPresentation.FromNativeNotesText(source));
    }
}
