using Shift.Domain;
namespace Shift.Application;

public enum DoctorState { Ready, Warning, Error, Unknown }
public sealed record DoctorFinding(DoctorState State, string Code);
public sealed record DoctorItemResult(Guid ItemId, string Name, DoctorState State, IReadOnlyList<DoctorFinding> Findings);
public sealed record DoctorReport(Guid WorkspaceId, DateTimeOffset CheckedAt, IReadOnlyList<DoctorItemResult> Items)
{
    public int Ready => Items.Count(i => i.State == DoctorState.Ready);
    public int Warnings => Items.Count(i => i.State == DoctorState.Warning);
    public int Errors => Items.Count(i => i.State == DoctorState.Error);
    public int Unknown => Items.Count(i => i.State == DoctorState.Unknown);
}
public interface IWorkspaceDoctor
{
    Task<DoctorReport> CheckAsync(Workspace workspace, CancellationToken token = default);
}
public interface IItemInspection
{
    Task<IReadOnlyList<DoctorFinding>> InspectAsync(WorkspaceItem item, CancellationToken token);
}

/// <summary>Read-only observations, never a launch guarantee or a command safety verdict.</summary>
public sealed class WorkspaceDoctor(IItemInspection inspection) : IWorkspaceDoctor
{
    public async Task<DoctorReport> CheckAsync(Workspace workspace, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        token.ThrowIfCancellationRequested();
        if (workspace.Items is null || workspace.Items.Count > 100 || workspace.Items.Any(i => i is null))
            throw new ValidationException([new ValidationIssue("Items", "A workspace supports up to 100 launch items.")]);
        var results = new List<DoctorItemResult>();
        foreach (var item in workspace.Items.OrderBy(i => i.Order).ThenBy(i => i.Id))
        {
            token.ThrowIfCancellationRequested();
            var findings = new List<DoctorFinding>();
            try { WorkspaceValidator.Validate(new Workspace { Name = "Inspection", Items = [item] }); }
            catch (ValidationException ex) { findings.AddRange(ex.Issues.Select(i => new DoctorFinding(DoctorState.Error, "Validation." + i.Code))); }
            if (!item.Enabled)
                findings.Add(new(DoctorState.Unknown, "Doctor.Disabled"));
            else if (findings.Count == 0)
            {
                try { findings.AddRange(await inspection.InspectAsync(item, token).ConfigureAwait(false)); }
                catch (UnauthorizedAccessException) { findings.Add(new(DoctorState.Error, "Doctor.AccessDenied")); }
                catch (System.Security.SecurityException) { findings.Add(new(DoctorState.Error, "Doctor.AccessDenied")); }
                catch (IOException) { findings.Add(new(DoctorState.Unknown, "Doctor.Unavailable")); }
                if (item.Kind == ItemKind.Command)
                    findings.Add(new(DoctorState.Warning, "Doctor.CommandConsent"));
                if (item.Kind == ItemKind.App && item.Arguments.Length > 0)
                    findings.Add(new(DoctorState.Warning, "Doctor.Arguments"));
            }
            token.ThrowIfCancellationRequested();
            if (findings.Count == 0)
                findings.Add(new(DoctorState.Ready, item.Kind == ItemKind.Url ? "Doctor.UrlFormat" : "Doctor.PathReady"));
            var state = findings.Any(f => f.State == DoctorState.Error) ? DoctorState.Error
                : findings.Any(f => f.State == DoctorState.Unknown) ? DoctorState.Unknown
                : findings.Any(f => f.State == DoctorState.Warning) ? DoctorState.Warning : DoctorState.Ready;
            results.Add(new(item.Id, item.Name, state, findings));
        }
        return new(workspace.Id, DateTimeOffset.UtcNow, results);
    }
}
