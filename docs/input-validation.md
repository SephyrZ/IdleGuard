# Keyboard/mouse input validation

## Scope

Raw Input keyboard and mouse interfaces feed a device-path allowlist and an application-owned monotonic idle tracker. The manual screensaver test remains available in the fixed window footer. No automatic timeout trigger or game controller support is included yet.

## Automated checks

```powershell
dotnet run --project tests/IdleGuard.InputChecks/IdleGuard.InputChecks.csproj
```

The dependency-free checks compile the production policy, idle tracker, mouse filter, models, and native layout declarations directly. All 26 checks passed: enabled input resets idle; disabled, unknown, and offline sources do not; paused input and suspended time do not advance idle; resume resets the baseline; redundant resume does not. Mouse checks cover empty packets, relative movement, button transitions, vertical/horizontal wheels, repeated absolute positions, separate device baselines, reconnect, and coordinate-space changes. Layout checks cover the Win32 structures used to decode packets.

## Observed on the development machine

- Debug x64 build succeeded with zero warnings and zero errors.
- The window listed seven keyboard and four mouse interfaces, including friendly labels and device paths.
- Actual mouse input was observed from Razer Naga Trinity, accepted, and reset the displayed idle time.
- The user confirmed physical input displayed a source and reset idle, then confirmed that unchecking the source showed ignored input while idle continued to grow.
- The manual screensaver button remains visible in the fixed footer while the main content scrolls.

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
