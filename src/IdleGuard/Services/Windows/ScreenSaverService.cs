using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using IdleGuard.Native;

namespace IdleGuard.Services.Windows;

public sealed record ScreenSaverConfiguration(string? Path, int TimeoutSeconds, bool AutomaticEnabled);

public sealed class ScreenSaverService
{
    public ScreenSaverConfiguration ReadConfiguration()
    {
        using var desktop = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop");
        var path = desktop?.GetValue("SCRNSAVE.EXE") as string;
        path = string.IsNullOrWhiteSpace(path) ? null : Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
        return new(path, ReadParameter(NativeMethods.GetScreenSaverTimeout),
            ReadParameter(NativeMethods.GetScreenSaverActive) != 0);
    }

    public bool IsRunning() => ReadParameter(NativeMethods.GetScreenSaverRunning) != 0;

    public void RequestStart(nint window)
    {
        if (ReadConfiguration().Path is null)
            throw new InvalidOperationException("NoSaverError");
        if (!NativeMethods.PostMessage(window, NativeMethods.WmSysCommand, NativeMethods.ScScreenSave, 0))
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    private static int ReadParameter(uint action)
    {
        if (!NativeMethods.SystemParametersInfo(action, 0, out var value, 0))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        return value;
    }
}
