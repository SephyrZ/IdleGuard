using System.Diagnostics;
using IdleGuard.Services.Windows;
using IdleGuard.Services.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI;
using Microsoft.UI.Windowing;

namespace IdleGuard;

public sealed partial class MainWindow : Window
{
    private readonly ScreenSaverService _screenSaver = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly CancellationTokenSource _lifetime = new();
    private bool _observing;
    private readonly InputMonitor _input;

    public MainWindow(InputMonitor input)
    {
        _input = input;
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBarArea);
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Standard;
        RootLayout.Loaded += (_, _) => UpdateTitleBar();
        RootLayout.ActualThemeChanged += (_, _) => UpdateTitleBar();
        RootLayout.SizeChanged += (_, _) => UpdateTitleBarInsets();
        Activated += (_, args) => WindowTitleText.Opacity = args.WindowActivationState == WindowActivationState.Deactivated ? 0.65 : 1;
        AppWindow.Resize(new Windows.Graphics.SizeInt32(920, 960));
        DeviceList.ItemsSource = _input.Catalog.Devices;
        _timer.Tick += OnTick;
        Closed += (_, _) => { _timer.Stop(); _lifetime.Cancel(); };
        RefreshConfiguration();
        RefreshInputDisplay();
        _timer.Start();
    }

    private void UpdateTitleBar()
    {
        var dark = RootLayout.ActualTheme == ElementTheme.Dark;
        var titleBar = AppWindow.TitleBar;
        titleBar.ButtonBackgroundColor = Colors.Transparent;
        titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
        titleBar.ButtonForegroundColor = dark ? Colors.White : Colors.Black;
        titleBar.ButtonInactiveForegroundColor = dark ? Colors.Gray : Colors.DimGray;
        titleBar.ButtonHoverForegroundColor = dark ? Colors.White : Colors.Black;
        titleBar.ButtonPressedForegroundColor = dark ? Colors.White : Colors.Black;
        titleBar.ButtonHoverBackgroundColor = Windows.UI.Color.FromArgb(24, dark ? (byte)255 : (byte)0,
            dark ? (byte)255 : (byte)0, dark ? (byte)255 : (byte)0);
        titleBar.ButtonPressedBackgroundColor = Windows.UI.Color.FromArgb(16, dark ? (byte)255 : (byte)0,
            dark ? (byte)255 : (byte)0, dark ? (byte)255 : (byte)0);
        UpdateTitleBarInsets();
    }

    private void UpdateTitleBarInsets()
    {
        var scale = RootLayout.XamlRoot?.RasterizationScale ?? 1;
        TitleBarArea.Padding = new Thickness(AppWindow.TitleBar.LeftInset / scale + 16, 0,
            AppWindow.TitleBar.RightInset / scale + 16, 0);
    }

    private void RefreshDevices_Click(object sender, RoutedEventArgs e)
    {
        _input.RefreshDevices();
        RefreshInputDisplay();
    }

    private void RefreshInputDisplay()
    {
        IdleText.Text = $"空闲 {_input.Tracker.Idle.TotalSeconds:F1} 秒";
        MonitorText.Text = _input.Status;
        ActivityText.Text = $"最近输入：{_input.LastActivity}";
        LastAcceptedText.Text = $"最近接受的设备：{_input.LastAccepted}";
        CountsText.Text = $"已接受 {_input.AcceptedCount} 次 · 已忽略 {_input.IgnoredCount} 次";
        EmptyDevicesText.Visibility = _input.Catalog.Devices.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => RefreshConfiguration();

    private void RefreshConfiguration()
    {
        try
        {
            var configuration = _screenSaver.ReadConfiguration();
            ScreenSaverText.Text = configuration.Path ?? "未配置";
            TimeoutText.Text = $"{configuration.TimeoutSeconds} 秒";
            AutomaticText.Text = configuration.AutomaticEnabled ? "已启用" : "已禁用";
            var running = _screenSaver.IsRunning();
            RunningText.Text = running ? "运行中" : "未运行";
            StartButton.IsEnabled = !_observing && !running && configuration.Path is not null;
            if (!_timer.IsEnabled) _timer.Start();
        }
        catch (Exception ex)
        {
            ScreenSaverText.Text = "读取失败";
            TimeoutText.Text = AutomaticText.Text = "未知";
            StartButton.IsEnabled = false;
            ResultText.Text = ex.Message;
        }
    }

    private async void Start_Click(object sender, RoutedEventArgs e)
    {
        if (_observing) return;
        try
        {
            if (_screenSaver.IsRunning())
            {
                ResultText.Text = "屏保已经运行，没有重复请求。";
                return;
            }
            _screenSaver.RequestStart(WinRT.Interop.WindowNative.GetWindowHandle(this));
            _observing = true;
            StartButton.IsEnabled = false;
            ResultText.Text = $"{DateTime.Now:T}：启动请求已发出，正在检测（最多 10 秒）…";
            // DefWindowProc can wait for the saver to exit. Observe off the UI thread
            // so a short-lived running state is still recorded while it is covered.
            var result = await Task.Run(async () =>
            {
                var elapsed = Stopwatch.StartNew();
                do
                {
                    _lifetime.Token.ThrowIfCancellationRequested();
                    if (_screenSaver.IsRunning())
                        return $"{DateTime.Now:T}：启动请求后检测到屏保运行。";
                    await Task.Delay(250, _lifetime.Token);
                } while (elapsed.Elapsed < TimeSpan.FromSeconds(10));
                return $"{DateTime.Now:T}：请求已发出，但 10 秒内未检测到屏保运行。可能未启动，或已被输入操作退出。";
            }, _lifetime.Token);
            if (!_lifetime.IsCancellationRequested) ResultText.Text = result;
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception ex)
        {
            ResultText.Text = ex.Message;
        }
        finally
        {
            _observing = false;
            if (!_lifetime.IsCancellationRequested) RefreshConfiguration();
        }
    }

    private void OnTick(object? sender, object e)
    {
        RefreshInputDisplay();
        try
        {
            var running = _screenSaver.IsRunning();
            RunningText.Text = running ? "运行中" : "未运行";
            if (!_observing && running) StartButton.IsEnabled = false;
            if (!_observing && !running && !StartButton.IsEnabled) RefreshConfiguration();
        }
        catch (Exception ex)
        {
            RunningText.Text = "读取失败";
            StartButton.IsEnabled = false;
            ResultText.Text = $"屏保运行状态读取失败：{ex.Message}。请点击刷新信息重试。";
        }
    }
}
