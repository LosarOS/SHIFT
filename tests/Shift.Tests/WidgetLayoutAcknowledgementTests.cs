using Shift.App.Studio;
using Shift.Domain;
using Xunit;

namespace Shift.Tests;

public class WidgetLayoutAcknowledgementTests
{
    [Fact]
    public void ModeChangeCannotRebaseAnUnmovedWindowOntoUnseenEditorGeometry()
    {
        // The native window remained clean at the acknowledged position. An editor
        // changed stored geometry; persisting Desktop mode must not make a later
        // native drag eligible to overwrite that unseen editor layout.
        var acknowledged = new WidgetInstance { DefinitionId = Guid.NewGuid(), Layout = new() { X = .2, Width = 300 } };
        var editorThenMode = acknowledged with
        {
            Layout = acknowledged.Layout with { X = .8, Width = 700 },
            Interaction = WidgetInteraction.Desktop
        };
        Assert.False(WidgetPresentation.CanAcknowledgeModeChange(acknowledged, editorThenMode));
    }

    [Fact]
    public void ModeOnlyChangeMayAdvanceTheAcknowledgedMode()
    {
        var acknowledged = new WidgetInstance { DefinitionId = Guid.NewGuid() };
        var saved = acknowledged with { Interaction = WidgetInteraction.Desktop };
        Assert.True(WidgetPresentation.CanAcknowledgeModeChange(acknowledged, saved));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void ModeCannotAcknowledgeReplacementHiddenOrDifferentInstance(int mutation)
    {
        var acknowledged = new WidgetInstance { DefinitionId = Guid.NewGuid() };
        var saved = mutation switch
        {
            0 => acknowledged with { DefinitionId = Guid.NewGuid() },
            1 => acknowledged with { Visible = false },
            _ => acknowledged with { Id = Guid.NewGuid() }
        };
        Assert.False(WidgetPresentation.CanAcknowledgeModeChange(acknowledged, saved));
    }

    [Fact]
    public void CleanFallbackPlacementKeepsItsStoredMonitorAndSizeDuringModeChange()
    {
        var acknowledged = new WidgetInstance { DefinitionId = Guid.NewGuid(),
            Layout = new() { MonitorId = "temporarily-unavailable", Width = 1200, Height = 900, X = .8 } };
        // A temporary visible clamp is not the persisted baseline. No comparison
        // with live fallback pixels is made for a clean window.
        var saved = acknowledged with { Interaction = WidgetInteraction.ClickThrough };
        Assert.True(WidgetPresentation.CanAcknowledgeModeChange(acknowledged, saved));
        Assert.Equal("temporarily-unavailable", saved.Layout.MonitorId);
        Assert.Equal(1200, saved.Layout.Width);
    }

    [Fact]
    public void ALayoutActuallyAcknowledgedBeforeModeChangeIsEligible()
    {
        var original = new WidgetInstance { DefinitionId = Guid.NewGuid() };
        var storedAcknowledgement = original with { Layout = original.Layout with { X = .4 } };
        var savedMode = storedAcknowledgement with { Interaction = WidgetInteraction.Desktop };
        Assert.True(WidgetPresentation.CanAcknowledgeModeChange(storedAcknowledgement, savedMode));
        Assert.False(WidgetPresentation.CanAcknowledgeModeChange(original, savedMode));
    }
}
