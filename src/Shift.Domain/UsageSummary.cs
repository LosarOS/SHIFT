namespace Shift.Domain;

public sealed record WorkspaceUsage(Guid WorkspaceId, long LaunchCount, DateTimeOffset? LastLaunchedAt, double AverageDurationMs);
public sealed record UsageSummary(long TotalRuns, long CompletedRuns, long FailedRuns, long CancelledRuns,
    IReadOnlyList<WorkspaceUsage> Workspaces)
{
    public static UsageSummary Empty { get; } = new(0, 0, 0, 0, []);
}
public sealed record HistoryFilter(string Query = "", bool OnlyFailures = false, int Offset = 0, int PageSize = 30);
public sealed record HistoryPage(IReadOnlyList<LaunchRun> Runs, long TotalCount, int Offset, int PageSize)
{
    public bool HasNext => Offset + Runs.Count < TotalCount;
    public bool HasPrevious => Offset > 0;
}
public sealed record RestoreResult(string SafetyBackupPath);
