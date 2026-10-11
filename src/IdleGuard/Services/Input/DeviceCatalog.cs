using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using IdleGuard.Models;
using IdleGuard.Native;
using Microsoft.Win32;

namespace IdleGuard.Services.Input;

public sealed class DeviceCatalog
{
    private readonly Dictionary<string, InputDevice> _byPath = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<nint, InputDevice> _byHandle = new();
    public ObservableCollection<InputDevice> Devices { get; } = new();

    public InputDevice? Find(nint handle) => _byHandle.GetValueOrDefault(handle);

    public void Refresh()
    {
        var entrySize = (uint)Marshal.SizeOf<InputNativeMethods.DeviceListEntry>();
        InputNativeMethods.DeviceListEntry[]? entries = null;
        uint actualCount = 0;
        // A device may arrive between the count query and the list query.
        for (var attempt = 0; attempt < 3; attempt++)
        {
            uint count = 0;
            if (InputNativeMethods.GetRawInputDeviceList(null, ref count, entrySize) == InputNativeMethods.Error)
                throw new Win32Exception(Marshal.GetLastWin32Error());
            entries = new InputNativeMethods.DeviceListEntry[count];
            actualCount = InputNativeMethods.GetRawInputDeviceList(entries, ref count, entrySize);
            if (actualCount != InputNativeMethods.Error) break;
            if (Marshal.GetLastWin32Error() != 122 || attempt == 2) throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        var handles = new Dictionary<nint, InputDevice>();
        foreach (var entry in entries!.Take((int)actualCount))
        {
            if (entry.Type > 1) continue;
            var path = ReadPath(entry.Handle);
            if (path is null) continue; // A device can disappear while enumerating.
            if (!_byPath.TryGetValue(path, out var device))
            {
                var kind = entry.Type == 0 ? InputDeviceKind.Mouse : InputDeviceKind.Keyboard;
                var label = kind == InputDeviceKind.Keyboard ? "键盘" : "鼠标";
                var number = Devices.Count(d => d.Kind == kind) + 1;
                device = new InputDevice { Path = path, Kind = kind, Name = $"{label} {number} · {ReadFriendlyName(path) ?? "HID 设备"}" };
                _byPath.Add(path, device);
                Devices.Add(device);
            }
            handles[entry.Handle] = device;
        }
        var connected = handles.Values.ToHashSet();
        foreach (var device in Devices) device.IsConnected = connected.Contains(device);
        _byHandle = handles;
    }

    private static string? ReadPath(nint handle)
    {
        uint length = 0;
        if (InputNativeMethods.GetRawInputDeviceInfo(handle, InputNativeMethods.RidiDeviceName, null, ref length) == InputNativeMethods.Error || length == 0)
            return null;
        var buffer = new StringBuilder((int)length);
        return InputNativeMethods.GetRawInputDeviceInfo(handle, InputNativeMethods.RidiDeviceName, buffer, ref length) == InputNativeMethods.Error
            ? null : buffer.ToString();
    }

    private static string? ReadFriendlyName(string path)
    {
        // Friendly labels are optional; the full Raw Input path remains the identity.
        var pieces = path.Replace(@"\\?\", "", StringComparison.Ordinal).Split('#');
        if (pieces.Length < 3) return null;
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Enum\{pieces[0]}\{pieces[1]}\{pieces[2]}");
            var text = key?.GetValue("FriendlyName") as string ?? key?.GetValue("DeviceDesc") as string;
            return text?.Split(';')[^1];
        }
        catch (System.Security.SecurityException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }
}
