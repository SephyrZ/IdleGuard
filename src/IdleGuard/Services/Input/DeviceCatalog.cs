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
    private readonly Dictionary<string, bool> _rules = new(StringComparer.OrdinalIgnoreCase);
    public ObservableCollection<InputDevice> Devices { get; } = new();
    public IReadOnlyCollection<InputDevice> Available => _byPath.Values;
    public event Action? RulesChanged;

    public InputDevice? Find(nint handle) => _byHandle.GetValueOrDefault(handle);
    public InputDevice? Find(string path) => _byPath.GetValueOrDefault(path);
    public Dictionary<string, bool> ExportRules()
    {
        var rules = new Dictionary<string, bool>(_rules, StringComparer.OrdinalIgnoreCase);
        foreach (var device in Devices) rules[device.Path] = device.IsEnabled;
        return rules;
    }

    public void ApplyRules(Dictionary<string, bool>? rules)
    {
        _rules.Clear();
        if (rules is not null) foreach (var rule in rules) _rules[rule.Key] = rule.Value;
        foreach (var device in _byPath.Values)
        {
            if (_rules.TryGetValue(device.Path, out var enabled)) Include(device, enabled);
        }
    }

    public bool Add(InputDevice device)
    {
        if (!device.CanMonitor || !device.IsConnected || !_byPath.TryGetValue(device.Path, out var known) || known != device) return false;
        Include(device, true);
        RulesChanged?.Invoke();
        return true;
    }

    private void Include(InputDevice device, bool enabled)
    {
        device.IsEnabled = enabled;
        if (!Devices.Contains(device)) Devices.Add(device);
    }

    private InputDevice Remember(InputDevice device, bool automatic)
    {
        _byPath.Add(device.Path, device);
        if (_rules.TryGetValue(device.Path, out var enabled)) Include(device, enabled);
        else if (automatic) Include(device, true);
        device.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName != nameof(InputDevice.IsEnabled)) return;
            _rules[device.Path] = device.IsEnabled;
            RulesChanged?.Invoke();
        };
        return device;
    }

    internal void SetXInputConnected(int slot, bool connected)
    {
        var path = $"xinput:{slot}";
        if (!_byPath.TryGetValue(path, out var device))
        {
            if (!connected) return;
            device = Remember(new InputDevice { Path = path, Kind = InputDeviceKind.Controller,
                HardwareName = $"XInput · {slot + 1}", Number = Devices.Count(d => d.Kind == InputDeviceKind.Controller) + 1 }, true);
        }
        device.IsConnected = connected;
    }

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
            var path = ReadPath(entry.Handle);
            if (path is null) continue; // A device can disappear while enumerating.
            // Windows exposes XInput-compatible HID interfaces too. Only poll their XInput slots.
            if (entry.Type == 2 && path.Contains("IG_", StringComparison.OrdinalIgnoreCase)) continue;
            if (!_byPath.TryGetValue(path, out var device))
            {
                using var hid = entry.Type == 2 ? HidDeviceReader.TryCreate(entry.Handle) : null;
                var kind = entry.Type == 0 ? InputDeviceKind.Mouse : entry.Type == 1 ? InputDeviceKind.Keyboard
                    : hid is { UsagePage: 1, Usage: 4 or 5 or 8 } ? InputDeviceKind.Controller : InputDeviceKind.Hid;
                var number = _byPath.Values.Count(d => d.Kind == kind) + 1;
                device = Remember(new InputDevice { Path = path, Kind = kind, Number = number, HardwareName = ReadFriendlyName(path),
                    UsagePage = hid?.UsagePage ?? 0, Usage = hid?.Usage ?? 0, CanMonitor = entry.Type < 2 || hid?.CanMonitor == true },
                    entry.Type < 2 || kind == InputDeviceKind.Controller && hid?.CanMonitor == true);
            }
            handles[entry.Handle] = device;
        }
        var connected = handles.Values.ToHashSet();
        foreach (var device in _byPath.Values.Where(d => !d.Path.StartsWith("xinput:", StringComparison.Ordinal))) device.IsConnected = connected.Contains(device);
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
        if (!OperatingSystem.IsWindows()) return null;
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
