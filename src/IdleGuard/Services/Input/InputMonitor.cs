using IdleGuard.Models;

namespace IdleGuard.Services.Input;

/// <summary>Owned by App; native messages and device rules are handled on the UI thread.</summary>
public sealed class InputMonitor : IDisposable
{
    private RawInputSource? _source;
    private bool _locked, _suspended;
    private string? _error;
    public DeviceCatalog Catalog { get; } = new();
    public IdleTracker Tracker { get; } = new();
    public long AcceptedCount { get; private set; }
    public long IgnoredCount { get; private set; }
    public InputActivity? LastActivity { get; private set; }
    public InputDevice? LastAccepted { get; private set; }
    public string? Error => _error;

    public InputMonitor() => RefreshDevices();

    public void RefreshDevices()
    {
        try
        {
            if (_source is null)
            {
                _source = new RawInputSource();
                _source.Activity += OnActivity;
                _source.DevicesChanged += RefreshDevices;
                _source.Error += OnError;
                _source.SessionLocked += locked => { _locked = locked; UpdatePause(); };
                _source.SystemSuspended += suspended => { _suspended = suspended; UpdatePause(); };
            }
            Catalog.Refresh();
            _error = null;
            UpdatePause();
        }
        catch (Exception ex) { OnError(ex); }
    }

    private void OnActivity(nint handle, InputActivityKind kind)
    {
        var device = Catalog.Find(handle);
        // Arrival can race enumeration; unknown sources remain rejected until refresh.
        var accepted = Tracker.RecordActivity(DevicePolicy.Accepts(device));
        LastActivity = new(device, kind, accepted);
        if (accepted)
        {
            AcceptedCount++;
            LastAccepted = device;
        }
        else IgnoredCount++;
    }

    private void OnError(Exception ex) { _error = ex.Message; UpdatePause(); }
    private void UpdatePause()
    {
        if (_locked || _suspended || _error is not null) Tracker.Pause();
        else Tracker.Resume();
    }

    public void Dispose() { _source?.Dispose(); _source = null; Tracker.Pause(); }
}
