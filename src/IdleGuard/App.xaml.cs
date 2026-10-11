using Microsoft.UI.Xaml;
using IdleGuard.Services.Input;
using IdleGuard.Native;

namespace IdleGuard;

public partial class App : Application
{
    private Window? _window;
    private InputMonitor? _input;
    private Mutex? _instance;

    public App() => InitializeComponent();

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _instance = new Mutex(true, @"Local\IdleGuard.0F59225B", out var first);
        if (!first)
        {
            NativeMethods.PostMessage(0xffff, TrayNativeMethods.ShowWindowMessage, 0, 0);
            _instance.Dispose();
            _instance = null;
            Exit();
            return;
        }
        _input = new InputMonitor();
        var window = new MainWindow(_input);
        _window = window;
        _window.Closed += (_, _) =>
        {
            _input.Dispose();
            _instance.ReleaseMutex();
            _instance.Dispose();
            Exit();
        };
        if (!Environment.GetCommandLineArgs().Contains("--background", StringComparer.OrdinalIgnoreCase) || !window.CanHideToTray)
            window.ShowMainWindow();
    }
}
