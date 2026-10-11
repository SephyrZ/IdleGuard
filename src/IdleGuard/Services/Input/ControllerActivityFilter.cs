namespace IdleGuard.Services.Input;

public sealed class ControllerActivityFilter
{
    private readonly ControlActivityFilter _controls = new();

    public bool IsActivity(ushort buttons, byte leftTrigger, byte rightTrigger,
        short leftX, short leftY, short rightX, short rightY)
    {
        // Evaluate every control even when one has changed, so each baseline stays current.
        var changed = _controls.ValueChanged("buttons", buttons & 0xF3FF);
        changed |= _controls.ValueChanged("lt", Trigger(leftTrigger), 0.05);
        changed |= _controls.ValueChanged("rt", Trigger(rightTrigger), 0.05);
        changed |= _controls.ValueChanged("lx", Stick(leftX, 7849), 0.05);
        changed |= _controls.ValueChanged("ly", Stick(leftY, 7849), 0.05);
        changed |= _controls.ValueChanged("rx", Stick(rightX, 8689), 0.05);
        changed |= _controls.ValueChanged("ry", Stick(rightY, 8689), 0.05);
        return changed;
    }

    private static double Trigger(byte value) => value <= 30 ? 0 : (value - 30) / 225.0;
    private static double Stick(short value, int deadZone) => Math.Abs((int)value) <= deadZone
        ? 0 : Math.Sign(value) * (Math.Abs((int)value) - deadZone) / (32768.0 - deadZone);
    public void Clear() => _controls.Clear();
}
