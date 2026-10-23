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

Check(PowerRequestService.ParseDisplayRequest("DISPLAY:\r\n[PROCESS] C:\\Browser\\chrome.exe\r\nVideo Wake Lock\r\n\r\nSYSTEM:\r\nNone.\r\n"),
    "A persistent browser DISPLAY request is detected");
Check(PowerRequestService.ParseDisplayRequest("DISPLAY:\n[驱动程序] 显示驱动\n原因\nSYSTEM:\n无。\n"),
    "Localized driver DISPLAY requests are also respected");
Check(!PowerRequestService.ParseDisplayRequest("DISPLAY:\r\nNone.\r\n\r\nSYSTEM:\r\n[PROCESS] downloader.exe\r\nEXECUTION:\r\n[PROCESS] worker.exe\r\n"),
    "System and execution requests alone do not defer the screensaver");
Check(!PowerRequestService.ParseDisplayRequest("DISPLAY:\n无。\n\nSYSTEM:\n无。\n"),
    "Chinese no-request output does not depend on console text encoding");
var requestSources = PowerRequestService.ParseDisplayRequestSources("DISPLAY:\r\n[PROCESS] \\Device\\HarddiskVolume3\\视频 播放器\\PotPlayer.exe\r\n视频播放\r\n[DRIVER] USB Audio Device\r\nAudio Stream\r\n[PROCESS] \\Device\\HarddiskVolume3\\视频 播放器\\PotPlayer.exe\r\nSYSTEM:\r\n[PROCESS] ignored.exe\r\n");
Check(requestSources.SequenceEqual(new[] { @"\Device\HarddiskVolume3\视频 播放器\PotPlayer.exe", "USB Audio Device" }),
    "Tooltip and clipboard sources preserve Unicode and spaces, deduplicate, and exclude reasons and SYSTEM requests");
foreach (var malformed in new[] { "This command requires administrator privileges.", "DISPLAY:\nNone.\n", "DISPLAY:\n\nSYSTEM:\nNone.\n", "DISPLAY:\n[PROCESS\nSYSTEM:\nNone.\n" })
{
    var rejected = false;
    try { PowerRequestService.ParseDisplayRequest(malformed); }
    catch (FormatException) { rejected = true; }
    Check(rejected, "Failed or incomplete powercfg output cannot be treated as no requests");
}

var powerNow = TimeSpan.Zero;
var powerTracker = new IdleTracker(() => powerNow);
var powerController = new IdleController();
var powerTimeout = TimeSpan.FromMinutes(5);
powerController.Enable(powerTracker.Cycle);
powerNow += powerTimeout;
Check(powerController.Evaluate(powerTracker.Idle, powerTimeout, powerTracker.Cycle, false, false), "Display check is reached only at the idle deadline");
powerTracker.Restart();
powerController.Enable(powerTracker.Cycle);
Check(powerTracker.Idle == TimeSpan.Zero && !powerController.Evaluate(powerTracker.Idle, powerTimeout, powerTracker.Cycle, false, false),
    "A display request restarts a full idle cycle without leaving the controller triggering");
