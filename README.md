# IdleGuard

**English** | [简体中文](README.zh-CN.md)

A Windows utility in development that tracks its own idle time using selected input devices and starts the configured Windows screensaver when the timeout is reached. The planned desktop app uses C# and WinUI 3.

## Current status

The project is at the prototype stage. A complete desktop app is not yet available.

It includes a WinUI 3 window with keyboard/mouse selection, an independent idle timer, and manual screensaver testing. [RawIdleTest.ps1](prototypes/RawIdleTest.ps1) remains available as a separate diagnostic script. Automatic timeout triggering and controller support are not connected yet.

## Run the validation window

Requirements: Windows 10 version 2004 or later (x64), .NET 10 SDK, and NuGet access for the first build. The Windows App SDK runtime is included in the build output; no MSIX installation is needed.

```powershell
dotnet build src/IdleGuard/IdleGuard.csproj -c Debug -p:Platform=x64
& ./src/IdleGuard/bin/x64/Debug/net10.0-windows10.0.19041.0/win-x64/IdleGuard.exe
```

The prototype UI is currently Chinese. It shows the configured screensaver, system timeout, automatic screensaver setting, and current running state. **Refresh information** reloads configuration; **Start screensaver** sends one request and observes the running state in the background for up to ten seconds. Keep input devices still after clicking, then dismiss the saver to review the retained result. A request being sent does not prove the saver started; the result reports whether running was observed. If no saver is configured, the start button is disabled. This window does not change Windows settings.

## Device selection and idle time

- Keyboard and mouse interfaces are enabled by default. Uncheck an interface to exclude its input from IdleGuard's timer; normal Windows input continues to work.
- The window displays independent idle seconds, the latest input and whether it was accepted, the last accepted device, and accepted/ignored packet counts. These are input events, not a count of user gestures.
- Relative mouse movement, changed absolute positions, button changes, nonzero wheel movement, and keyboard press/repeat/release events count. Empty mouse packets and unattributable sources do not reset idle.
- Raw Input runs through a separate hidden window even while the main window is minimized. Lock/suspend pauses the timer; unlock/resume starts a fresh baseline.
- **Refresh devices** reloads the list without resetting idle. Unplugged interfaces remain marked offline. Selection is retained by device path during this run, including reconnection with the same path; it is not saved across app restarts. A changed USB port or connection mode may produce a different path. A physical device may expose several interfaces.
- **Start screensaver** remains a permanent manual test button. This stage does not start a saver automatically; Windows' own screensaver timer still works independently.

See [input validation](docs/input-validation.md) for checks and remaining hardware scenarios.

## Planned features

- Add game controller support and dead zones.
- Persist device selection and add settings.
- Set a custom timeout or use the timeout from Windows screensaver settings.
- Start the configured Windows screensaver once per idle cycle when IdleGuard's own timer reaches the timeout.

Device selection controls IdleGuard's timer. Screensaver dismissal and sign-in requirements remain governed by Windows and the screensaver. Windows' own screensaver timer operates independently and may trigger earlier; IdleGuard will not change it by default.

Features and usage instructions will be updated as development progresses.

See [architecture and implementation plan](docs/architecture.md) for planned files, timer behavior, and screensaver integration.
