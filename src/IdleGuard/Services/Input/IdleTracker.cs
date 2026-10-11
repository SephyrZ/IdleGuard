using System.Diagnostics;

namespace IdleGuard.Services.Input;

/// <summary>Application-owned monotonic idle time. Does not read Windows global idle.</summary>
public sealed class IdleTracker
{
    private readonly Func<TimeSpan> _now;
    private TimeSpan _baseline;
    private TimeSpan _pausedIdle;

    public IdleTracker(Func<TimeSpan>? now = null)
    {
        _now = now ?? (() => Stopwatch.GetElapsedTime(0));
        _baseline = _now();
    }

    public bool IsPaused { get; private set; }
    public long Cycle { get; private set; }
    public TimeSpan Idle => IsPaused ? _pausedIdle : _now() - _baseline;

    public bool RecordActivity(bool accepted)
    {
        if (!accepted || IsPaused) return false;
        _baseline = _now();
        Cycle++;
        return true;
    }

    public void Pause()
    {
        if (IsPaused) return;
        _pausedIdle = Idle;
        IsPaused = true;
    }

    public void Resume()
    {
        if (!IsPaused) return;
        _baseline = _now();
        Cycle++;
        IsPaused = false;
    }

    public void Restart()
    {
        _baseline = _now();
        _pausedIdle = TimeSpan.Zero;
        Cycle++;
    }
}
