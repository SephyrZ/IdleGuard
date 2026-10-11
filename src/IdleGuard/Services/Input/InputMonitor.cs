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
    public string LastActivity { get; private set; } = "尚未收到有效键鼠输入。";
    public string LastAccepted { get; private set; } = "尚无";
    public string Status => _error is not null ? $"监控已暂停：{_error}" :
        _locked || _suspended ? "监控已暂停（锁屏或系统挂起）" : "监控中 · 仅键盘和鼠标";

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

    private void OnActivity(nint handle, string kind)
    {
        var device = Catalog.Find(handle);
        // Arrival can race enumeration; unknown sources remain rejected until refresh.
        var accepted = Tracker.RecordActivity(DevicePolicy.Accepts(device));
        var source = device?.Name ?? "无法识别的设备";
        LastActivity = $"{source} · {kind} · {(accepted ? "已接受" : "已忽略")}";
        if (accepted)
        {
            AcceptedCount++;
            LastAccepted = source;
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
