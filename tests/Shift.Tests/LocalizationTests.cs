using System.Globalization;
using System.Text.RegularExpressions;
using Shift.App.Services;
using Shift.Domain;
using Xunit;
namespace Shift.Tests;
public sealed class LocalizationTests
{
    [Fact] public void EveryResourceHasBothLanguagesAndMatchingFormatArguments()
    {
        var ru = new Strings { Language = "ru" }; var en = new Strings { Language = "en" };
        foreach (var key in Strings.Keys)
        {
            Assert.False(string.IsNullOrWhiteSpace(ru[key]), key); Assert.False(string.IsNullOrWhiteSpace(en[key]), key);
            Assert.Equal(Regex.Matches(en[key], @"\{\d+(?:[^}]*)?\}").Select(m => m.Value).Order(), Regex.Matches(ru[key], @"\{\d+(?:[^}]*)?\}").Select(m => m.Value).Order());
        }
    }
    [Fact] public void DateFormattingFollowsChosenLanguageRatherThanHostCulture()
    {
        var old = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            var date = new DateTimeOffset(2026, 10, 27, 14, 5, 0, TimeSpan.Zero);
            var text = new Strings().FormatDate(date);
            Assert.Equal(date.ToLocalTime().ToString("g", CultureInfo.GetCultureInfo("ru-RU")), text);
            Assert.Equal("—", new Strings().FormatDate(null));
        }
        finally { CultureInfo.CurrentCulture = old; }
    }
    [Theory] [InlineData("Minimal")] [InlineData("Glass")] [InlineData("Neon")] [InlineData("Midnight")] [InlineData("Light")] [InlineData("Forest")]
    public void StyleNamesAreLocalizedWithoutChangingIdentifiers(string id)
    {
        var ru = new Strings(); Assert.NotEqual(id, ru.TemplateName(id)); Assert.Equal("custom", ru.TemplateName("custom"));
        Assert.Equal(id == "Glass" ? "Glass · translucent" : id, new Strings { Language = "en" }.TemplateName(id));
    }
    [Theory] [InlineData(WidgetKind.Clock)] [InlineData(WidgetKind.SystemMonitor)] [InlineData(WidgetKind.FocusTimer)] [InlineData(WidgetKind.Notes)]
    public void BuiltInWidgetKindsHaveRussianDisplayNames(WidgetKind kind)
    { Assert.Matches("[А-Яа-я]", new Strings().WidgetKindName(kind)); }
    [Fact] public void KnownRecoveryConflictKeepsExactMonitorIdentity()
    {
        var text = "Wallpaper changed after SHIFT: "+@"\\?\DISPLAY#Экран#1";
        Assert.Equal("Обои изменены после применения SHIFT: "+@"\\?\DISPLAY#Экран#1", new Strings().LocalizeMessage(text));
        Assert.Equal(text, new Strings { Language = "en" }.LocalizeMessage(text));
    }
    [Fact] public void UserTitlesAndUnknownDiagnosticsAreNotRewritten()
    {
        var text = "My project: Studio is disabled.";
        Assert.Equal(text, new Strings().LocalizeMessage(text));
        var diagnostic = "Unexpected vendor error E42 at C:/Мой проект";
        Assert.Contains(diagnostic, new Strings().ErrorDetails(new IOException(diagnostic)));
    }
    [Fact] public void MissingFileHasRussianGuidanceAndPreservesTechnicalDetail()
    {
        var error = new FileNotFoundException("Missing C:/проект/file.txt");
        Assert.StartsWith("Файл или папка не найдены", new Strings().ErrorDetails(error));
        Assert.Contains(error.Message, new Strings().ErrorDetails(error));
        Assert.Equal(error.Message, new Strings { Language = "en" }.ErrorDetails(error));
    }
}
