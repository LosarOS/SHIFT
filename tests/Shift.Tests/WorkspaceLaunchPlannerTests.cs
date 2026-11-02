using Shift.Application;
using Shift.Domain;
using Shift.App.Services;
using Xunit;

namespace Shift.Tests;

public class WorkspaceLaunchPlannerTests
{
    private static WorkspaceItem App(string args = "") => new() { Name = "Editor", Kind = ItemKind.App, Target = @"C:\Tools\editor.exe", Arguments = args };
    [Fact] public void OrderAndEnabledWaitBudgetMatchCoordinatorSemantics()
    {
        var command = new WorkspaceItem { Name = "Command", Kind = ItemKind.Command, Target = "Write-Output 'test'", Order = 0, DelayMs = 50 };
        var skipped = new WorkspaceItem { Name = "Skipped", Kind = ItemKind.Url, Target = "https://example.org/", Order = 1, DelayMs = 300000, Enabled = false };
        var app = App() with { Order = 2, DelayMs = 100 };
        var plan = WorkspaceLaunchPlanner.Create(new Workspace { Name = "Plan", Items = [app, skipped, command] });
        Assert.Equal(new[] { command.Id, skipped.Id, app.Id }, plan.Steps.Select(s => s.Item.Id));
        Assert.Equal(new long?[] { 50, null, 150 }, plan.Steps.Select(s => s.DelayBudgetMs));
        Assert.Equal(150, plan.TotalDelayMs); Assert.Equal(2, plan.Enabled); Assert.Equal(1, plan.Disabled); Assert.Equal(1, plan.Commands);
    }

    [Fact] public void EqualOrderUsesTheSameGuidTieBreakAsLaunch()
    {
        var first = App() with { Id = Guid.Parse("00000000-0000-4000-8000-000000000001") };
        var second = App() with { Id = Guid.Parse("00000000-0000-4000-8000-000000000002") };
        var plan = WorkspaceLaunchPlanner.Create(new Workspace { Name = "Plan", Items = [second, first] });
        Assert.Equal(first.Id, plan.Steps[0].Item.Id);
        Assert.Equal(first.Id, plan.Steps[1].RepeatsItemId);
    }

    [Fact] public void DifferentArgumentsAndWorkingDirectoriesAreNotFalseDuplicates()
    {
        var first = App("--project One");
        var second = App("--project Two") with { Order = 1 };
        var third = App("--project One") with { Order = 2, WorkingDirectory = @"C:\Other" };
        var fourth = first with { Id = Guid.NewGuid(), Order = 3, Target = "c:/tools/EDITOR.exe" };
        var plan = WorkspaceLaunchPlanner.Create(new Workspace { Name = "Plan", Items = [first, second, third, fourth] });
        Assert.Equal(1, plan.RepeatedTargets); Assert.Equal(first.Id, plan.Steps[3].RepeatsItemId);
    }

    [Fact] public void DisabledTargetsDoNotCreateRepeatedWarningsOrCommandConsent()
    {
        var first = App() with { Enabled = false };
        var second = App() with { Order = 1 };
        var command = new WorkspaceItem { Name = "Disabled command", Kind = ItemKind.Command, Target = "exit", Enabled = false, Order = 2 };
        var plan = WorkspaceLaunchPlanner.Create(new Workspace { Name = "Plan", Items = [first, second, command] });
        Assert.Equal(0, plan.RepeatedTargets); Assert.Equal(0, plan.Commands); Assert.Equal(1, plan.Enabled);
    }

    [Fact] public void CommandsRemainLiteralAndShellSpecificInThePreview()
    {
        var command = new WorkspaceItem { Name = "Script", Kind = ItemKind.Command, Target = "echo One\r\necho Two", DelayMs = 100 };
        var other = command with { Id = Guid.NewGuid(), Shell = CommandShell.Cmd, Order = 1 };
        var repeat = command with { Id = Guid.NewGuid(), Order = 2 };
        var plan = WorkspaceLaunchPlanner.Create(new Workspace { Name = "Plan", Items = [command, other, repeat] });
        Assert.Equal(3, plan.Commands); Assert.Equal(1, plan.RepeatedTargets);
        Assert.Equal(command.Target, plan.Steps[0].Item.Target);
    }

