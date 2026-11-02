using Shift.Domain;
namespace Shift.Application;

public sealed record LaunchPlanStep(int Position, WorkspaceItem Item, long? DelayBudgetMs, Guid? RepeatsItemId);
public sealed record LaunchPlan(Guid WorkspaceId, string Name, DateTimeOffset CreatedAt, IReadOnlyList<LaunchPlanStep> Steps,
    int Enabled, int Commands, int RepeatedTargets, long TotalDelayMs)
{
    public int Disabled => Steps.Count - Enabled;
}

/// <summary>Dispatch order and configured wait budget only; does not inspect, launch or predict application readiness.</summary>
public static class WorkspaceLaunchPlanner
{
    public static LaunchPlan Create(Workspace workspace)
    {
        WorkspaceValidator.Validate(workspace);
        var ordered = workspace.Items.OrderBy(i => i.Order).ThenBy(i => i.Id).ToArray();
        var seen = new Dictionary<string, Guid>(StringComparer.Ordinal);
        var steps = new List<LaunchPlanStep>();
        long budget = 0;
        foreach (var item in ordered)
        {
            Guid? repeats = null;
            if (item.Enabled)
            {
                budget = checked(budget + item.DelayMs);
                var key = item.Kind == ItemKind.Command
                    ? "command:" + item.Shell + "\0" + item.Target
                    : item.Kind + ":" + WorkspaceComposer.TargetKey(item.Kind, item.Target);
                key += "\0" + item.Arguments + "\0" + item.WorkingDirectory.Replace('/', '\\').ToUpperInvariant();
                if (seen.TryGetValue(key, out var original)) repeats = original;
                else seen.Add(key, item.Id);
            }
            steps.Add(new(steps.Count + 1, item, item.Enabled ? budget : null, repeats));
        }
        return new(workspace.Id, workspace.Name, DateTimeOffset.UtcNow, steps.AsReadOnly(),
            ordered.Count(i => i.Enabled), ordered.Count(i => i.Enabled && i.Kind == ItemKind.Command),
            steps.Count(s => s.RepeatsItemId is not null), budget);
    }
}
