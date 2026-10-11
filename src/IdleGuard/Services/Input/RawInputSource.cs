using System.ComponentModel;
using System.Runtime.InteropServices;
using IdleGuard.Native;
using IdleGuard.Models;

namespace IdleGuard.Services.Input;

internal sealed class RawInputSource : IDisposable
{
    private readonly InputMessageWindow _window;
    private readonly MouseActivityFilter _mouse = new();
    private readonly Dictionary<nint, HidDeviceReader> _hid = new();
    private readonly Func<nint, bool> _monitoredHid;
    internal event Action<nint, InputActivityKind>? Activity;
    internal event Action? DevicesChanged;
    internal event Action<bool>? SessionLocked;
    internal event Action<bool>? SystemSuspended;
    internal event Action<Exception>? Error;

    internal RawInputSource(Func<nint, bool> monitoredHid)
    {
        _monitoredHid = monitoredHid;
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
            ClearHid();
            DevicesChanged?.Invoke();
        }
        else if (message == InputNativeMethods.SessionChange && (wParam == 7 || wParam == 8))
        {
            _mouse.Clear();
            ClearHid();
            SessionLocked?.Invoke(wParam == 7);
        }
        else if (message == InputNativeMethods.PowerBroadcast && (wParam == 4 || wParam == 7 || wParam == 18))
        {
            _mouse.Clear();
            ClearHid();
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
            else if (header.Type == 2 && copied >= headerSize + 8)
            {
                // Registration is per usage, so it can also deliver unselected or duplicate XInput interfaces.
                if (!_monitoredHid(header.Device)) return;
                var reportSize = unchecked((uint)Marshal.ReadInt32(body));
                var count = unchecked((uint)Marshal.ReadInt32(body, 4));
                if (reportSize == 0 || (ulong)reportSize * count > copied - headerSize - 8) return;
                if (!_hid.TryGetValue(header.Device, out var reader))
                {
                    reader = HidDeviceReader.TryCreate(header.Device);
                    if (reader is null) return;
                    _hid[header.Device] = reader;
                }
                var report = new byte[checked((int)reportSize)];
                var active = false;
                for (var index = 0u; index < count; index++)
                {
                    Marshal.Copy(body + 8 + checked((int)(index * reportSize)), report, 0, report.Length);
                    active |= reader.IsActivity(report);
                }
                if (active) Activity?.Invoke(header.Device, reader.UsagePage == 1 && reader.Usage is 4 or 5 or 8
                    ? InputActivityKind.ControllerInput : InputActivityKind.HidInput);
            }
        }
        finally { Marshal.FreeHGlobal(data); }
    }

    internal void SetHidDevices(IEnumerable<InputDevice> devices) => _window.SetHidDevices(devices);
    private void ClearHid() { foreach (var reader in _hid.Values) reader.Dispose(); _hid.Clear(); }
    public void Dispose() { ClearHid(); _window.Dispose(); }
}
