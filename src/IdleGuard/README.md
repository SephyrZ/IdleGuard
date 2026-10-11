# Screensaver validation app

This directory contains an unpackaged C# / WinUI 3 x64 application targeting .NET 10. MainWindow displays keyboard/mouse interfaces, per-interface selection, independent idle time, recent input, and screensaver configuration. The manual Start screensaver button is permanent. InputMonitor is owned by App; its hidden native window receives Raw Input without suppressing normal keyboard or mouse input. ScreenSaverService contains the screensaver integration. Controller support, persisted settings, and the automatic idle controller are not implemented yet.

See the root [README](../../README.md) for build/run instructions and [architecture](../../docs/architecture.md) for planned files and responsibilities. Empty directories are tracked with `.gitkeep` files until implementation starts.
