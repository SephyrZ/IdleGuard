# Keyboard/mouse input validation

## Scope

Raw Input keyboard, mouse and supported HID interfaces, plus XInput controller slots, feed a saved device allowlist and an application-owned monotonic idle tracker. Other HID interfaces require manual addition from the searchable system input list. The manual screensaver test remains available below every navigation page. Automatic triggering supports custom/system timeouts.

## Automated checks

```powershell
dotnet run --project tests/IdleGuard.InputChecks/IdleGuard.InputChecks.csproj
```

The dependency-free checks compile production logic directly. They cover input policy, monotonic idle, mouse filtering, native layouts, one request per cycle, English/Chinese resources, preference reload, HID signed axes and per-report baselines, axis travel thresholds, button sets, XInput dead zones, held controls, reconnect baselines, administrator preference and device-rule persistence. On Windows they also initialize the production native input monitor, enumerate actual interfaces and register a supported manually added HID if available.

## Controller and manual-device update (2026-10-11)

- 86 checks passed. Native smoke testing initially found 25 input interfaces; a supported consumer-control HID was manually added and registered successfully. The later run found 27 interfaces. Physical controller input has not been verified.
- UI inspection confirmed **Add device** beside **Refresh devices**, searchable Windows input interfaces, disabled unsupported items, and the explicit **Current time** footer label. Searching `VID_04F2` matched consumer-control and vendor interfaces; only the supported consumer-control interface was selectable.
- Switching Chinese to English updated the closed timeout ComboBox to **Custom duration** immediately. ItemsSource is replaced on language change while suppressing selection handlers, preserving the mode and custom duration.
- Settings shows the administrator preference and current permission state. Elevation uses Windows `runas`, preserves the original process if launch is cancelled, and makes the replacement wait for the original to exit. UAC and administrator restart have not been automated.
- The user confirmed that running IdleGuard as administrator restores physical keyboard/mouse activity while BH3 is focused in fullscreen. BH3's embedded manifest requests administrator privileges.
- Xbox-compatible `IG_` HID paths are excluded to avoid double-counting XInput. Connected slots are sampled about every 32 ms; disconnected slots every two seconds. The first reading establishes a baseline; unchanged controls and dead-zone jitter do not reset idle.
- HID reports use Windows capability parsing. Only standard button pages and generic desktop axes/hat switches are supported; telemetry bytes and vendor report changes alone are not activity. Absolute HID axes require 10% logical-range travel. Some proprietary controls may not be readable.
- Saved Raw Input rules use interface paths; XInput rules use labeled connection slots and may apply to another controller after reconnection. Missing device paths retain their rules.

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

Window/status refinement: startup and minimum size are 660 x 660 logical pixels. The sidebar uses the outer window width with DPI normalization: open at 900 or above, compact below 900. The home/navigation label is Screensaver settings; the Windows section has no separate running-state row. Footer result shows not running before any operation, running while active, and preserves the recorded observation after dismissal (request/error feedback is retained). All 96 checks pass; the new width threshold and startup layout still need visual confirmation on the updated executable.

Latest layout/write update: removed the Windows automatic toggle, moved the effective duration beneath the IdleGuard section heading, and placed each group in its own compact card. The rebuilt Release executable was opened and the Chinese home grouping was visually checked. Minimum size is now 720 x 600 logical pixels with automatic sidebar collapse; the exact minimum-size layout and DPI transitions still need visual validation. All 96 read-only/queue checks pass, including a deliberately blocked write returning control immediately, FIFO ordering, and recovery after an earlier write fails. Windows writes now run off the UI thread; the user's reported two-second delay has not been timed on the rebuilt UI.

Compact home layout: effective duration is beside the timing heading, Windows saver and timeout share a row, both system settings links are borderless, and device icons appear in the picker and latest activity. Minimum outer window size is 1000 x 640 logical pixels, scaled for the current DPI using OverlappedPresenter. The 92 read-only checks pass; minimum-size layout and DPI transitions still need native visual validation. The running Release executable was left open, so this build is staged under Release/Update.

Windows screensaver editing: a native round-trip check (`--system-settings-roundtrip`) verified automatic off, timeout write/read, installed saver selection, None disabling automatic startup, and restoration of the original configuration. All 97 checks passed on this host (six installed saver choices, original timeout 720 seconds). The normal check run is read-only. The new home layout and native settings dialog interaction still need a visual run of the rebuilt executable; the existing executable was left open during packaging. External dialog edits synchronize after Apply/OK, not while they are uncommitted.

- Unplug/reconnect with the same path: selection should remain, and unplug alone should not reset idle.
- Main window minimized or another app focused: accepted physical input should still reset idle.
- Hidden/minimized presentation gate: confirm the displayed clock and summaries stop updating while hidden, restore immediately to current values on reopening, and an idle timeout still launches the saver. The input checks pass after this change; the updated layout and hide/restore behavior still need a native run of the new executable (the previous executable remained open during packaging).
- Sleep/resume and lock/unlock: paused time should not count; resume should establish a fresh baseline.
- Absolute pointing devices and horizontal wheels: validate against actual hardware, in addition to deterministic packet tests.
- Bluetooth/wired and USB-port changes can change paths and are treated as a new interface. Saved rules apply when the same path reappears.
- Physical XInput and generic HID controllers: buttons, triggers, hats, drift, held controls, hot-plug, background/fullscreen input and source deduplication.
- Administrator preference: user-approved restart, cancelled UAC, reopening an elevated instance, and Windows sign-in startup. Startup still uses HKCU Run and may prompt; no silent elevated scheduled task is installed.

Synthetic input without an attributable Raw Input device is intentionally ignored. UI automation alone cannot validate physical source routing. Normal input is not suppressed: registration uses `RIDEV_INPUTSINK | RIDEV_DEVNOTIFY`, without `RIDEV_NOLEGACY`.

## References

- [Raw Input](https://learn.microsoft.com/en-us/windows/win32/inputdev/raw-input)
- [RAWINPUTDEVICE registration flags](https://learn.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-rawinputdevice)
- [RAWMOUSE](https://learn.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-rawmouse)
