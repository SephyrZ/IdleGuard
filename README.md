# IdleGuard

**English** | [简体中文](README.zh-CN.md)

A Windows utility in development that tracks its own idle time using selected input devices and starts the configured Windows screensaver when the timeout is reached. The planned desktop app uses C# and WinUI 3.

## Current status

The project is at the prototype stage. A complete desktop app is not yet available.

It includes a WinUI 3 app with sidebar navigation, keyboard/mouse selection, independent idle time, manual testing, automatic screensaver start, a system tray icon, and sign-in startup. English and Simplified Chinese can be switched live in Settings. [RawIdleTest.ps1](prototypes/RawIdleTest.ps1) remains available as a separate diagnostic script. Game controllers are not implemented yet.

## Run the validation window

For normal use, run `Release/IdleGuard.exe`. This is the compressed single-file release, with its own .NET and Windows App SDK runtimes. Only the executable and a `docs/` folder are exposed. It extracts runtime dependencies into the .NET cache on first launch, so compressed download size is smaller than total runtime disk usage.

Regenerate this local release with `./scripts/publish.ps1` in PowerShell. Exit the running release from its tray menu first. The script packages files locally; it does not upload or publish a GitHub release. `bin/` and `obj/` remain development build output, not the user-facing distribution.

Requirements: Windows 10 version 2004 or later (x64), .NET 10 SDK, and NuGet access for the first build. The Windows App SDK runtime is included in the build output; no MSIX installation is needed.

```powershell
dotnet build src/IdleGuard/IdleGuard.csproj -c Debug -p:Platform=x64
& ./src/IdleGuard/bin/x64/Debug/net10.0-windows10.0.19041.0/win-x64/IdleGuard.exe
```

The app opens in Chinese initially. Use **Settings → Display language** to switch to English immediately. Language, timeout and monitoring preferences are saved in `%LOCALAPPDATA%/IdleGuard/settings.json`. Monitoring is initially off; later launches restore the saved switch.

## Navigation and automatic start

- **Screensaver**: choose a custom duration in whole seconds (1–86,400), or read the Windows screensaver timeout. Click **Start monitoring** to begin a fresh idle cycle; **Pause monitoring** stops automatic requests. The page shows Windows screensaver information; idle time, remaining time and status stay in the fixed footer.
- **Activity & devices**: view live input, accepted/ignored events, and select keyboard/mouse interfaces.
- **About** and **Settings** stay at the bottom of the sidebar. Settings includes an immediate English / Simplified Chinese language switch and **Launch at startup**.
- The permanent **Test screensaver** button stays at the bottom right beside a clock showing seconds. Hover for testing guidance. Successful observation records the saver name and local date/time to the second. This is the first observed running time, not a guaranteed process creation timestamp. Windows configuration refreshes automatically.

Automatic start requests the configured saver once when IdleGuard's timer reaches the effective timeout. Accepted input opens a new cycle. Dismissal by an excluded device does not immediately relaunch the saver. Existing running savers are not launched again. An unconfirmed or failed start pauses automatic triggering until **Retry monitoring** is clicked. Changing the active timeout starts a fresh monitoring baseline. Reading the system timeout does not overwrite the saved custom duration or change Windows settings. System configuration is refreshed at least every five seconds while the app is running.

A request being sent does not prove the saver started: the app observes running state for up to ten seconds. If no saver is configured, the manual test button is disabled and automatic start shows an error.

## Device selection and idle time

- Keyboard and mouse interfaces are enabled by default. Uncheck an interface to exclude its input from IdleGuard's timer; normal Windows input continues to work.
- The fixed footer displays independent idle time and monitoring status on every page, with a clock and manual screensaver control on the right. The activity page shows the latest input, its device and acceptance result, and accepted/ignored packet counts. These are input events, not a count of user gestures.
- Each device occupies one row with its connection status beside its name. **Show device interface paths** reveals its Windows interface path on a second row; paths are hidden by default.
- Relative mouse movement, changed absolute positions, button changes, nonzero wheel movement, and keyboard press/repeat/release events count. Empty mouse packets and unattributable sources do not reset idle.
- Raw Input runs through a separate hidden window even while the main window is minimized. Lock/suspend pauses the timer; unlock/resume starts a fresh baseline.
- **Refresh devices** reloads the list without resetting idle. Unplugged interfaces remain marked offline. Selection is retained by device path during this run, including reconnection with the same path; it is not saved across app restarts. A changed USB port or connection mode may produce a different path. A physical device may expose several interfaces.
- **Test screensaver** remains a permanent manual test button. Windows' own screensaver timer still works independently.

See [input validation](docs/input-validation.md) for checks and remaining hardware scenarios.

## Background and startup

Closing the window hides it to the notification area and keeps monitoring alive. Left-click the IdleGuard tray icon to reopen the window. Right-click for **Launch at startup**, **Monitor screensaver**, and **Exit**. The switches share state with Settings and the main page. Launching the app again restores the existing window instead of creating another instance. **Exit** stops monitoring and removes the tray icon.

Startup uses the current user's Run registry entry and launches the current executable with `--background` at Windows sign-in. Moving the executable requires turning startup off and on from the new location. Startup is off until enabled. Windows can delay startup or disable it in Task Manager; sign-in behavior has not been tested by restarting Windows in this session.

## Planned features

- Add game controller support and dead zones.
- Persist device selection.

Device selection controls IdleGuard's timer. Screensaver dismissal and sign-in requirements remain governed by Windows and the screensaver. Windows' own screensaver timer operates independently and may trigger earlier; IdleGuard will not change it by default.

Features and usage instructions will be updated as development progresses.

See [architecture and implementation plan](docs/architecture.md) for planned files, timer behavior, and screensaver integration.
