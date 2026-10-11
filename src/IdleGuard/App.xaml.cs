using Microsoft.UI.Xaml;
using IdleGuard.Services.Input;
using IdleGuard.Native;
using IdleGuard.Services;
using IdleGuard.Services.Windows;
using System.Diagnostics;

namespace IdleGuard;

public partial class App : Application
{
    private Window? _window;
    private InputMonitor? _input;
    private Mutex? _instance;

    public App() => InitializeComponent();

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        var arguments = Environment.GetCommandLineArgs();
        var waitIndex = Array.IndexOf(arguments, "--wait-for-exit");
        if (waitIndex >= 0 && waitIndex + 1 < arguments.Length && int.TryParse(arguments[waitIndex + 1], out var parentId)
            && parentId != Environment.ProcessId)
        {
            try
            {
                using var parent = Process.GetProcessById(parentId);
                if (!await Task.Run(() => parent.WaitForExit(15000))) { Exit(); return; }
            }
            catch (ArgumentException) { } // The original instance already exited.
            catch (Exception ex) { AdministratorService.LaunchError = ex.Message; }
        }
        _instance = new Mutex(true, @"Local\IdleGuard.0F59225B", out var first);
        if (!first)
        {
            NativeMethods.PostMessage(0xffff, TrayNativeMethods.ShowWindowMessage, 0, 0);
            _instance.Dispose();
            _instance = null;
            Exit();
            return;
        }
        try
        {
            if (new SettingsStore().Load().RunAsAdministrator && !AdministratorService.IsAdministrator)
            {
                AdministratorService.StartElevated(arguments.Contains("--background", StringComparer.OrdinalIgnoreCase));
                _instance.ReleaseMutex();
                _instance.Dispose();
                _instance = null;
                Exit();
                return;
            }
        }
        catch (Exception ex) { AdministratorService.LaunchError = ex.Message; }
        _input = new InputMonitor();
        var window = new MainWindow(_input);
        _window = window;
        _window.Closed += (_, _) =>
        {
            _input.Dispose();
            _instance!.ReleaseMutex();
            _instance.Dispose();
            Exit();
        };
        if (!arguments.Contains("--background", StringComparer.OrdinalIgnoreCase) || !window.CanHideToTray || AdministratorService.LaunchError is not null)
            window.ShowMainWindow();
    }
}