powerNow += powerTimeout - TimeSpan.FromSeconds(1);
Check(!powerController.Evaluate(powerTracker.Idle, powerTimeout, powerTracker.Cycle, false, false), "No repeated automatic check before the next full timeout");
powerNow += TimeSpan.FromSeconds(1);
Check(powerController.Evaluate(powerTracker.Idle, powerTimeout, powerTracker.Cycle, false, false), "The next full timeout permits a fresh display check");

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
Check(!DevicePolicy.Accepts(new InputDevice { Path = "unsupported", Kind = InputDeviceKind.Hid, IsConnected = true, CanMonitor = false }), "Unsupported interfaces cannot reset idle even if enabled");
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
    return JsonSerializer.Deserialize(stream, AppJsonContext.Default.LanguageDictionary)!;
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
    Check(store.Load().RunAsAdministrator, "First launch requests administrator permissions by default");
    File.WriteAllText(preferencesFile, """{"RunAsAdministrator":false}""");
    Check(!store.Load().RunAsAdministrator, "An existing explicit administrator opt-out is respected");
    store.Save(new AppSettings(RunAsAdministrator: false));
    Check(!store.Load().RunAsAdministrator, "Turning administrator startup off remains off after reload");
    File.WriteAllText(preferencesFile, """
        {"Language":"en-US","CustomTimeoutSeconds":37,"RunAsAdministrator":true,
         "DeviceRules":{"HID\\VID_1234&PID_5678":false,"设备":true},"FutureSetting":123}
        """);
    var legacy = store.Load();
    Check(legacy.Language == "en-US" && legacy.CustomTimeoutSeconds == 37 && legacy.RunAsAdministrator
        && !legacy.UseSystemTimeout && !legacy.MonitoringEnabled
        && legacy.DeviceRules is { Count: 2 } && !legacy.DeviceRules[@"HID\VID_1234&PID_5678"] && legacy.DeviceRules["设备"],
        "Generated JSON reader accepts existing preferences, missing optional fields, Unicode device rules and unknown fields");
    store.Save(legacy);
    var legacyReloaded = store.Load();
    Check(legacyReloaded.DeviceRules!.OrderBy(p => p.Key).SequenceEqual(legacy.DeviceRules!.OrderBy(p => p.Key))
        && legacyReloaded.RunAsAdministrator, "Generated JSON writer preserves device rules and administrator preference");
    File.WriteAllText(preferencesFile, "{}");
    Check(store.Load() == new AppSettings(), "Empty legacy preference object retains constructor defaults");
    Check(store.Load().RunAsAdministrator, "Preferences without an administrator field use the new first-launch default");
    File.WriteAllText(preferencesFile, "null");
    var nullRejected = false;
    try { store.Load(); } catch (InvalidDataException) { nullRejected = true; }
    Check(nullRejected, "Null preferences are rejected rather than silently overwritten");
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
var controls = new ControlActivityFilter();
Check(!controls.ValueChanged("axis", 0.5, 0.1), "First HID axis reading establishes baseline");
Check(!controls.ValueChanged("axis", 0.55, 0.1), "HID axis jitter below travel threshold is ignored");
Check(controls.ValueChanged("axis", 0.7, 0.1), "Intentional HID axis movement counts");
Check(!controls.ValueChanged("axis", 0.7, 0.1), "Held HID axis cannot keep resetting idle");
Check(!controls.ValueChanged("axis", double.NaN, 0.1), "Invalid analog values are ignored");
Check(!controls.ValueChanged("other-report", 0.3, 0.1), "Separate HID report controls have independent baselines");
Check(!controls.ButtonsChanged("buttons", new ushort[] { 1, 2 }), "First HID button report establishes baseline");
Check(!controls.ButtonsChanged("buttons", new ushort[] { 2, 1 }), "HID button order is not activity");
Check(controls.ButtonsChanged("buttons", new ushort[] { 2 }), "HID button release counts");
Check(controls.ButtonsChanged("buttons", new ushort[] { 2, 3 }), "HID button press counts");
controls.Clear();
Check(!controls.ValueChanged("axis", 1, 0.1) && !controls.ButtonsChanged("buttons", Array.Empty<ushort>()), "Reconnect clears HID baselines without reporting input");

var pad = new ControllerActivityFilter();
Check(!pad.IsActivity(0, 0, 0, 0, 0, 0, 0), "Controller connection establishes baseline");
Check(!pad.IsActivity(0, 30, 29, 7849, -7849, 8689, -8689), "Controller stick and trigger dead zones ignore drift");
Check(!pad.IsActivity(0, 31, 0, 7850, 0, 8690, 0), "Jitter across dead-zone boundaries is ignored");
Check(pad.IsActivity(0x1000, 0, 0, 0, 0, 0, 0), "Controller button press counts");
Check(!pad.IsActivity(0x1000, 0, 0, 0, 0, 0, 0), "Held controller button does not repeat");
Check(pad.IsActivity(0, 0, 0, 0, 0, 0, 0), "Controller button release counts");
Check(pad.IsActivity(0, 0, 0, short.MaxValue, 0, 0, 0), "Controller stick movement counts");
Check(!pad.IsActivity(0, 0, 0, 32500, 0, 0, 0), "Small movement around held controller axis is ignored");
Check(pad.IsActivity(0, 100, 0, 32500, 0, 0, 0), "Controller trigger movement counts");
Check(pad.IsActivity(0, 100, 0, short.MinValue, 0, 0, 0), "Negative full controller axis is handled without overflow");
pad.Clear();
Check(!pad.IsActivity(0x1000, 0, 0, short.MaxValue, 0, 0, 0), "Reconnected controller does not reset idle by attachment alone");

