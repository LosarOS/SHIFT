using Shift.Domain;
namespace Shift.Application;

/// <summary>Monotonic elapsed time while alive, persisted UTC deadline for restart and sleep recovery.</summary>
public sealed class FocusTimerController
{
    private readonly TimeProvider time;
    private FocusTimerState state;
    private long started;
    private double initial;
    public FocusTimerController(FocusTimerState state, TimeProvider? timeProvider = null)
    {
        this.time = timeProvider ?? TimeProvider.System;
        StudioValidator.Validate(new WidgetRuntimeState { InstanceId = Guid.NewGuid(), Timer = state });
        this.state = state;
        if (state.Running)
        {
            var elapsed = Math.Max(0, (time.GetUtcNow() - state.SavedAtUtc!.Value).TotalSeconds);
            initial = Math.Max(0, Math.Min(state.RemainingSeconds - elapsed, (state.EndsAtUtc!.Value - time.GetUtcNow()).TotalSeconds));
            started = time.GetTimestamp();
            this.state = state with { RemainingSeconds = initial, EndsAtUtc = time.GetUtcNow().AddSeconds(initial), SavedAtUtc = time.GetUtcNow() };
        }
        else initial = state.RemainingSeconds;
    }
    public FocusTimerState State { get { Refresh(); return state; } }
    private void Refresh()
    {
        if (!state.Running) return;
        var monotonic = initial - Math.Max(0, time.GetElapsedTime(started).TotalSeconds);
        var wall = state.EndsAtUtc is { } end ? (end - time.GetUtcNow()).TotalSeconds : monotonic;
        // Moving the wall clock backwards cannot increase remaining time. Forward changes/sleep can finish early.
        var remaining = Math.Max(0, Math.Min(state.RemainingSeconds, Math.Min(monotonic, wall)));
        state = state with { RemainingSeconds = remaining, SavedAtUtc = time.GetUtcNow() };
    }
    public void Start()
    {
        Refresh(); if (state.Running || state.RemainingSeconds <= 0) return;
        initial = state.RemainingSeconds; started = time.GetTimestamp();
        state = state with { Running = true, EndsAtUtc = time.GetUtcNow().AddSeconds(initial), SavedAtUtc = time.GetUtcNow(), CompletionNotified = false };
    }
    public void Pause() { Refresh(); state = state with { Running = false, EndsAtUtc = null, SavedAtUtc = time.GetUtcNow() }; }
    public void Reset(TimeSpan duration)
    {
        if (duration.TotalSeconds is < 1 or > 86400 || !double.IsFinite(duration.TotalSeconds)) throw new ArgumentOutOfRangeException(nameof(duration));
        state = new() { RemainingSeconds = duration.TotalSeconds, SavedAtUtc = time.GetUtcNow() }; initial = duration.TotalSeconds;
    }
    public bool Tick()
    {
        Refresh();
        if (!state.Running || state.RemainingSeconds > 0 || state.CompletionNotified) return false;
        state = state with { Running = false, EndsAtUtc = null, CompletionNotified = true };
        return true;
    }
}
