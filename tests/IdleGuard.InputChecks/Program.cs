using System.Runtime.InteropServices;
using IdleGuard.Models;
using IdleGuard.Native;
using IdleGuard.Services.Input;
using IdleGuard.Services;
using System.Text.Json;
using IdleGuard.Services.Windows;

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
Check(Marshal.SizeOf<TrayNativeMethods.NotifyIconData>() == (IntPtr.Size == 8 ? 976 : 956), "NOTIFYICONDATA native size");
Check(StartupService.CommandFor(@"C:\Apps With Spaces\IdleGuard.exe") == "\"C:\\Apps With Spaces\\IdleGuard.exe\" --background", "Startup command quotes executable paths and requests background launch");
var controller = new IdleController();
var threshold = TimeSpan.FromSeconds(10);
Check(!controller.Evaluate(threshold, threshold, 0, false, false), "Automatic start is initially disabled");
controller.Enable(0);
Check(!controller.Evaluate(TimeSpan.FromSeconds(9), threshold, 0, false, false), "No start before timeout");
Check(!controller.Evaluate(threshold, threshold, 0, true, false), "Paused session cannot trigger");
Check(controller.Evaluate(threshold, threshold, 0, false, false), "Timeout crossing requests one start");
Check(!controller.Evaluate(threshold, threshold, 1, false, false), "Input while request is pending cannot launch again");
controller.CompleteRequest(true, "");
Check(!controller.Evaluate(TimeSpan.FromSeconds(20), threshold, 0, false, false), "Dismissal without accepted input does not re-trigger");
Check(!controller.Evaluate(TimeSpan.Zero, threshold, 1, false, false) && controller.State == IdleControllerState.Monitoring, "Accepted input re-arms a completed cycle");
Check(controller.Evaluate(threshold, threshold, 1, false, false), "A new cycle can launch once");
controller.CompleteRequest(false, "StartUnconfirmed");
Check(!controller.Enabled && !controller.Evaluate(threshold, threshold, 2, false, false), "Unconfirmed start pauses automatic requests");
controller.Enable(2);
Check(!controller.Evaluate(threshold, threshold, 2, false, true) && controller.State == IdleControllerState.Triggered, "Existing Windows screensaver consumes this cycle without a duplicate request");
Check(!controller.Evaluate(threshold, threshold, 2, false, false), "External screensaver dismissal does not loop");
controller.Enable(3);
controller.BeginRequest(3);
controller.Disable();
controller.CompleteRequest(true, "");
Check(controller.State == IdleControllerState.Disabled, "Pausing during a request remains paused after completion");
controller.Enable(4);
Check(!controller.Evaluate(threshold, TimeSpan.Zero, 4, false, false) && controller.State == IdleControllerState.Error, "Invalid timeout fails closed");

var languages = new[] { "en-US", "zh-CN" }.Select(language =>
{
    using var stream = typeof(LocalizationService).Assembly.GetManifestResourceStream($"IdleGuard.Strings.{language}.json")!;
    return JsonSerializer.Deserialize<Dictionary<string, string>>(stream)!;
}).ToArray();
Check(languages[0].Keys.ToHashSet().SetEquals(languages[1].Keys), "English and Chinese resources have matching keys");
var strings = LocalizationService.Current;
strings.SetLanguage("en-US");
Check(strings["Settings"] == "Settings" && strings.Format("Counts", 1, 2).Contains("accepted"), "English resources and formatting load");
var dynamicDevice = new InputDevice { Path = "path", Kind = InputDeviceKind.Mouse, Number = 1, IsConnected = true };
Check(dynamicDevice.Name.StartsWith("Mouse") && dynamicDevice.Description.Contains("Connected"), "Device labels render in English");
strings.SetLanguage("zh-CN");
Check(strings["Settings"] == "设置" && dynamicDevice.Name.StartsWith("鼠标") && dynamicDevice.Description.Contains("已连接"), "Device and interface labels switch to Chinese");
Check(strings.Format("ObservedAt", "Mystify.scr", new DateTime(2026, 10, 10, 14, 5, 6)).Contains("2026-10-10 14:05:06"), "Screensaver observation includes a date and seconds");

var preferencesFile = Path.Combine(AppContext.BaseDirectory, $"preferences-check-{Guid.NewGuid():N}.json");
try
{
    var store = new SettingsStore(preferencesFile);
    Check(store.Load() == new AppSettings(), "Missing settings use defaults");
    var saved = new AppSettings("en-US", true, 42);
    store.Save(saved);
    Check(store.Load() == saved, "Language, timeout mode, and custom duration survive reload");
    store.Save(saved with { UseSystemTimeout = false });
    Check(store.Load().CustomTimeoutSeconds == 42, "Switching timeout modes preserves custom duration");
    store.Save(saved with { MonitoringEnabled = true });
    Check(store.Load().MonitoringEnabled, "Monitoring preference survives reload for background startup");
    Check(new AppSettings("invalid", false, -1).Validated() == new AppSettings(), "Invalid persisted values fall back to valid defaults");
}
finally { if (File.Exists(preferencesFile)) File.Delete(preferencesFile); }
Console.WriteLine($"All {passed} checks passed.");