Check(Marshal.SizeOf<ControllerNativeMethods.Gamepad>() == 12 && Marshal.SizeOf<ControllerNativeMethods.State>() == 16, "XInput native layouts");
Check(Marshal.SizeOf<HidNativeMethods.Caps>() == 64 && Marshal.SizeOf<HidNativeMethods.ButtonCaps>() == 72
    && Marshal.SizeOf<HidNativeMethods.ValueCaps>() == 72, "HID capability native layouts");
Check(HidDeviceReader.SignedValue(0xFF, 8, true) == -1 && HidDeviceReader.SignedValue(0xFF, 8, false) == 255, "Signed HID axes use descriptor bit width");
Check(HidDeviceReader.SignedValue(0x80000000, 32, true) == int.MinValue, "32-bit HID signed minimum");

var rulesFile = Path.Combine(AppContext.BaseDirectory, $"device-rules-check-{Guid.NewGuid():N}.json");
try
{
    var store = new SettingsStore(rulesFile);
    store.Save(new AppSettings(RunAsAdministrator: true, DeviceRules: new() { ["OFFLINE-PATH"] = false, ["manual-hid"] = true }));
    var loaded = store.Load();
    Check(loaded.RunAsAdministrator && loaded.DeviceRules!["manual-hid"] && !loaded.DeviceRules["OFFLINE-PATH"], "Administrator preference and manual device choices survive reload");
    var catalog = new DeviceCatalog();
    catalog.ApplyRules(loaded.DeviceRules);
    Check(!catalog.ExportRules()["offline-path"] && catalog.ExportRules()["manual-hid"], "Offline saved device choices survive enumeration gaps and use case-insensitive paths");
    Check(!catalog.Add(new InputDevice { Path = "untrusted", Kind = InputDeviceKind.Hid, IsConnected = true }), "Devices outside the system catalog cannot be manually added");
}
finally { if (File.Exists(rulesFile)) File.Delete(rulesFile); }

