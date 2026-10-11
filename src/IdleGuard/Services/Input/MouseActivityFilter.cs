namespace IdleGuard.Services.Input;

/// <summary>Filters empty packets and unchanged absolute coordinates per current device handle.</summary>
public sealed class MouseActivityFilter
{
    private readonly Dictionary<nint, (int X, int Y, bool VirtualDesktop)> _absolute = new();

    public bool IsActivity(nint device, ushort flags, ushort buttonFlags, ushort buttonData, int x, int y)
    {
        bool movement;
        if ((flags & 1) == 0)
        {
            _absolute.Remove(device);
            movement = x != 0 || y != 0;
        }
        else
        {
            var position = (x, y, (flags & 2) != 0);
            movement = _absolute.TryGetValue(device, out var previous) && previous.Item3 == position.Item3
                && (previous.X != x || previous.Y != y);
            _absolute[device] = position;
        }
        var buttonChanged = (buttonFlags & 0x03FF) != 0;
        var wheelChanged = (buttonFlags & 0x0C00) != 0 && buttonData != 0;
        return movement || buttonChanged || wheelChanged;
    }

    public void Clear() => _absolute.Clear();
}
