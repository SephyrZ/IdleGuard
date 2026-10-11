using IdleGuard.Models;

namespace IdleGuard.Services.Input;

public static class DevicePolicy
{
    // Unknown devices never reset the timer. Only keyboard/mouse devices are catalogued here.
    public static bool Accepts(InputDevice? device) => device is { IsConnected: true, IsEnabled: true };
}