var backgroundWriter = new BackgroundSettingsWriter();
using (var releaseWrite = new ManualResetEventSlim())
{
    var enteredWrite = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var writeOrder = new List<int>();
    var firstWrite = backgroundWriter.Enqueue(() =>
    {
        enteredWrite.SetResult();
        if (!releaseWrite.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("Test did not release the write.");
        writeOrder.Add(1);
    });
    try
    {
        await enteredWrite.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Check(!firstWrite.IsCompleted, "A blocked Windows setting write returns control to the caller");
        var secondWrite = backgroundWriter.Enqueue(() => writeOrder.Add(2));
        Check(!secondWrite.IsCompleted, "New setting edits wait behind an unfinished write");
        releaseWrite.Set();
        await Task.WhenAll(firstWrite, secondWrite).WaitAsync(TimeSpan.FromSeconds(5));
        Check(writeOrder.SequenceEqual(new[] { 1, 2 }), "Background settings writes preserve edit order");
    }
    finally { releaseWrite.Set(); }
}
var failingWrite = backgroundWriter.Enqueue(() => throw new InvalidOperationException("Expected failure"));
var recoveredWrite = false;
var nextWrite = backgroundWriter.Enqueue(() => recoveredWrite = true);
try { await failingWrite; }
catch (InvalidOperationException) { }
await nextWrite.WaitAsync(TimeSpan.FromSeconds(5));
Check(recoveredWrite, "A failed system write does not block later setting edits");
await backgroundWriter.DrainAsync();

if (OperatingSystem.IsWindows())
{
    try
    {
        var displayRequested = await new PowerRequestService().HasDisplayRequestAsync(CancellationToken.None);
        Check(true, $"Live powercfg query parsed successfully (DISPLAY active: {displayRequested})");
    }
    catch (Exception ex) when (ex is IOException or FormatException)
    {
        Check(ex.Message.Contains("powercfg", StringComparison.OrdinalIgnoreCase),
            "Live powercfg permission/output failure is surfaced instead of returning no requests");
        Console.WriteLine($"Power request query unavailable: {ex.Message}");
    }
    using (var icons = new AppIcons(0))
    {
        Check(icons.Small != 0 && icons.Large != 0 && icons.Small != icons.Large, "Embedded small and full icon frames load as independent native icons");
        icons.Dispose();
        Check(icons.Small == 0 && icons.Large == 0, "Application icon handles are released and disposal is repeatable");
    }
    var saverService = new ScreenSaverService();
    var saverConfig = saverService.ReadConfiguration();
    var saverChoices = saverService.GetChoices("(None)", saverConfig.Path);
    Check(saverChoices[0].Path is null, "Screensaver list includes None without executing saver code");
    Check(saverChoices.Skip(1).All(choice => !string.IsNullOrWhiteSpace(choice.Name) && System.IO.Path.IsPathRooted(choice.Path!)), "Installed screensavers have display names and absolute paths");
    Check(saverChoices.Select(choice => choice.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() == saverChoices.Count, "Screensaver paths are deduplicated");
    var resolvedCurrentSaver = saverConfig.Path is null ? null : ScreenSaverService.ResolvePath(saverConfig.Path);
    Check(saverConfig.Path is null || saverChoices.Any(choice => string.Equals(choice.Path, resolvedCurrentSaver, StringComparison.OrdinalIgnoreCase)), "Current third-party screensaver is retained in the picker");
    foreach (var invalidTimeout in new[] { 0, -1 })
    {
        bool rejected = false;
        try { saverService.SetTimeout(invalidTimeout); }
        catch (ArgumentOutOfRangeException) { rejected = true; }
        Check(rejected, "Invalid system timeout is rejected before writing Windows settings");
    }
    Console.WriteLine($"Screensaver read-only check: {saverChoices.Count - 1} choices, {saverConfig.TimeoutSeconds}s, automatic={saverConfig.AutomaticEnabled}.");
    if (args.Contains("--system-settings-roundtrip") && !saverConfig.IsManaged && saverConfig.TimeoutSeconds > 0)
    {
        var originalPath = saverConfig.Path is null ? null : ScreenSaverService.ResolvePath(saverConfig.Path);
        try
        {
            saverService.SetAutomatic(false);
            Check(!saverService.ReadConfiguration().AutomaticEnabled, "Windows automatic screensaver write is read back");
            var testTimeout = saverConfig.TimeoutSeconds < int.MaxValue - 60 ? saverConfig.TimeoutSeconds + 60 : 600;
            saverService.SetTimeout(testTimeout);
            Check(saverService.ReadConfiguration().TimeoutSeconds == testTimeout, "Windows timeout write is read back through SystemParametersInfo");
            var alternative = saverChoices.FirstOrDefault(choice => choice.Path is not null && File.Exists(choice.Path) && !string.Equals(choice.Path, originalPath, StringComparison.OrdinalIgnoreCase));
            if (alternative is not null)
            {
                saverService.SetScreenSaver(alternative.Path);
                Check(saverService.ReadConfiguration().Path == alternative.Path, "Installed screensaver selection is read back without launching it");
            }
            saverService.SetScreenSaver(null);
            var none = saverService.ReadConfiguration();
            Check(none.Path is null && !none.AutomaticEnabled, "Selecting None removes the saver and disables Windows automatic startup");
        }
        finally
        {
            saverService.SetScreenSaver(originalPath);
            saverService.SetTimeout(saverConfig.TimeoutSeconds);
            saverService.SetAutomatic(saverConfig.AutomaticEnabled);
        }
        var restored = saverService.ReadConfiguration();
        Check(restored.Path == originalPath && restored.TimeoutSeconds == saverConfig.TimeoutSeconds && restored.AutomaticEnabled == saverConfig.AutomaticEnabled, "Original Windows screensaver configuration restored after round-trip check");
    }
    using var monitor = new InputMonitor();
    Check(monitor.Error is null, "Native input window, HID enumeration and XInput initialize without errors");
    Check(monitor.Catalog.Devices.All(d => d.Kind != InputDeviceKind.Hid), "Other HID interfaces require explicit manual addition");
    var candidate = monitor.Catalog.Available.FirstOrDefault(d => d.Kind == InputDeviceKind.Hid && d.IsConnected && d.CanMonitor);
    if (candidate is not null)
    {
        Check(monitor.Catalog.Add(candidate), "A supported connected HID can be manually added");
        monitor.RefreshDevices();
        Check(monitor.Error is null && monitor.Catalog.Devices.Contains(candidate), "Manual HID registration succeeds and survives refresh");
        candidate.IsEnabled = false;
        Check(!monitor.Catalog.ExportRules()[candidate.Path], "Disabled manual HID choice is retained");
    }
    Console.WriteLine($"Native smoke check: {monitor.Catalog.Devices.Count} monitored, {monitor.Catalog.Available.Count} system input interfaces.");
}
Console.WriteLine($"All {passed} checks passed.");
