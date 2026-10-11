# Keyboard/mouse input validation

## Scope

Raw Input keyboard and mouse interfaces feed a device-path allowlist and an application-owned monotonic idle tracker. The manual screensaver test remains available below every navigation page. Automatic triggering supports custom/system timeouts; game controllers are not included yet.

## Automated checks

```powershell
dotnet run --project tests/IdleGuard.InputChecks/IdleGuard.InputChecks.csproj
```

The dependency-free checks compile production logic directly. All 51 checks passed. The original 26 cover input policy, monotonic idle, mouse filtering, and native layouts. Additional checks cover one request per cycle, no repeated launch after dismissal, new-cycle rearming, paused sessions, failure requiring explicit retry, existing Windows savers, disabling during a pending request, invalid timeouts, matching English/Chinese keys, dynamic device translation, preference reload, retaining custom time when switching modes, notification-icon structure size, quoted background startup commands, monitoring preference reload, and timestamps with seconds.

## Observed on the development machine

- Debug x64 build succeeded with zero warnings and zero errors.
- The window listed seven keyboard and four mouse interfaces, including friendly labels and device paths.
- Actual mouse input was observed from Razer Naga Trinity, accepted, and reset the displayed idle time.
- The user confirmed physical input displayed a source and reset idle, then confirmed that unchecking the source showed ignored input while idle continued to grow.
- The manual screensaver button remains visible in the fixed footer while the main content scrolls.
- English switching updated sidebar labels, settings, device labels, and runtime messages immediately; English persisted after closing and reopening the app.
- The user confirmed automatic screensaver start after setting a five-second timeout and clicking Start monitoring.
- The user confirmed tray left-click opens the window and right-click exposes the startup/monitoring checkboxes.
- Closing the window left the process responsive with no visible main window. Launching again restored the original window handle and left one process running.
- The user confirmed a five-second timeout still started the screensaver with the window closed and that reopening showed the saver name and observation date/time with seconds.
- Release x64 build with tray/startup support succeeded with zero warnings and zero errors. Windows sign-in startup has not been tested by logging out or restarting.
- The compact single-file release launched from the top-level Release directory with dependencies bundled. The project now references WinUI and Runtime component packages instead of the full umbrella SDK; the resolved dependency graph excludes the unused AI, ML and Widgets packages. The compressed executable is about 84.8 MiB, including the .NET runtime, compared with the old 137.8 MiB build folder which did not include .NET. Single-file extraction still consumes cache disk space.

The four-page sidebar keeps live monitoring independent from the visible page. Language resources are embedded JSON dictionaries; application-created text switches immediately, while hardware names and native Windows error descriptions retain their system-provided wording.

## Remaining hardware coverage

- Unplug/reconnect with the same path: selection should remain, and unplug alone should not reset idle.
- Main window minimized or another app focused: accepted physical input should still reset idle.
- Sleep/resume and lock/unlock: paused time should not count; resume should establish a fresh baseline.
- Absolute pointing devices and horizontal wheels: validate against actual hardware, in addition to deterministic packet tests.
- Bluetooth/wired and USB-port changes can change paths and are treated as a new interface. Saved rules across app restarts are planned separately.

Synthetic input without an attributable Raw Input device is intentionally ignored. UI automation alone cannot validate physical source routing. Normal input is not suppressed: registration uses `RIDEV_INPUTSINK | RIDEV_DEVNOTIFY`, without `RIDEV_NOLEGACY`.

## References

- [Raw Input](https://learn.microsoft.com/en-us/windows/win32/inputdev/raw-input)
- [RAWINPUTDEVICE registration flags](https://learn.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-rawinputdevice)
- [RAWMOUSE](https://learn.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-rawmouse)