    [Fact] public void TheMaximumConfiguredDelayFitsWithoutOverflow()
    {
        var workspace = new Workspace { Name = "Maximum", Items = Enumerable.Range(0, 100).Select(n => App(n.ToString()) with { Order = n, DelayMs = 300000 }).ToArray() };
        var plan = WorkspaceLaunchPlanner.Create(workspace);
        Assert.Equal(30_000_000L, plan.TotalDelayMs); Assert.Equal(30_000_000L, plan.Steps[^1].DelayBudgetMs);
    }

    [Fact] public void PlansAreReadOnlySnapshotsWithoutEditingTheWorkspace()
    {
        var original = App() with { Order = 9 };
        var array = new[] { original };
        var workspace = new Workspace { Name = "Plan", Items = array };
        var plan = WorkspaceLaunchPlanner.Create(workspace);
        array[0] = original with { Name = "Changed" };
        Assert.Equal("Editor", plan.Steps[0].Item.Name);
        Assert.Equal(9, plan.Steps[0].Item.Order);
        Assert.Throws<NotSupportedException>(() => ((IList<LaunchPlanStep>)plan.Steps)[0] = plan.Steps[0]);
    }

    [Fact] public void EmptyPlansDoNotInventAReadyState()
    {
        var plan = WorkspaceLaunchPlanner.Create(new Workspace { Name = "Empty" });
        Assert.Empty(plan.Steps); Assert.Equal(0, plan.Enabled); Assert.Equal(0, plan.TotalDelayMs);
    }

    [Fact] public void InvalidModelsAreRejectedBeforeAPlanIsPresented() =>
        Assert.Throws<ValidationException>(() => WorkspaceLaunchPlanner.Create(new Workspace { Name = "Invalid", Items = [App() with { DelayMs = -1 }] }));

    [Theory]
    [InlineData("ru")]
    [InlineData("en")]
    public void NewWorkflowMessagesAreLocalizedAndFormatCorrectly(string language)
    {
        var s = new Strings { Language = language };
        foreach (var key in Strings.Keys.Where(k => k.StartsWith("Compose.") || k.StartsWith("Plan.") || k.StartsWith("Validation.Compose.")))
            Assert.NotEqual(key, s[key]);
        Assert.DoesNotContain("{0}", string.Format(s.Culture, s["Plan.Summary"], 1, 2, 3, 4));
        Assert.DoesNotContain("{4}", string.Format(s.Culture, s["Compose.Summary"], 1, 2, 3, 4, 5));
    }

    [Fact] public void UrlCredentialIdentityIsCaseSensitiveAndNotMergedWithPublicRequests()
    {
        var first = new WorkspaceItem { Name = "First", Kind = ItemKind.Url, Target = "https://user:one@example.org/a" };
        var second = first with { Id = Guid.NewGuid(), Order = 1, Target = "https://user:One@example.org/a" };
        var third = first with { Id = Guid.NewGuid(), Order = 2, Target = "https://USER:one@example.org/a" };
        var publicRequest = first with { Id = Guid.NewGuid(), Order = 3, Target = "https://example.org/a" };
        var same = first with { Id = Guid.NewGuid(), Order = 4, Target = "https://user:one@EXAMPLE.org/a" };
        var plan = WorkspaceLaunchPlanner.Create(new Workspace { Name = "Auth", Items = [first, second, third, publicRequest, same] });
        Assert.Equal(1, plan.RepeatedTargets);
        Assert.Equal(first.Id, plan.Steps[4].RepeatsItemId);
        Assert.All(plan.Steps.Take(4), step => Assert.Null(step.RepeatsItemId));
    }
}
