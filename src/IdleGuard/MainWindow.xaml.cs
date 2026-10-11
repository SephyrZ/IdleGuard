using System.Diagnostics;
using IdleGuard.Models;
using IdleGuard.Native;
using IdleGuard.Services;
using IdleGuard.Services.Input;
using IdleGuard.Services.Windows;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace IdleGuard;

public sealed partial class MainWindow : Window
{
    private readonly ScreenSaverService _screenSaver = new();
    private readonly StartupService _startup = new();
    private TrayIcon? _tray;
    private readonly SettingsStore _store = new();
    private readonly LocalizationService _strings = LocalizationService.Current;
    private readonly IdleController _controller = new();
    private readonly InputMonitor _input;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Stopwatch _configurationPoll = Stopwatch.StartNew();
    private AppSettings _settings = new();
    private ScreenSaverConfiguration? _configuration;
    private bool _ready, _requestInFlight, _screenReady, _running;
    private bool _exiting, _syncStartup, _startupEnabled;
    private DateTimeOffset? _observedAt;
    private string _observedSaver = "";
    private string _resultKey = "NoOperation", _resultDetail = "", _controllerDetail = "";
    private string? _preferencesErrorKey, _preferencesErrorDetail, _configurationError;

    public MainWindow(InputMonitor input)
    {
        _input = input;
        try { _settings = _store.Load(); }
        catch (Exception ex) { _preferencesErrorKey = "LoadFailed"; _preferencesErrorDetail = ex.Message; }
        _strings.SetLanguage(_settings.Language);
        InitializeComponent();
        RootLayout.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(RootLayout_PointerPressed), true);
        RootLayout.DataContext = _strings;
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBarArea);
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Standard;
        RootLayout.Loaded += (_, _) => UpdateTitleBar();
        RootLayout.ActualThemeChanged += (_, _) => UpdateTitleBar();
        RootLayout.SizeChanged += (_, _) => UpdateTitleBarInsets();
        Activated += (_, args) => WindowTitleText.Opacity = args.WindowActivationState == WindowActivationState.Deactivated ? 0.65 : 1;
        var scale = NativeMethods.GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this)) / 96.0;
        var workArea = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        AppWindow.Resize(new Windows.Graphics.SizeInt32(
            Math.Min((int)(1060 * scale), workArea.Width - 48), Math.Min((int)(820 * scale), workArea.Height - 48)));
        DeviceList.ItemsSource = _input.Catalog.Devices;
        TimeoutSourceBox.SelectedIndex = _settings.UseSystemTimeout ? 1 : 0;
        TimeoutBox.Value = _settings.CustomTimeoutSeconds;
        LanguageBox.SelectedIndex = _settings.Language == "en-US" ? 1 : 0;
        Navigation.SelectedItem = HomeNav;
        _timer.Tick += OnTick;
        AppWindow.Closing += (_, args) =>
        {
            if (!_exiting && CanHideToTray) { args.Cancel = true; AppWindow.Hide(); }
        };
        Closed += (_, _) => { _timer.Stop(); _lifetime.Cancel(); _tray?.Dispose(); };
        _ready = true;
        RefreshStartup();
        try
        {
            _tray = new TrayIcon(WinRT.Interop.WindowNative.GetWindowHandle(this), ShowMainWindow,
                () => SetStartup(!_startupEnabled), ToggleMonitoring, ExitApplication,
                () => { RefreshStartup(); return _startupEnabled; }, () => _controller.Enabled,
                ex => { TrayErrorText.Text = _strings.Format("TrayError", ex.Message); ShowMainWindow(); });
        }
        catch (Exception ex) { TrayErrorText.Text = _strings.Format("TrayError", ex.Message); }
        RefreshConfiguration();
        if (_settings.MonitoringEnabled) ToggleMonitoring();
        UpdateDisplay();
        _timer.Start();
    }

    public bool CanHideToTray => _tray?.Available == true;

    public void ShowMainWindow()
    {
        AppWindow.Show();
        var handle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        TrayNativeMethods.ShowWindow(handle, 9); // Restore a minimized window before activation.
        Activate();
        TrayNativeMethods.SetForegroundWindow(handle);
    }

    private void ExitApplication() { _exiting = true; Close(); }

    private void RootLayout_PointerPressed(object sender, PointerRoutedEventArgs args)
    {
        // Blank surfaces do not normally take focus. Explicitly end numeric editing there.
        var focused = FocusManager.GetFocusedElement(RootLayout.XamlRoot) as DependencyObject;
        bool Inside(DependencyObject? element, DependencyObject target)
        {
            for (; element is not null; element = VisualTreeHelper.GetParent(element)) if (element == target) return true;
            return false;
        }
        if (!Inside(focused, TimeoutBox)) return;
        for (var source = args.OriginalSource as DependencyObject; source is not null; source = VisualTreeHelper.GetParent(source))
            if (source is NumberBox or TextBox or ButtonBase or ComboBox or ToggleSwitch or NavigationViewItem) return;
        Navigation.Focus(FocusState.Programmatic);
    }

    private void RefreshStartup()
    {
        try
        {
            _startupEnabled = _startup.IsEnabled();
            _syncStartup = true;
            StartupToggle.IsOn = _startupEnabled;
            StartupErrorText.Text = "";
        }
        catch (Exception ex) { StartupErrorText.Text = _strings.Format("StartupError", ex.Message); }
        finally { _syncStartup = false; }
    }

    private void SetStartup(bool enabled)
    {
        try { _startup.SetEnabled(enabled); RefreshStartup(); }
        catch (Exception ex)
        {
            _syncStartup = true;
            StartupToggle.IsOn = _startupEnabled;
            _syncStartup = false;
            StartupErrorText.Text = _strings.Format("StartupError", ex.Message);
            ShowMainWindow();
        }
    }

    private void Startup_Toggled(object sender, RoutedEventArgs args)
    {
        if (_ready && !_syncStartup) SetStartup(StartupToggle.IsOn);
    }

    private void UpdateTitleBar()
    {
        var dark = RootLayout.ActualTheme == ElementTheme.Dark;
        var bar = AppWindow.TitleBar;
        bar.ButtonBackgroundColor = bar.ButtonInactiveBackgroundColor = Colors.Transparent;
        bar.ButtonForegroundColor = bar.ButtonHoverForegroundColor = bar.ButtonPressedForegroundColor = dark ? Colors.White : Colors.Black;
        bar.ButtonInactiveForegroundColor = dark ? Colors.Gray : Colors.DimGray;
        byte channel = dark ? (byte)255 : (byte)0;
        bar.ButtonHoverBackgroundColor = Windows.UI.Color.FromArgb(24, channel, channel, channel);
        bar.ButtonPressedBackgroundColor = Windows.UI.Color.FromArgb(16, channel, channel, channel);
        UpdateTitleBarInsets();
    }

    private void UpdateTitleBarInsets()
    {
        var scale = RootLayout.XamlRoot?.RasterizationScale ?? 1;
        TitleBarArea.Padding = new Thickness(AppWindow.TitleBar.LeftInset / scale + 16, 0, AppWindow.TitleBar.RightInset / scale + 16, 0);
    }

    private void Navigation_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (HomePage is null) return;
        var page = (args.SelectedItem as NavigationViewItem)?.Tag as string ?? "home";
        HomePage.Visibility = page == "home" ? Visibility.Visible : Visibility.Collapsed;
        DevicesPage.Visibility = page == "devices" ? Visibility.Visible : Visibility.Collapsed;
        SettingsPage.Visibility = page == "settings" ? Visibility.Visible : Visibility.Collapsed;
        AboutPage.Visibility = page == "about" ? Visibility.Visible : Visibility.Collapsed;
    }

    private bool CustomTimeoutValid => double.IsFinite(TimeoutBox.Value) && TimeoutBox.Value is >= 1 and <= 86400 && TimeoutBox.Value == Math.Truncate(TimeoutBox.Value);
    private int? EffectiveTimeout => _settings.UseSystemTimeout
        ? _screenReady && _configuration is { TimeoutSeconds: > 0 } ? _configuration.TimeoutSeconds : null
        : CustomTimeoutValid ? (int)TimeoutBox.Value : null;

    private void TimeoutSource_Changed(object sender, SelectionChangedEventArgs args)
    {
        if (!_ready) return;
        _settings = _settings with { UseSystemTimeout = TimeoutSourceBox.SelectedIndex == 1 };
        RefreshConfiguration();
        TimingChanged();
    }

    private void Timeout_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (!_ready) return;
        if (CustomTimeoutValid) _settings = _settings with { CustomTimeoutSeconds = (int)TimeoutBox.Value };
        TimingChanged();
    }

    private void TimingChanged()
    {
        if (_controller.Enabled)
        {
            if (EffectiveTimeout is null) _controller.Fail("InvalidTimeout");
            else if (!_requestInFlight) { _input.Tracker.Restart(); _controller.Enable(_input.Tracker.Cycle); }
        }
        SavePreferences();
        UpdateDisplay();
    }

    private void Language_Changed(object sender, SelectionChangedEventArgs args)
    {
        if (!_ready) return;
        _settings = _settings with { Language = LanguageBox.SelectedIndex == 1 ? "en-US" : "zh-CN" };
        _strings.SetLanguage(_settings.Language);
        foreach (var device in _input.Catalog.Devices) device.RefreshLanguage();
        SavePreferences();
        UpdateDisplay();
    }

    private void SavePreferences()
    {
        try { _store.Save(_settings); _preferencesErrorKey = _preferencesErrorDetail = null; }
        catch (Exception ex) { _preferencesErrorKey = "SaveFailed"; _preferencesErrorDetail = ex.Message; }
    }

    private void Monitoring_Click(object sender, RoutedEventArgs args) => ToggleMonitoring();

    private void ToggleMonitoring()
    {
        if (_controller.Enabled) _controller.Disable();
        else
        {
            RefreshConfiguration();
            if (!_screenReady) { _controllerDetail = _configurationError ?? ""; _controller.Fail("ReadFailed"); }
            else if (_configuration?.Path is null) _controller.Fail("NoSaverError");
            else if (EffectiveTimeout is null) _controller.Fail("InvalidTimeout");
            else if (_input.Error is not null) { _controllerDetail = _input.Error; _controller.Fail("InputError"); }
            else { _input.Tracker.Restart(); _controller.Enable(_input.Tracker.Cycle); }
        }
        _settings = _settings with { MonitoringEnabled = _controller.Enabled };
        SavePreferences();
        UpdateDisplay();
    }

    private void RefreshDevices_Click(object sender, RoutedEventArgs args) { _input.RefreshDevices(); UpdateDisplay(); }
    private void ShowDeviceIds_Toggled(object sender, RoutedEventArgs args)
    {
        foreach (var device in _input.Catalog.Devices) device.ShowId = ShowDeviceIds.IsOn;
    }

    private void RefreshConfiguration()
    {
        _configurationPoll.Restart();
        RefreshStartup();
        try
        {
            _configuration = _screenSaver.ReadConfiguration();
            ObserveRunning(_screenSaver.IsRunning());
            _screenReady = true;
            _configurationError = null;
            if (_controller.Enabled && _configuration.Path is null) _controller.Fail("NoSaverError");
            if (_controller.Enabled && EffectiveTimeout is null) _controller.Fail("InvalidTimeout");
        }
        catch (Exception ex)
        {
            _screenReady = false;
            _configurationError = ex.Message;
            if (_controller.Enabled) { _controllerDetail = ex.Message; _controller.Fail("ReadFailed"); }
        }
    }

    private string TranslateError(string key, string detail) => key is "ReadFailed" or "RequestFailed" or "InputError"
        ? _strings.Format(key, detail) : _strings[key];

    private void UpdateDisplay()
    {
        CustomTimeoutItem.Content = _strings["Custom"];
        SystemTimeoutItem.Content = _strings["System"];
        var idle = _input.Tracker.Idle.TotalSeconds;
        var timeout = EffectiveTimeout;
        IdleText.Text = _strings.Format("IdleSummary", idle);
        RemainingText.Text = _controller.State == IdleControllerState.Monitoring && timeout is not null && !_input.Tracker.IsPaused
            ? _strings.Format("RemainingSummary", Math.Max(0, timeout.Value - idle)) : "";
        CustomTimeoutRow.Visibility = _settings.UseSystemTimeout ? Visibility.Collapsed : Visibility.Visible;
        EffectiveTimeoutText.Text = timeout is not null ? _strings.Format("EffectiveTimeout", timeout.Value)
            : _settings.UseSystemTimeout ? _strings["Unknown"] : _strings["InvalidTimeout"];
        ControllerStatusText.Text = _controller.State switch
        {
            IdleControllerState.Disabled => _strings["Off"],
            IdleControllerState.Error => _strings.Format("ErrorState", TranslateError(_controller.Error!, _controllerDetail)),
            _ when _input.Tracker.IsPaused => _strings["SystemPaused"],
            IdleControllerState.Triggering => _strings["Triggering"],
            IdleControllerState.Triggered => _strings["Triggered"],
            _ => _strings["Monitoring"]
        };
        MonitoringButton.Content = _strings[_controller.Enabled ? "PauseMonitoring" : _controller.State == IdleControllerState.Error ? "RetryMonitoring" : "StartMonitoring"];
        MonitoringButton.IsEnabled = !_requestInFlight || _controller.Enabled;
        MonitorText.Text = _input.Error is not null ? _strings.Format("InputError", _input.Error) : _strings[_input.Tracker.IsPaused ? "InputPaused" : "InputMonitoring"];
        var activity = _input.LastActivity;
        ActivityText.Text = activity is null ? _strings["NoInput"] : $"{activity.Device?.Name ?? _strings["UnknownDevice"]} · {_strings[activity.Kind.ToString()]} · {_strings[activity.Accepted ? "Accepted" : "Ignored"]}";
        foreach (var device in _input.Catalog.Devices) device.ShowId = ShowDeviceIds.IsOn;
        CountsText.Text = _strings.Format("Counts", _input.AcceptedCount, _input.IgnoredCount);
        EmptyDevicesText.Visibility = _input.Catalog.Devices.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ScreenSaverText.Text = _configurationError is not null ? _strings.Format("ReadFailed", _configurationError) : _configuration?.Path ?? _strings["NotConfigured"];
        TimeoutText.Text = _screenReady ? _strings.Format("Seconds", _configuration!.TimeoutSeconds) : _strings["Unknown"];
        AutomaticText.Text = _screenReady ? _strings[_configuration!.AutomaticEnabled ? "Enabled" : "Disabled"] : _strings["Unknown"];
        RunningText.Text = _screenReady ? _strings[_running ? "Yes" : "No"] : _strings["Unknown"];
        StartButton.IsEnabled = _screenReady && _configuration?.Path is not null && !_running && !_requestInFlight;
        CurrentTimeText.Text = DateTime.Now.ToString("HH:mm:ss");
        ResultText.Text = _resultKey == "StartObserved" && _observedAt is { } observedAt
            ? _strings.Format("ObservedAt", _observedSaver, observedAt.LocalDateTime)
            : TranslateError(_resultKey, _resultDetail);
        ResultText.Visibility = _resultKey == "NoOperation" ? Visibility.Collapsed : Visibility.Visible;
        ToolTipService.SetToolTip(ResultText, ResultText.Text);
        PreferencesErrorText.Text = _preferencesErrorKey is null ? "" : _strings.Format(_preferencesErrorKey, _preferencesErrorDetail ?? "");
    }

    private async void Start_Click(object sender, RoutedEventArgs args) => await RequestScreenSaverAsync();

    private void RecordObserved(DateTimeOffset time)
    {
        _observedAt = time;
        _observedSaver = System.IO.Path.GetFileName(_configuration?.Path) ?? _strings["ScreenSaverTitle"];
        _resultKey = "StartObserved";
    }

    private void ObserveRunning(bool running)
    {
        if (running && !_running) RecordObserved(DateTimeOffset.Now);
        _running = running;
    }

    private async Task RequestScreenSaverAsync()
    {
        if (_requestInFlight) return;
        _requestInFlight = true;
        try
        {
            if (_screenSaver.IsRunning()) { ObserveRunning(true); _controller.CompleteRequest(true, ""); return; }
            var requestedAt = DateTimeOffset.Now;
            _screenSaver.RequestStart(WinRT.Interop.WindowNative.GetWindowHandle(this));
            _controller.BeginRequest(_input.Tracker.Cycle);
            _resultKey = "RequestSent";
            UpdateDisplay();
            // Observe independently: default window processing can wait for the saver.
            var observed = await Task.Run(async () =>
            {
                var elapsed = Stopwatch.StartNew();
                do
                {
                    _lifetime.Token.ThrowIfCancellationRequested();
                    if (_screenSaver.IsRunning()) return (DateTimeOffset?)DateTimeOffset.Now;
                    await Task.Delay(250, _lifetime.Token);
                } while (elapsed.Elapsed < TimeSpan.FromSeconds(10));
                return null;
            }, _lifetime.Token);
            if (_lifetime.IsCancellationRequested) return;
            _resultKey = observed.HasValue ? "StartObserved" : "StartUnconfirmed";
            if (observed is { } time && (_observedAt is null || _observedAt < requestedAt)) RecordObserved(time);
            _controller.CompleteRequest(observed.HasValue, "StartUnconfirmed");
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception ex)
        {
            _resultKey = ex is InvalidOperationException ? "NoSaverError" : "RequestFailed";
            _resultDetail = _controllerDetail = ex.Message;
            if (_controller.Enabled) _controller.Fail(_resultKey);
        }
        finally
        {
            _requestInFlight = false;
            if (!_lifetime.IsCancellationRequested) { RefreshConfiguration(); UpdateDisplay(); }
        }
    }

    private void OnTick(object? sender, object args)
    {
        if (_configurationPoll.Elapsed >= TimeSpan.FromSeconds(5) && !_requestInFlight) RefreshConfiguration();
        if (_screenReady)
        {
            try { ObserveRunning(_screenSaver.IsRunning()); }
            catch (Exception ex)
            {
                _screenReady = false;
                _configurationError = _controllerDetail = ex.Message;
                if (_controller.Enabled) _controller.Fail("ReadFailed");
            }
        }
        if (_controller.Enabled && _input.Error is not null) { _controllerDetail = _input.Error; _controller.Fail("InputError"); }
        if (!_requestInFlight && EffectiveTimeout is { } timeout && _controller.Evaluate(_input.Tracker.Idle,
                TimeSpan.FromSeconds(timeout), _input.Tracker.Cycle, _input.Tracker.IsPaused, _running))
            _ = RequestScreenSaverAsync();
        UpdateDisplay();
    }
}
