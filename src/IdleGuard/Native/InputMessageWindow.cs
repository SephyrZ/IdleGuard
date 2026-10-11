using System.ComponentModel;
using System.Runtime.InteropServices;

namespace IdleGuard.Native;

/// <summary>Hidden top-level window; owns Raw Input registration on the app's UI thread.</summary>
internal sealed class InputMessageWindow : IDisposable
{
    private readonly string _className = $"IdleGuard.Input.{Guid.NewGuid():N}";
    private readonly InputNativeMethods.WindowProcedure _procedure;
    private readonly nint _instance = InputNativeMethods.GetModuleHandle(null);
    private bool _registered, _inputRegistered;
    internal nint Handle { get; private set; }
    internal event Action<uint, nuint, nint>? Message;
    internal event Action<Exception>? Error;

    internal InputMessageWindow()
    {
        _procedure = WndProc;
        try
        {
            var windowClass = new InputNativeMethods.WindowClass
            {
                Size = (uint)Marshal.SizeOf<InputNativeMethods.WindowClass>(),
                Procedure = _procedure, Instance = _instance, ClassName = _className
            };
            if (InputNativeMethods.RegisterClassEx(ref windowClass) == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
            _registered = true;
            Handle = InputNativeMethods.CreateWindowEx(0, _className, "IdleGuard input listener", 0,
                0, 0, 0, 0, 0, 0, _instance, 0);
            if (Handle == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
            RegisterInput(InputNativeMethods.InputSink | InputNativeMethods.DeviceNotify, Handle);
            _inputRegistered = true;
            if (!InputNativeMethods.WTSRegisterSessionNotification(Handle, 0)) throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        catch { Dispose(); throw; }
    }

    private static void RegisterInput(uint flags, nint target)
    {
        InputNativeMethods.DeviceRegistration[] devices =
        [new() { UsagePage = 1, Usage = 2, Flags = flags, Target = target },
         new() { UsagePage = 1, Usage = 6, Flags = flags, Target = target }];
        if (!InputNativeMethods.RegisterRawInputDevices(devices, (uint)devices.Length,
                (uint)Marshal.SizeOf<InputNativeMethods.DeviceRegistration>()))
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    private nint WndProc(nint window, uint message, nuint wParam, nint lParam)
    {
        // Never let a managed exception cross the native callback boundary.
        try { Message?.Invoke(message, wParam, lParam); }
        catch (Exception ex) { Error?.Invoke(ex); }
        // Required for foreground WM_INPUT cleanup; normal input is never suppressed.
        return InputNativeMethods.DefWindowProc(window, message, wParam, lParam);
    }

    public void Dispose()
    {
        if (_inputRegistered)
        {
            try { RegisterInput(InputNativeMethods.Remove, 0); }
            catch (Win32Exception ex) { Error?.Invoke(ex); }
            _inputRegistered = false;
        }
        if (Handle != 0)
        {
            InputNativeMethods.WTSUnRegisterSessionNotification(Handle);
            InputNativeMethods.DestroyWindow(Handle);
            Handle = 0;
        }
        if (_registered) { InputNativeMethods.UnregisterClass(_className, _instance); _registered = false; }
        GC.KeepAlive(_procedure);
    }
}
