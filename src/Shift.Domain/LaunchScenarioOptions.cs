namespace Shift.Domain;

public enum PreflightPolicy { Off, Continue, StopOnErrors, SkipErrors }
public enum LaunchErrorPolicy { Continue, Stop }

/// <summary>Opt-in local launch policies. They never grant permission to execute commands.</summary>
public sealed record LaunchScenarioOptions
{
    public PreflightPolicy Preflight { get; init; } = PreflightPolicy.Off;
    public LaunchErrorPolicy OnLaunchError { get; init; } = LaunchErrorPolicy.Continue;

    public void Validate()
    {
        if (!Enum.IsDefined(Preflight) || !Enum.IsDefined(OnLaunchError))
            throw new ValidationException([new ValidationIssue("Scenario.Invalid", "Launch scenario policy is invalid.")]);
    }

    public static bool ValidSettings(AppSettings settings) => settings.WorkspaceScenarios is not null
        && settings.WorkspaceScenarios.Count <= 1000
        && settings.WorkspaceScenarios.All(x => x.Key != Guid.Empty && x.Value is not null
            && Enum.IsDefined(x.Value.Preflight) && Enum.IsDefined(x.Value.OnLaunchError));
}
