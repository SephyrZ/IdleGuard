namespace IdleGuard.Services;

/// <summary>Runs blocking system writes off the UI thread, in the order accepted.</summary>
public sealed class BackgroundSettingsWriter
{
    private readonly object _gate = new();
    private Task _tail = Task.CompletedTask;

    public Task Enqueue(Action write)
    {
        lock (_gate)
        {
            var previous = _tail;
            return _tail = Task.Run(async () =>
            {
                try { await previous.ConfigureAwait(false); }
                catch { /* A failed write must not prevent the next edit from saving. */ }
                write();
            });
        }
    }

    public Task DrainAsync() { lock (_gate) return _tail; }
}
