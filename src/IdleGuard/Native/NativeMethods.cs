using System.Runtime.InteropServices;

namespace IdleGuard.Native;

internal static class NativeMethods
{
    internal const uint GetScreenSaverActive = 0x0010;
    internal const uint GetScreenSaverTimeout = 0x000E;
    internal const uint GetScreenSaverRunning = 0x0072;
    internal const uint WmSysCommand = 0x0112;
    internal const nuint ScScreenSave = 0xF140;

    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SystemParametersInfo(uint action, uint parameter, out int value, uint flags);

    [DllImport("user32.dll", EntryPoint = "PostMessageW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool PostMessage(nint window, uint message, nuint wParam, nint lParam);
}
