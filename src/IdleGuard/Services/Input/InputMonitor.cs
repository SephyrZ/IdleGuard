using IdleGuard.Models;

namespace IdleGuard.Services.Input;

/// <summary>Owned by App; native messages and device rules are handled on the UI thread.</summary>
public sealed class InputMonitor : IDisposable
{
    private RawInputSource? _source;
    private readonly XInputSource _controllers = new();
    private bool _locked, _suspended;
    private string? _error;
    public DeviceCatalog Catalog { get; } = new();
    public IdleTracker Tracker { get; } = new();
    public long AcceptedCount { get; private set; }
    public long IgnoredCount { get; private set; }
    public InputActivity? LastActivity { get; private set; }
    public InputDevice? LastAccepted { get; private set; }
    public string? Error => _error;

    public InputMonitor()
    {
        _controllers.ConnectionChanged += Catalog.SetXInputConnected;
        _controllers.Activity += slot => RecordActivity(Catalog.Find($"xinput:{slot}"), InputActivityKind.ControllerInput);
        RefreshDevices();
        PollControllers();
    }

    public void PollControllers()
    {
        if (_locked || _suspended) return;
        try { _controllers.Poll(); }
        catch (Exception ex) { OnError(ex); }
    }

    public void RefreshDevices()
    {
        try
        {
            if (_source is null)
            {
                _source = new RawInputSource(handle => Catalog.Find(handle) is { CanMonitor: true } device && Catalog.Devices.Contains(device));
                _source.Activity += OnActivity;
                _source.DevicesChanged += RefreshDevices;
                _source.Error += OnError;
                _source.SessionLocked += locked => { _locked = locked; _controllers.Clear(); UpdatePause(); };
                _source.SystemSuspended += suspended => { _suspended = suspended; _controllers.Clear(); UpdatePause(); };
            }
            Catalog.Refresh();
            _source.SetHidDevices(Catalog.Devices.Where(d => d.IsConnected && d.Usage != 0 && d.CanMonitor));
            _error = null;
            UpdatePause();
        }
        catch (Exception ex) { OnError(ex); }
    }

    private void OnActivity(nint handle, InputActivityKind kind)
    {
        var device = Catalog.Find(handle);
        RecordActivity(device, kind);
    }

    private void RecordActivity(InputDevice? device, InputActivityKind kind)
    {
        // Arrival can race enumeration; unknown sources remain rejected until refresh.
        var accepted = Tracker.RecordActivity(device is not null && Catalog.Devices.Contains(device) && DevicePolicy.Accepts(device));
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
