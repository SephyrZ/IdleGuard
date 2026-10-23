# IdleGuard

Requires **Windows 10 2004 or later, x64**. Run `IdleGuard.exe` in the parent directory. Runtimes are included; the first launch may take longer while they are extracted.

1. Set a timeout on the screensaver page and enable monitoring.
2. Choose which devices reset the timer under **Activity & devices**. Use **Test screensaver** for a manual test.
3. Closing the window keeps monitoring in the tray; use its menu to exit.

First launch requests administrator permissions. You can disable this in Settings for future launches. Existing opt-outs are preserved; cancelling approval opens the app with standard permissions.

Automatic triggering checks for display wake requests and defers while they are active. This requires administrator permissions; errors pause triggering. Windows' own screensaver timer is independent. Selecting None in the Windows screensaver controls disables its saver.

Settings includes language and sign-in startup. Preferences are saved in `%LOCALAPPDATA%\IdleGuard\settings.json`. After moving the program, toggle startup off and on to update its path.

Prototype: some controllers may be unsupported; controller coverage and sign-in elevation still need validation.
