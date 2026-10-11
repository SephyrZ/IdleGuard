namespace IdleGuard.Services;

public enum IdleControllerState { Disabled, Monitoring, Triggering, Triggered, Error }

/// <summary>One start request per accepted-input cycle; failures require an explicit retry.</summary>
public sealed class IdleController
{
    private long _cycle;
    public IdleControllerState State { get; private set; }
    public string? Error { get; private set; }
    public bool Enabled => State is IdleControllerState.Monitoring or IdleControllerState.Triggering or IdleControllerState.Triggered;

    public void Enable(long cycle) { _cycle = cycle; Error = null; State = IdleControllerState.Monitoring; }
    public void Disable() { State = IdleControllerState.Disabled; Error = null; }
    public void Fail(string error) { State = IdleControllerState.Error; Error = error; }
    public void BeginRequest(long cycle) { if (Enabled) { _cycle = cycle; State = IdleControllerState.Triggering; } }
    public void CompleteRequest(bool observed, string failure)
    {
        if (State != IdleControllerState.Triggering) return;
        if (observed) State = IdleControllerState.Triggered;
        else Fail(failure);
    }

    public bool Evaluate(TimeSpan idle, TimeSpan timeout, long cycle, bool paused, bool saverRunning)
    {
        if (!Enabled || paused || State == IdleControllerState.Triggering) return false;
        if (cycle != _cycle) { _cycle = cycle; State = IdleControllerState.Monitoring; }
        if (State == IdleControllerState.Triggered) return false;
        if (saverRunning) { State = IdleControllerState.Triggered; return false; }
        if (timeout <= TimeSpan.Zero) { Fail("InvalidTimeout"); return false; }
        if (idle < timeout) return false;
        State = IdleControllerState.Triggering;
        return true;
    }
}
