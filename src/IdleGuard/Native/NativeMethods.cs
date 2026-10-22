using System.Runtime.InteropServices;

namespace IdleGuard.Native;

internal static class NativeMethods
{
    internal const uint DwmUseImmersiveDarkMode = 20;
    internal const uint DwmBorderColor = 34;
    internal const uint DwmColorNone = 0xFFFFFFFE;
    internal const uint DwmColorDefault = 0xFFFFFFFF;

    [DllImport("dwmapi.dll")]
    internal static extern int DwmSetWindowAttribute(nint window, uint attribute, in uint value, uint size);

    [DllImport("user32.dll")]
    internal static extern uint GetDpiForWindow(nint window);
    internal const uint GetScreenSaverActive = 0x0010;
    internal const uint GetScreenSaverTimeout = 0x000E;
    internal const uint GetScreenSaverRunning = 0x0072;
    internal const uint SetScreenSaverTimeout = 0x000F;
    internal const uint SetScreenSaverActive = 0x0011;
    internal const uint WmSysCommand = 0x0112;
    internal const nuint ScScreenSave = 0xF140;

    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SystemParametersInfo(uint action, uint parameter, out int value, uint flags);

    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetSystemParameter(uint action, uint parameter, nint value, uint flags);

    [DllImport("kernel32.dll", EntryPoint = "LoadLibraryExW", CharSet = CharSet.Unicode)]
    internal static extern nint LoadResourceLibrary(string path, nint file, uint flags);
    [DllImport("user32.dll", EntryPoint = "LoadStringW", CharSet = CharSet.Unicode)]
    internal static extern int LoadString(nint module, uint id, System.Text.StringBuilder text, int length);
    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool FreeLibrary(nint module);

    [DllImport("user32.dll", EntryPoint = "PostMessageW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool PostMessage(nint window, uint message, nuint wParam, nint lParam);
}
