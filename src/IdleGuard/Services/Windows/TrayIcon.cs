using System.ComponentModel;
using System.Runtime.InteropServices;
using IdleGuard.Native;

namespace IdleGuard.Services.Windows;

/// <summary>UI-thread notification icon and native menu; the window remains alive when hidden.</summary>
internal sealed class TrayIcon : IDisposable
{
    private readonly TrayNativeMethods.SubclassProcedure _procedure;
    private TrayNativeMethods.NotifyIconData _data;
    private readonly Action _open, _startup, _monitor, _exit;
    private readonly Func<bool> _startupEnabled, _monitorEnabled;
    private readonly Action<Exception> _error;
    private bool _disposed;
    internal bool Available { get; private set; }

    internal TrayIcon(nint window, Action open, Action startup, Action monitor, Action exit,
        Func<bool> startupEnabled, Func<bool> monitorEnabled, Action<Exception> error)
    {
        _open = open; _startup = startup; _monitor = monitor; _exit = exit;
        _startupEnabled = startupEnabled; _monitorEnabled = monitorEnabled; _error = error;
        _procedure = WndProc;
        _data = new() { Size = (uint)Marshal.SizeOf<TrayNativeMethods.NotifyIconData>(), Window = window,
            Id = 1, Flags = 1 | 2 | 4, CallbackMessage = TrayNativeMethods.Callback,
            Icon = TrayNativeMethods.LoadIcon(0, 32512), Tip = "IdleGuard", Info = "", InfoTitle = "" };
        if (!TrayNativeMethods.SetWindowSubclass(window, _procedure, 1, 0)) throw new Win32Exception(Marshal.GetLastWin32Error());
        try { Add(); }
        catch { TrayNativeMethods.RemoveWindowSubclass(window, _procedure, 1); throw; }
    }

    private void Add()
    {
        Available = TrayNativeMethods.NotifyIcon(0, ref _data);
        if (!Available) throw new Win32Exception(Marshal.GetLastWin32Error());
        _data.Version = 4;
        TrayNativeMethods.NotifyIcon(4, ref _data);
    }

    private nint WndProc(nint window, uint message, nuint wParam, nint lParam, nuint id, nuint data)
    {
        try
        {
            if (message == 0x16 && wParam != 0) { _exit(); return 0; } // WM_ENDSESSION: allow logoff/shutdown.
            if (message == TrayNativeMethods.ShowWindowMessage) { _open(); return 0; }
            if (message == TrayNativeMethods.TaskbarCreated) { Available = false; Add(); return 0; }
            if (message == TrayNativeMethods.Callback)
            {
                var notification = (uint)((nuint)lParam & 0xffff);
                if (notification is 0x400 or 0x401 or 0x202) _open(); // NIN_SELECT, keyboard selection, legacy click
                else if (notification is 0x7b or 0x205) ShowMenu();
                return 0;
            }
        }
        catch (Exception ex) { _error(ex); }
        return TrayNativeMethods.DefSubclassProc(window, message, wParam, lParam);
    }

    private void ShowMenu()
    {
        var menu = TrayNativeMethods.CreatePopupMenu();
        if (menu == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
        uint command;
        try
        {
            var strings = LocalizationService.Current;
            void Item(uint flags, nuint id, string? label)
            {
                if (!TrayNativeMethods.AppendMenu(menu, flags, id, label)) throw new Win32Exception(Marshal.GetLastWin32Error());
            }
            Item(0, 1, strings["OpenWindow"]);
            Item(_startupEnabled() ? 8u : 0, 2, strings["LaunchAtStartup"]);
            Item(_monitorEnabled() ? 8u : 0, 3, strings["MonitorScreensaver"]);
            Item(0x800, 0, null);
            Item(0, 4, strings["Exit"]);
            if (!TrayNativeMethods.GetCursorPos(out var point)) throw new Win32Exception(Marshal.GetLastWin32Error());
            TrayNativeMethods.SetForegroundWindow(_data.Window);
            command = TrayNativeMethods.TrackPopupMenu(menu, 0x100 | 0x2, point.X, point.Y, 0, _data.Window, 0);
            NativeMethods.PostMessage(_data.Window, 0, 0, 0);
        }
        finally { TrayNativeMethods.DestroyMenu(menu); }
        switch (command) { case 1: _open(); break; case 2: _startup(); break; case 3: _monitor(); break; case 4: _exit(); break; }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        TrayNativeMethods.NotifyIcon(2, ref _data);
        TrayNativeMethods.RemoveWindowSubclass(_data.Window, _procedure, 1);
        Available = false;
        GC.KeepAlive(_procedure);
    }
}
