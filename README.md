# IdleGuard

**English** | [简体中文](README.zh-CN.md)

A C# / WinUI 3 utility that starts the Windows screensaver after selected input devices have been idle for a chosen duration.

## Use

Requires **Windows 10 2004 or later, x64**. Run `Release/IdleGuard.exe`; .NET and WinUI runtimes are included.

1. Choose a timeout on the screensaver page and enable monitoring.
2. Select which keyboards, mice and supported controllers reset the timer under **Activity & devices**.
3. Close the window to keep running in the tray. Use the tray menu to exit.

First launch requests administrator permissions. Disable this in Settings if preferred; an existing saved opt-out is preserved. Settings also includes English / Chinese and sign-in startup options.

Automatic triggering defers while an app or driver requests the display stay on. This check requires administrator permissions; errors pause automatic triggering. Windows' own screensaver timer remains independent.

Preferences: `%LOCALAPPDATA%\IdleGuard\settings.json`. After moving the executable, toggle sign-in startup off and on to update its path.

## Build

Requires the **.NET 10 SDK** and NuGet access. Exit the running release before rebuilding.

```powershell
# Development build
dotnet build src/IdleGuard/IdleGuard.csproj -c Debug -p:Platform=x64
# Local release in Release/
./scripts/publish.ps1
```

Successful publishing removes temporary build files and keeps reports in `artifacts/latest-publish`.

This is a prototype; controller hardware coverage and sign-in elevation still need validation. See [validation](docs/input-validation.md), [architecture](docs/architecture.md), and [publishing](docs/publish-optimization.md).
