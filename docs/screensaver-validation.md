# Screensaver validation

The first implementation added manual screensaver testing. The current window also includes keyboard/mouse selection and independent idle time; see [input validation](input-validation.md). Automatic timeout triggering is not implemented. The manual Start screensaver button remains a permanent user testing feature.

## Observed on the development machine

- Debug x64 build: succeeded with zero warnings and zero errors.
- The unpackaged executable opened successfully.
- Configuration displayed `Mystify.scr`, 720 seconds, automatic screensaver enabled, and initially not running.
- Clicking Start screensaver changed the running state to running and retained a timestamped observation result.
- Dismissing the saver restored the not-running state and re-enabled Start; the observation result remained visible.

This confirms the configured saver on this machine. Other savers, no-saver configurations, Windows policy overrides, locked sessions, and read/request failures still need manual coverage. Sampling every 250 ms can miss very short runs; a ten-second observation timeout means unconfirmed, not a definitive launch failure.

## Implementation references

- [SystemParametersInfoW](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-systemparametersinfow): reads timeout, automatic activation, and running state.
- [WM_SYSCOMMAND](https://learn.microsoft.com/en-us/windows/win32/menurc/wm-syscommand): `SC_SCREENSAVE` requests the configured saver through the window's default command processing.
- [Self-contained Windows App SDK deployment](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/self-contained-deploy/deploy-self-contained-apps): includes the Windows App SDK runtime beside the executable.
