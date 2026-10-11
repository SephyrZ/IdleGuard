namespace IdleGuard.Models;

public enum InputActivityKind { MouseMove, MouseButton, MouseWheel, KeyDown, KeyUp }
public sealed record InputActivity(InputDevice? Device, InputActivityKind Kind, bool Accepted);
