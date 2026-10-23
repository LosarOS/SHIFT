using Shift.Application;
using Shift.Domain;
using Xunit;
namespace Shift.Tests;

public class StudioValidationTests
{
    [Fact] public void SafeWidgetRoundTripUsesFreshIdentityAndNoExecutableFields()
    {
        var original = new WidgetDefinition { Title = "Мои часы", Style = StudioTemplates.For("Neon"), Revision = 4 };
        var imported = WidgetTransfer.Import(WidgetTransfer.Export(original));
        Assert.Equal(original.Title, imported.Title); Assert.Equal(original.Style, imported.Style);
        Assert.NotEqual(original.Id, imported.Id); Assert.Equal(0, imported.Revision);
    }
    [Theory]
    [InlineData("{\"SchemaVersion\":2}")]
    [InlineData("{\"Kind\":99}")]
    [InlineData("{\"Kind\":\"Command\"}")]
    [InlineData("{\"ExecutablePath\":\"cmd.exe\"}")]
    [InlineData("{\"Style\":{\"Opacity\":0}}")]
    [InlineData("{\"Content\":{\"FocusMinutes\":0}}")]
    [InlineData("{\"Style\":{\"FontFamily\":\"https://fonts.test/a\"}}")]
    public void UntrustedDeclarativeInputIsRejected(string json) => Assert.ThrowsAny<Exception>(() => WidgetTransfer.Import(json));
    [Fact] public void OversizedAndDeepInputsAreRejected()
    {
        Assert.Throws<InvalidDataException>(() => WidgetTransfer.Import(new string(' ', WidgetTransfer.MaxBytes + 1)));
        Assert.Throws<InvalidDataException>(() => WidgetTransfer.Import(new string('[', 20) + new string(']', 20)));
    }
    [Theory]
    [InlineData("https://server/a.png")]
    [InlineData("\\\\server\\a.png")]
    [InlineData("C:\\wall.png:stream")]
    [InlineData("C:\\x\\..\\wall.png")]
    [InlineData("relative.png")]
    public void WallpaperRequiresAbsoluteLocalPath(string path) => Assert.Throws<ValidationException>(() => StudioValidator.Validate(new DesktopProfile { Wallpapers = [new("display", path)] }));
    [Fact] public void ProfileDuplicateSeparatesInstancesAndClearsAutomaticBehavior()
    {
        var original = new DesktopProfile { WorkspaceId = Guid.NewGuid(), ApplyOnWorkspaceLaunch = true, Widgets = [new() { DefinitionId = Guid.NewGuid() }] };
        var copy = original.Duplicate("Copy");
        Assert.NotEqual(original.Id, copy.Id); Assert.NotEqual(original.Widgets[0].Id, copy.Widgets[0].Id);
        Assert.Equal(original.Widgets[0].DefinitionId, copy.Widgets[0].DefinitionId); Assert.False(copy.ApplyOnWorkspaceLaunch); Assert.Null(copy.WorkspaceId);
    }
    [Fact] public void AutomaticWorkspaceWallpaperChangesRequireManualConfirmationInstead()
    {
        Assert.Throws<ValidationException>(() => StudioValidator.Validate(new DesktopProfile { ApplyOnWorkspaceLaunch = true, Wallpapers = [new("A", @"C:\a.png")] }));
        Assert.Throws<ValidationException>(() => StudioValidator.Validate(new DesktopProfile { ApplyOnWorkspaceLaunch = true, ChangeWallpaperPosition = true }));
        StudioValidator.Validate(new DesktopProfile { ApplyOnWorkspaceLaunch = true });
    }
    [Fact] public void TemplatesRemainImmutableAndValid()
    {
        Assert.Equal(6, StudioTemplates.All.Count);
        foreach (var style in StudioTemplates.All) StudioValidator.Validate(new WidgetDefinition { Style = style });
        var edited = StudioTemplates.For("Neon") with { Accent = "#000000" };
        Assert.NotEqual(edited.Accent, StudioTemplates.For("Neon").Accent);
    }
    [Fact] public void NonFiniteAndOversizedGeometryCannotEnterRuntime()
    {
        Assert.Throws<ValidationException>(() => StudioValidator.Validate(new WidgetLayout { X = double.NaN }));
        Assert.Throws<ValidationException>(() => StudioValidator.Validate(new WidgetLayout { Width = 20000 }));
    }
    [Fact] public void MissingMonitorFallsBackToPrimaryWithNormalizedDpiPlacement()
    {
        var primary = new MonitorIdentity("new", "Primary", -1920, 0, 1920, 1080, -1920, 0, 1920, 1040, 1.5, true);
        var bounds = WidgetLayoutService.Resolve(new() { MonitorId = "missing", X = 1, Y = 1, Width = 400, Height = 200 }, [primary]);
        Assert.Equal("new", bounds.MonitorId); Assert.Equal(-600, bounds.X); Assert.Equal(740, bounds.Y); Assert.Equal(600, bounds.Width);
        var restored = WidgetLayoutService.Capture(bounds, [primary]); Assert.Equal(1, restored.X); Assert.Equal(1, restored.Y); Assert.Equal(400, restored.Width);
    }
    private sealed class FakeTime : TimeProvider
    {
        public DateTimeOffset Utc = DateTimeOffset.Parse("2026-10-23T10:00:00Z"); public long Timestamp;
        public override DateTimeOffset GetUtcNow() => Utc;
        public override long GetTimestamp() => Timestamp;
        public override long TimestampFrequency => 1000;
        public void Advance(double monotonic, double utc) { Timestamp += (long)(monotonic * 1000); Utc = Utc.AddSeconds(utc); }
    }
    [Fact] public void TimerPauseAndResetDoNotDependOnUiTickCount()
    {
        var time = new FakeTime(); var timer = new FocusTimerController(new() { RemainingSeconds = 60 }, time);
        timer.Start(); time.Advance(15, 15); timer.Pause(); time.Advance(100, 100);
        Assert.Equal(45, timer.State.RemainingSeconds); timer.Start(); time.Advance(46, 46);
        Assert.True(timer.Tick()); Assert.False(timer.Tick()); timer.Reset(TimeSpan.FromMinutes(5)); Assert.Equal(300, timer.State.RemainingSeconds);
    }
    [Fact] public void TimerIncludesSleepAndSurvivesRestartAndClockRollback()
    {
        var time = new FakeTime(); var timer = new FocusTimerController(new() { RemainingSeconds = 300 }, time);
        timer.Start(); time.Advance(10, 10); var saved = timer.State;
        time.Advance(0, 200); var restored = new FocusTimerController(saved, time);
        Assert.Equal(90, restored.State.RemainingSeconds); time.Advance(30, -3600); Assert.Equal(60, restored.State.RemainingSeconds);
        time.Advance(0, 4000); Assert.True(restored.Tick()); Assert.Equal(0, restored.State.RemainingSeconds);
    }
}
