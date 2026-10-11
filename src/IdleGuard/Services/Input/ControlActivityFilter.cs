namespace IdleGuard.Services.Input;

/// <summary>Compare meaningful controls, never timestamps, battery levels or report counters.</summary>
public sealed class ControlActivityFilter
{
    private readonly Dictionary<string, double> _values = new();
    private readonly Dictionary<string, HashSet<ushort>> _buttons = new();

    public bool ValueChanged(string key, double value, double threshold = 0)
    {
        if (!double.IsFinite(value)) return false;
        if (!_values.TryGetValue(key, out var previous)) { _values[key] = value; return false; }
        if (value == previous || Math.Abs(value - previous) < threshold) return false;
        _values[key] = value;
        return true;
    }

    public bool ButtonsChanged(string key, IEnumerable<ushort> buttons)
    {
        var current = buttons.ToHashSet();
        if (!_buttons.TryGetValue(key, out var previous)) { _buttons[key] = current; return false; }
        if (previous.SetEquals(current)) return false;
        _buttons[key] = current;
        return true;
    }

    public void Clear() { _values.Clear(); _buttons.Clear(); }
}
