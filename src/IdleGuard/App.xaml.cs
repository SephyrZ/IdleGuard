using Microsoft.UI.Xaml;
using IdleGuard.Services.Input;

namespace IdleGuard;

public partial class App : Application
{
    private Window? _window;
    private InputMonitor? _input;

    public App() => InitializeComponent();

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _input = new InputMonitor();
        _window = new MainWindow(_input);
        _window.Closed += (_, _) => _input.Dispose();
        _window.Activate();
    }
}
