using IdleGuard.Models;

namespace IdleGuard.Services.Input;

public static class DevicePolicy
{
    // Catalog membership is checked by InputMonitor; unsupported controls never reset idle.
    public static bool Accepts(InputDevice? device) => device is { IsConnected: true, IsEnabled: true, CanMonitor: true };
}
