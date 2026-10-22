# IdleGuard

Run `IdleGuard.exe` in the parent directory. No separate .NET or Windows App SDK installation is required.

Requires Windows 10 version 2004 or later (x64). The first launch extracts bundled dependencies to the .NET cache and may take longer. Compression reduces distribution size; extracted runtime files still consume disk space.

## Layout

- `IdleGuard.exe`: application entry point and compressed runtime dependencies.
- `docs/`: English and Chinese instructions.

## Usage

Set a timeout on the Screensaver page and start monitoring. The footer shows idle time, current time, and the last observed screensaver name and timestamp.

Automatic startup checks `powercfg /requests` once at the idle deadline. An active `DISPLAY` request restarts the full IdleGuard timer; there is no periodic powercfg polling. Run IdleGuard as administrator for this query. Query errors pause automatic triggering until you retry monitoring. Manual screensaver testing bypasses the check. Other power request types alone do not defer startup, and this check cannot detect every screensaver-blocking technique.

Hover over the app or driver link in the deferral message to see sources from the last check. Click to copy them and briefly show a confirmation. Multiple sources are listed on separate lines; driver names are preserved when no path is supplied.

The Windows screensaver card edits the installed saver and system timeout in seconds. Changes are saved in order in the background while the UI stays responsive. The row's icon opens Windows settings. Applied external changes are refreshed every five seconds and when returning to IdleGuard, after pending writes finish. Selecting None disables the Windows saver. These settings are separate from IdleGuard's own timer. The minimum window size is 660 x 660 logical pixels.

Keyboards, mice and supported controllers are included automatically. Choose which inputs reset idle on the device page. Use **Add device** beside **Refresh devices** to search other Windows input interfaces by name, type or path. Unsupported interfaces cannot be added. Device choices are saved across restarts. Windows interface paths are hidden until the switch above the list is enabled.

Closing the window keeps IdleGuard running in the notification area. Left-click its tray icon to reopen; right-click to toggle startup, toggle monitoring, or exit. Settings offers language switching, sign-in startup and an administrator launch preference. Enabling the administrator preference may prompt for Windows approval at launch, including sign-in startup. Use the restart button to apply it now. Turning it off takes effect on the next normal launch; Windows compatibility settings take precedence.

Preferences are stored in `%LOCALAPPDATA%\IdleGuard\settings.json`, outside the program directory. If you move the executable, turn startup off and back on to register the new path.

This is a prototype. Controller input uses XInput or standard HID buttons and axes, with analog filtering. XInput slots are connection slots, not stable controller identities. Vendor-specific HID inputs may be unsupported. Actual controller hardware coverage and sign-in elevation still need validation.
