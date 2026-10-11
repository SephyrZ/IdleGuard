using Microsoft.Win32;
using System.Runtime.Versioning;

namespace IdleGuard.Services.Windows;

/// <summary>Current-user logon startup. No administrator privileges required.</summary>
public sealed class StartupService
{
    private const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "IdleGuard";
    public static string CommandFor(string executable) => $"\"{executable}\" --background";
    private static string Command => CommandFor(Environment.ProcessPath ?? throw new InvalidOperationException("Executable path is unavailable."));

    [SupportedOSPlatform("windows")]
    public bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
        return string.Equals(key?.GetValue(ValueName) as string, Command, StringComparison.OrdinalIgnoreCase);
    }

    [SupportedOSPlatform("windows")]
    public void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(KeyPath, writable: true);
        if (enabled) key.SetValue(ValueName, Command, RegistryValueKind.String);
        else key.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
