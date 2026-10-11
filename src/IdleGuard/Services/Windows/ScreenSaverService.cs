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
            throw new InvalidOperationException("尚未配置屏保，请先在 Windows 屏幕保护程序设置中选择屏保。");
        if (!NativeMethods.PostMessage(window, NativeMethods.WmSysCommand, NativeMethods.ScScreenSave, 0))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "发送屏保启动请求失败。");
    }

    private static int ReadParameter(uint action)
    {
        if (!NativeMethods.SystemParametersInfo(action, 0, out var value, 0))
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"读取屏保信息失败（0x{action:X4}）。");
        return value;
    }
}
