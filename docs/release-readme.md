# IdleGuard

Run `IdleGuard.exe` in the parent directory. No separate .NET or Windows App SDK installation is required.

Requires Windows 10 version 2004 or later (x64). The first launch extracts bundled dependencies to the .NET cache and may take longer. Compression reduces distribution size; extracted runtime files still consume disk space.

## Layout

- `IdleGuard.exe`: application entry point and compressed runtime dependencies.
- `docs/`: English and Chinese instructions.

## Usage

Set a timeout on the Screensaver page and start monitoring. The footer shows idle time, current time, and the last observed screensaver name and timestamp.

Choose which keyboard/mouse input resets idle on the device page. Windows device interface paths are hidden until the switch above the list is enabled.

Closing the window keeps IdleGuard running in the notification area. Left-click its tray icon to reopen; right-click to toggle startup, toggle monitoring, or exit. Settings offers language switching and sign-in startup.

Preferences are stored in `%LOCALAPPDATA%\IdleGuard\settings.json`, outside the program directory. If you move the executable, turn startup off and back on to register the new path.

This is a prototype. Keyboard and mouse are supported; game controllers and device selection persistence across app restarts are planned.
