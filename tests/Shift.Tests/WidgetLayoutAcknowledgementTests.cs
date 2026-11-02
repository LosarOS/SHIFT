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
    [Fact]
    public void GatherChangesOnlyPositionAndMonitorOfTheLatestStoredInstance()
    {
        var saved = new WidgetInstance { DefinitionId = Guid.NewGuid(), Interaction = WidgetInteraction.Desktop,
            Layout = new() { MonitorId = "old", X = .8, Y = .7, Width = 600, Height = 450, AlwaysOnTop = true } };
        var gathered = WidgetPresentation.GatherPlacement(saved, "primary", 0);
        Assert.Equal(saved.Id, gathered.Id); Assert.Equal(saved.DefinitionId, gathered.DefinitionId);
        Assert.Equal(saved.Visible, gathered.Visible); Assert.Equal(WidgetInteraction.Desktop, gathered.Interaction);
        Assert.Equal(600, gathered.Layout.Width); Assert.Equal(450, gathered.Layout.Height);
        Assert.True(gathered.Layout.AlwaysOnTop); Assert.Equal("primary", gathered.Layout.MonitorId);
        Assert.Equal(.04, gathered.Layout.X); Assert.Equal(.04, gathered.Layout.Y);
        Assert.Equal("old", saved.Layout.MonitorId); Assert.Equal(.8, saved.Layout.X);
    }

    [Fact]
    public void ProgrammaticGatherAcknowledgementPreservesPreferredSizeAcrossNativeClamping()
    {
        var saved = new WidgetInstance { Interaction = WidgetInteraction.Desktop,
            Layout = new() { Width = 1200, Height = 900, MonitorId = "primary", X = .04, Y = .04, AlwaysOnTop = true } };
        var presented = saved.Layout with { Width = 600, Height = 500, X = .0399, Y = .0401 };
        Assert.True(WidgetPresentation.CanAcknowledgeAppliedLayout(saved, presented, saved.Interaction, presented));
        Assert.False(WidgetPresentation.LayoutWasSaved(saved, presented, saved.Interaction));
        Assert.Equal(1200, saved.Layout.Width); Assert.True(saved.Layout.AlwaysOnTop);
    }

    [Fact]
    public void GatherCannotAcknowledgeANewerMoveDuringItsDatabaseAwait()
    {
        var saved = new WidgetInstance { Layout = new() { X = .04 } };
        var presented = saved.Layout;
        var movedDuringAwait = presented with { X = .3 };
        Assert.False(WidgetPresentation.CanAcknowledgeAppliedLayout(saved, movedDuringAwait, saved.Interaction, presented));
        Assert.False(WidgetPresentation.CanAcknowledgeAppliedLayout(saved, presented, WidgetInteraction.ClickThrough, presented));
    }

}
