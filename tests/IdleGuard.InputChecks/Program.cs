using System.Runtime.InteropServices;
using IdleGuard.Models;
using IdleGuard.Native;
using IdleGuard.Services.Input;

var passed = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new Exception($"FAIL: {name}");
    passed++;
    Console.WriteLine($"PASS: {name}");
}

var now = TimeSpan.Zero;
var tracker = new IdleTracker(() => now);
var device = new InputDevice { Path = "test-keyboard", Name = "Keyboard", Kind = InputDeviceKind.Keyboard, IsConnected = true };
now = TimeSpan.FromSeconds(8);
Check(tracker.Idle.TotalSeconds == 8, "Independent idle advances without input");
Check(tracker.RecordActivity(DevicePolicy.Accepts(device)) && tracker.Idle == TimeSpan.Zero, "Enabled connected device resets idle");
device.IsEnabled = false;
now += TimeSpan.FromSeconds(4);
Check(!tracker.RecordActivity(DevicePolicy.Accepts(device)) && tracker.Idle.TotalSeconds == 4, "Disabled device leaves idle untouched");
device.IsEnabled = true;
device.IsConnected = false;
Check(!tracker.RecordActivity(DevicePolicy.Accepts(device)) && tracker.Idle.TotalSeconds == 4, "Offline device cannot reset idle");
Check(!tracker.RecordActivity(DevicePolicy.Accepts(null)), "Unknown source cannot reset idle");
tracker.Pause();
now += TimeSpan.FromHours(3);
Check(tracker.Idle.TotalSeconds == 4 && !tracker.RecordActivity(true), "Suspended time and input do not change paused idle");
tracker.Resume();
Check(tracker.Idle == TimeSpan.Zero, "Resume establishes a fresh baseline");
now += TimeSpan.FromSeconds(2);
tracker.Resume();
Check(tracker.Idle.TotalSeconds == 2, "Redundant resume or device refresh does not reset idle");

var mouse = new MouseActivityFilter();
Check(!mouse.IsActivity(1, 0, 0, 0, 0, 0), "Empty mouse packets are ignored");
Check(mouse.IsActivity(1, 0, 0, 0, 1, 0), "Relative movement counts");
Check(mouse.IsActivity(1, 0, 1, 0, 0, 0) && mouse.IsActivity(1, 0, 2, 0, 0, 0), "Button press and release count");
Check(!mouse.IsActivity(1, 0, 0x400, 0, 0, 0), "Zero wheel delta is ignored");
Check(mouse.IsActivity(1, 0, 0x400, unchecked((ushort)-120), 0, 0), "Signed vertical wheel counts");
Check(mouse.IsActivity(1, 0, 0x800, 120, 0, 0), "Horizontal wheel counts");
Check(!mouse.IsActivity(1, 1, 0, 0, 100, 100), "First absolute position establishes a baseline");
Check(!mouse.IsActivity(1, 1, 0, 0, 100, 100), "Repeated absolute position is ignored");
Check(mouse.IsActivity(1, 1, 0, 0, 101, 100), "Changed absolute position counts");
Check(!mouse.IsActivity(2, 1, 0, 0, 500, 500), "Absolute baselines are separate per device");
Check(!mouse.IsActivity(1, 3, 0, 0, 500, 500), "Coordinate-space changes establish a fresh baseline");
mouse.Clear();
Check(!mouse.IsActivity(1, 1, 0, 0, 900, 900), "Reconnect clears absolute baseline");
Check(!mouse.IsActivity(1, 4, 0, 0, 0, 0), "Attribute-change-only mouse packet is ignored");

Check(Marshal.SizeOf<InputNativeMethods.RawMouse>() == 24, "RAWMOUSE native size");
Check(Marshal.OffsetOf<InputNativeMethods.RawMouse>("X").ToInt32() == 12, "RAWMOUSE movement union offset");
Check(Marshal.SizeOf<InputNativeMethods.RawKeyboard>() == 16, "RAWKEYBOARD native size");
Check(Marshal.SizeOf<InputNativeMethods.RawHeader>() == (IntPtr.Size == 8 ? 24 : 16), "RAWINPUTHEADER native size");
Check(Marshal.SizeOf<InputNativeMethods.DeviceRegistration>() == (IntPtr.Size == 8 ? 16 : 12), "RAWINPUTDEVICE native size");
Console.WriteLine($"All {passed} input checks passed.");
