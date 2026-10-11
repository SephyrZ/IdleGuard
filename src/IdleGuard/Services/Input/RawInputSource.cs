using System.ComponentModel;
using System.Runtime.InteropServices;
using IdleGuard.Native;
using IdleGuard.Models;

namespace IdleGuard.Services.Input;

internal sealed class RawInputSource : IDisposable
{
    private readonly InputMessageWindow _window;
    private readonly MouseActivityFilter _mouse = new();
    internal event Action<nint, InputActivityKind>? Activity;
    internal event Action? DevicesChanged;
    internal event Action<bool>? SessionLocked;
    internal event Action<bool>? SystemSuspended;
    internal event Action<Exception>? Error;

    internal RawInputSource()
    {
        _window = new InputMessageWindow();
        _window.Message += OnMessage;
        _window.Error += ex => Error?.Invoke(ex);
    }

    private void OnMessage(uint message, nuint wParam, nint lParam)
    {
        if (message == InputNativeMethods.Input) ReadInput(lParam);
        else if (message == InputNativeMethods.DeviceChange)
        {
            _mouse.Clear();
            DevicesChanged?.Invoke();
        }
        else if (message == InputNativeMethods.SessionChange && (wParam == 7 || wParam == 8))
        {
            _mouse.Clear();
            SessionLocked?.Invoke(wParam == 7);
        }
        else if (message == InputNativeMethods.PowerBroadcast && (wParam == 4 || wParam == 7 || wParam == 18))
        {
            _mouse.Clear();
            SystemSuspended?.Invoke(wParam == 4);
        }
    }

    private void ReadInput(nint input)
    {
        var headerSize = (uint)Marshal.SizeOf<InputNativeMethods.RawHeader>();
        uint size = 0;
        if (InputNativeMethods.GetRawInputData(input, InputNativeMethods.RidInput, 0, ref size, headerSize) == InputNativeMethods.Error)
            throw new Win32Exception(Marshal.GetLastWin32Error());
        if (size < headerSize) return;
        var data = Marshal.AllocHGlobal(checked((int)size));
        try
        {
            var copied = InputNativeMethods.GetRawInputData(input, InputNativeMethods.RidInput, data, ref size, headerSize);
            if (copied == InputNativeMethods.Error) throw new Win32Exception(Marshal.GetLastWin32Error());
            if (copied < headerSize) return;
            var header = Marshal.PtrToStructure<InputNativeMethods.RawHeader>(data);
            if (header.Device == 0) return; // No attributable device: do not reset idle.
            var body = data + (int)headerSize;
            if (header.Type == 0 && copied >= headerSize + Marshal.SizeOf<InputNativeMethods.RawMouse>())
            {
                var mouse = Marshal.PtrToStructure<InputNativeMethods.RawMouse>(body);
                if (_mouse.IsActivity(header.Device, mouse.Flags, mouse.ButtonFlags, mouse.ButtonData, mouse.X, mouse.Y))
                    Activity?.Invoke(header.Device, (mouse.ButtonFlags & 0xC00) != 0 ? InputActivityKind.MouseWheel :
                        (mouse.ButtonFlags & 0x3FF) != 0 ? InputActivityKind.MouseButton : InputActivityKind.MouseMove);
            }
            else if (header.Type == 1 && copied >= headerSize + Marshal.SizeOf<InputNativeMethods.RawKeyboard>())
            {
                var keyboard = Marshal.PtrToStructure<InputNativeMethods.RawKeyboard>(body);
                if (keyboard.VirtualKey != 0xFF && keyboard.MakeCode != 0xFF && keyboard.Message is 0x100 or 0x101 or 0x104 or 0x105)
                    Activity?.Invoke(header.Device, (keyboard.Flags & 1) == 0 ? InputActivityKind.KeyDown : InputActivityKind.KeyUp);
            }
        }
        finally { Marshal.FreeHGlobal(data); }
    }

    public void Dispose() => _window.Dispose();
}
