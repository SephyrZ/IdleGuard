using IdleGuard.Native;

namespace IdleGuard.Services.Input;

internal sealed class XInputSource
{
    private readonly ControllerActivityFilter[] _filters = Enumerable.Range(0, 4).Select(_ => new ControllerActivityFilter()).ToArray();
    private readonly bool[] _connected = new bool[4];
    private readonly long[] _nextProbe = new long[4];
    internal event Action<int, bool>? ConnectionChanged;
    internal event Action<int>? Activity;

    internal void Poll()
    {
        for (var index = 0; index < 4; index++)
        {
            if (!_connected[index] && Environment.TickCount64 < _nextProbe[index]) continue;
            var connected = ControllerNativeMethods.GetState((uint)index, out var state) == 0;
            if (!connected) _nextProbe[index] = Environment.TickCount64 + 2000;
            if (_connected[index] != connected)
            {
                _connected[index] = connected;
                _filters[index].Clear();
                ConnectionChanged?.Invoke(index, connected);
            }
            if (!connected) continue;
            var pad = state.Gamepad;
            if (_filters[index].IsActivity(pad.Buttons, pad.LeftTrigger, pad.RightTrigger,
                    pad.LeftX, pad.LeftY, pad.RightX, pad.RightY)) Activity?.Invoke(index);
        }
    }

    internal void Clear() { foreach (var filter in _filters) filter.Clear(); }
}
