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
using Microsoft.UI.Xaml.Documents;
using Windows.ApplicationModel.DataTransfer;

namespace IdleGuard;

public sealed partial class MainWindow : Window
{
    private readonly ScreenSaverService _screenSaver = new();
    private readonly PowerRequestService _powerRequests = new();
    private string _displayRequestSources = "";
    private readonly Flyout _copyPathFeedback = new() { Placement = FlyoutPlacementMode.Top };
    private readonly DispatcherTimer _copyPathFeedbackTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly StartupService _startup = new();
    private TrayIcon? _tray;
    private readonly AppIcons _icons;
    private readonly SettingsStore _store = new();
    private readonly LocalizationService _strings = LocalizationService.Current;
    private readonly IdleController _controller = new();
    private readonly InputMonitor _input;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly DispatcherTimer _controllerTimer = new() { Interval = TimeSpan.FromMilliseconds(32) };
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Stopwatch _configurationPoll = Stopwatch.StartNew();
    private AppSettings _settings = new();
    private ScreenSaverConfiguration? _configuration;
    private bool _ready, _requestInFlight, _screenReady, _running;
    private bool _exiting, _syncStartup, _startupEnabled;
    private readonly bool _isAdministrator = AdministratorService.IsAdministrator;
    private DateTimeOffset? _observedAt;
    private string _observedSaver = "";
    private string _resultKey = "NoOperation", _resultDetail = "", _controllerDetail = "";
    private string? _preferencesErrorKey, _preferencesErrorDetail, _configurationError;
    private string? _startupError;
    private bool _syncSystemSettings, _saverChoicesDirty = true;
    private IReadOnlyList<ScreenSaverChoice> _saverChoices = Array.Empty<ScreenSaverChoice>();
    private string? _systemSettingsError;
    private double _minimumWindowScale;
    private readonly BackgroundSettingsWriter _systemWrites = new();
    private int _pendingSystemWrites;
    private bool _exitRequested;

    public MainWindow(InputMonitor input)
    {
        _input = input;
        try { _settings = _store.Load(); }
        catch (Exception ex) { _preferencesErrorKey = "LoadFailed"; _preferencesErrorDetail = ex.Message; }
        _strings.SetLanguage(_settings.Language);
        InitializeComponent();
        _copyPathFeedbackTimer.Tick += (_, _) => { _copyPathFeedbackTimer.Stop(); _copyPathFeedback.Hide(); };
        _icons = new AppIcons(WinRT.Interop.WindowNative.GetWindowHandle(this));
        TitleIcon.Source = LoadAppIcon("small");
        AboutAppIcon.Source = LoadAppIcon("full");
        RootLayout.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(RootLayout_PointerPressed), true);
        RootLayout.DataContext = _strings;
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBarArea);
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Standard;
        RootLayout.Loaded += (_, _) => UpdateTitleBar();
        RootLayout.ActualThemeChanged += (_, _) => UpdateTitleBar();
        AppWindow.Changed += (_, _) => UpdateWindowFrame();
        RootLayout.SizeChanged += (_, _) => { UpdateTitleBarInsets(); UpdateMinimumWindowSize(); UpdateResponsiveNavigation(); };
        Activated += (_, args) =>
        {
            WindowTitleText.Opacity = args.WindowActivationState == WindowActivationState.Deactivated ? 0.65 : 1;
            if (_ready && args.WindowActivationState != WindowActivationState.Deactivated) { RefreshConfiguration(); UpdateDisplay(); }
        };
        var scale = NativeMethods.GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this)) / 96.0;
        UpdateMinimumWindowSize();
        var workArea = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        AppWindow.Resize(new Windows.Graphics.SizeInt32(
            Math.Min((int)(660 * scale), workArea.Width - 48), Math.Min((int)(660 * scale), workArea.Height - 48)));
        UpdateResponsiveNavigation();
        DeviceList.ItemsSource = _input.Catalog.Devices;
        RefreshTimeoutSourceLabels();
        _input.Catalog.ApplyRules(_settings.DeviceRules);
        _input.RefreshDevices();
        _input.Catalog.RulesChanged += DeviceRulesChanged;
        AdministratorToggle.IsOn = _settings.RunAsAdministrator;
        AdministratorErrorText.Text = AdministratorService.LaunchError is { } launchError ? _strings.Format("AdministratorError", launchError) : "";
        TimeoutBox.Value = _settings.CustomTimeoutSeconds;
        LanguageBox.SelectedIndex = _settings.Language == "en-US" ? 1 : 0;
        Navigation.SelectedItem = HomeNav;
        _timer.Tick += OnTick;
        _controllerTimer.Tick += (_, _) => _input.PollControllers();
        AppWindow.Closing += (_, args) =>
        {
            if (!_exiting && CanHideToTray) { args.Cancel = true; AppWindow.Hide(); }
        };
        Closed += (_, _) => { _timer.Stop(); _controllerTimer.Stop(); _copyPathFeedbackTimer.Stop(); _copyPathFeedback.Hide(); _input.Catalog.RulesChanged -= DeviceRulesChanged; _lifetime.Cancel(); _tray?.Dispose(); _icons.Dispose(); };
        _ready = true;
        RefreshStartup();
        try
        {
            _tray = new TrayIcon(WinRT.Interop.WindowNative.GetWindowHandle(this), _icons.Small, ShowMainWindow,
                () => SetStartup(!_startupEnabled), ToggleMonitoring, ExitApplication,
                () => { RefreshStartup(); return _startupEnabled; }, () => _controller.Enabled,
                ex => { TrayErrorText.Text = _strings.Format("TrayError", ex.Message); ShowMainWindow(); });
        }
        catch (Exception ex) { TrayErrorText.Text = _strings.Format("TrayError", ex.Message); }
        RefreshConfiguration();
        if (_settings.MonitoringEnabled) ToggleMonitoring();
        UpdateDisplay(force: true);
        _timer.Start();
        _controllerTimer.Start();
    }

    public bool CanHideToTray => _tray?.Available == true;

    private static Microsoft.UI.Xaml.Media.Imaging.BitmapImage LoadAppIcon(string variant)
    {
        using var stream = typeof(MainWindow).Assembly.GetManifestResourceStream($"IdleGuard.Assets.AppIcon.{variant}.png")!;
        using var randomAccess = stream.AsRandomAccessStream();
        var image = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage();
        image.SetSource(randomAccess);
        return image;
    }

    public void ShowMainWindow()
    {
        AppWindow.Show();
        var handle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        TrayNativeMethods.ShowWindow(handle, 9); // Restore a minimized window before activation.
        Activate();
        TrayNativeMethods.SetForegroundWindow(handle);
        UpdateDisplay();
    }

    private async void ExitApplication()
    {
        if (_exitRequested) return;
        _exitRequested = true;
        Navigation.IsEnabled = false;
        try { await _systemWrites.DrainAsync(); }
        catch { /* Individual write errors are already reported by the editor. */ }
        _exiting = true;
        Close();
    }

    private void RootLayout_PointerPressed(object sender, PointerRoutedEventArgs args)
    {
        // Blank surfaces do not normally take focus. Explicitly end numeric editing there.
        var focused = FocusManager.GetFocusedElement(RootLayout.XamlRoot) as DependencyObject;
        bool Inside(DependencyObject? element, DependencyObject target)
        {
            for (; element is not null; element = VisualTreeHelper.GetParent(element)) if (element == target) return true;
            return false;
        }
        if (!Inside(focused, TimeoutBox) && !Inside(focused, SystemTimeoutBox)) return;
        for (var source = args.OriginalSource as DependencyObject; source is not null; source = VisualTreeHelper.GetParent(source))
            if (source is NumberBox or TextBox or ButtonBase or ComboBox or ToggleSwitch or NavigationViewItem) return;
        Navigation.Focus(FocusState.Programmatic);
    }

    private void RefreshStartup()
    {
        try
        {
            _startupEnabled = _startup.IsEnabled();
            _startupError = null;
        }
        catch (Exception ex) { _startupError = ex.Message; }
    }

    private void SetStartup(bool enabled)
    {
        try { _startup.SetEnabled(enabled); RefreshStartup(); }
        catch (Exception ex)
        {
            _startupError = ex.Message;
            ShowMainWindow();
        }
        UpdateDisplay();
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
        UpdateWindowFrame();
        UpdateTitleBarInsets();
    }

    private void UpdateWindowFrame()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)) return;
        var window = WinRT.Interop.WindowNative.GetWindowHandle(this);
        uint dark = RootLayout.ActualTheme == ElementTheme.Dark ? 1u : 0u;
        // Keep the native frame consistent with XAML. Suppress the DWM border
        // only while maximized to avoid a bright strip on mixed-DPI displays.
        var border = AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Maximized }
            ? NativeMethods.DwmColorNone : NativeMethods.DwmColorDefault;
        // Cosmetic fallback: unsupported/failed attributes leave Windows defaults intact.
        _ = NativeMethods.DwmSetWindowAttribute(window, NativeMethods.DwmUseImmersiveDarkMode, in dark, sizeof(uint));
        _ = NativeMethods.DwmSetWindowAttribute(window, NativeMethods.DwmBorderColor, in border, sizeof(uint));
    }

    private void UpdateTitleBarInsets()
    {
        var scale = RootLayout.XamlRoot?.RasterizationScale ?? 1;
        TitleBarArea.Padding = new Thickness(AppWindow.TitleBar.LeftInset / scale + 16, 0, AppWindow.TitleBar.RightInset / scale + 16, 0);
    }

    private void UpdateMinimumWindowSize()
    {
        var scale = NativeMethods.GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this)) / 96.0;
        if (scale == _minimumWindowScale || AppWindow.Presenter is not OverlappedPresenter presenter) return;
        _minimumWindowScale = scale;
        presenter.PreferredMinimumWidth = (int)Math.Ceiling(660 * scale);
        presenter.PreferredMinimumHeight = (int)Math.Ceiling(660 * scale);
    }

    private void UpdateResponsiveNavigation()
    {
        var scale = NativeMethods.GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this)) / 96.0;
        var expanded = AppWindow.Size.Width / scale >= 900;
        Navigation.PaneDisplayMode = expanded ? NavigationViewPaneDisplayMode.Left : NavigationViewPaneDisplayMode.LeftCompact;
        Navigation.IsPaneOpen = expanded;
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
        RefreshTimeoutSourceLabels();
        foreach (var device in _input.Catalog.Devices) device.RefreshLanguage();
        _saverChoicesDirty = true;
        SavePreferences();
        UpdateDisplay();
    }

    private void SavePreferences()
    {
        _settings = _settings with { DeviceRules = _input.Catalog.ExportRules() };
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
    private void DeviceRulesChanged()
    {
        if (!_ready) return;
        SavePreferences();
    }

    private void RefreshTimeoutSourceLabels()
    {
        // ComboBox caches its closed selection presentation. Replace the string items to refresh it immediately.
        var ready = _ready;
        _ready = false;
        try
        {
            TimeoutSourceBox.ItemsSource = new[] { _strings["Custom"], _strings["System"] };
            TimeoutSourceBox.SelectedIndex = _settings.UseSystemTimeout ? 1 : 0;
        }
        finally { _ready = ready; }
    }

    private void Administrator_Toggled(object sender, RoutedEventArgs args)
    {
        if (!_ready) return;
        _settings = _settings with { RunAsAdministrator = AdministratorToggle.IsOn };
        SavePreferences();
        UpdateDisplay();
    }

    private void RestartAsAdministrator_Click(object sender, RoutedEventArgs args)
    {
        try { AdministratorService.StartElevated(false); ExitApplication(); }
        catch (Exception ex) { AdministratorErrorText.Text = _strings.Format("AdministratorError", ex.Message); UpdateDisplay(); }
    }

    private async void AddDevice_Click(object sender, RoutedEventArgs args)
    {
        _input.RefreshDevices();
        var search = new TextBox { PlaceholderText = _strings["SearchDevices"] };
        var list = new ListView { SelectionMode = ListViewSelectionMode.Single, Height = 300 };
        var hint = new TextBlock { Text = _strings["AddDeviceHelp"], TextWrapping = TextWrapping.Wrap };
        var empty = new TextBlock { Text = _strings["NoMatchingDevices"], TextWrapping = TextWrapping.Wrap };
        var panel = new StackPanel { Spacing = 12, MinWidth = 360, MaxWidth = 600 };
        panel.Children.Add(hint); panel.Children.Add(search); panel.Children.Add(list); panel.Children.Add(empty);
        var dialog = new ContentDialog { Title = _strings["AddDevice"], Content = panel, XamlRoot = RootLayout.XamlRoot,
            PrimaryButtonText = _strings["Add"], CloseButtonText = _strings["Cancel"], IsPrimaryButtonEnabled = false };
        Grid DeviceContent(InputDevice device)
        {
            var row = new Grid { ColumnSpacing = 8 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.Children.Add(new FontIcon { Glyph = device.IconGlyph, FontSize = 18, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 0, 0) });
            var details = new StackPanel
            {
                Spacing = 4, Children =
                {
                    new TextBlock { Text = device.PickerLabel, TextWrapping = TextWrapping.Wrap },
                    new TextBlock { Text = device.Path, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, Opacity = 0.65 }
                }
            };
            Grid.SetColumn(details, 1);
            row.Children.Add(details);
            return row;
        }
        void Filter()
        {
            var query = search.Text.Trim();
            var candidates = _input.Catalog.Available.Where(d => d.IsConnected && !_input.Catalog.Devices.Contains(d)
                && (query.Length == 0 || $"{d.Name} {d.Path} {d.KindLabel}".Contains(query, StringComparison.OrdinalIgnoreCase)))
                .OrderByDescending(d => d.CanMonitor).ThenBy(d => d.Name).ToArray();
            list.ItemsSource = candidates.Select(d => new ListViewItem { Padding = new Thickness(12, 7, 12, 7), HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Content = DeviceContent(d), Tag = d, IsEnabled = d.CanMonitor }).ToArray();
            dialog.IsPrimaryButtonEnabled = false;
            empty.Visibility = candidates.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        search.TextChanged += (_, _) => Filter();
        list.SelectionChanged += (_, _) => dialog.IsPrimaryButtonEnabled = (list.SelectedItem as ListViewItem)?.Tag is InputDevice { CanMonitor: true };
        Filter();
        if (await dialog.ShowAsync() == ContentDialogResult.Primary && (list.SelectedItem as ListViewItem)?.Tag is InputDevice selected)
        {
            if (_input.Catalog.Add(selected)) { _input.RefreshDevices(); SavePreferences(); }
            UpdateDisplay();
        }
    }
    private void ShowDeviceIds_Toggled(object sender, RoutedEventArgs args)
    {
        foreach (var device in _input.Catalog.Devices) device.ShowId = ShowDeviceIds.IsOn;
    }

    private void RefreshConfiguration()
    {
        if (_pendingSystemWrites > 0) return;
        _configurationPoll.Restart();
        RefreshStartup();
        try
        {
            var previousTimeout = _configuration?.TimeoutSeconds;
            _configuration = _screenSaver.ReadConfiguration();
            if (!_saverChoices.Any(choice => SameSaver(choice.Path, _configuration.Path))) _saverChoicesDirty = true;
            ObserveRunning(_screenSaver.IsRunning());
            _screenReady = true;
            _configurationError = null;
            if (_controller.Enabled && _configuration.Path is null) _controller.Fail("NoSaverError");
            if (_controller.Enabled && EffectiveTimeout is null) _controller.Fail("InvalidTimeout");
            if (_settings.UseSystemTimeout && previousTimeout is not null && previousTimeout != _configuration.TimeoutSeconds
                && _controller.Enabled && !_requestInFlight)
            {
                _input.Tracker.Restart();
                _controller.Enable(_input.Tracker.Cycle);
            }
        }
        catch (Exception ex)
        {
            _screenReady = false;
            _configurationError = ex.Message;
            if (_controller.Enabled) { _controllerDetail = ex.Message; _controller.Fail("ReadFailed"); }
        }
    }

    private string TranslateError(string key, string detail) => key is "ReadFailed" or "RequestFailed" or "InputError" or "PowerRequestCheckFailed"
        ? _strings.Format(key, detail) : _strings[key];

    private static bool SameSaver(string? first, string? second) => first is null || second is null
        ? first is null && second is null
        : string.Equals(ScreenSaverService.ResolvePath(first), ScreenSaverService.ResolvePath(second), StringComparison.OrdinalIgnoreCase);

    private bool IsEditing(DependencyObject target)
    {
        if (RootLayout.XamlRoot is null) return false;
        for (var focused = FocusManager.GetFocusedElement(RootLayout.XamlRoot) as DependencyObject;
            focused is not null; focused = VisualTreeHelper.GetParent(focused))
            if (focused == target) return true;
        return false;
    }

    private void SyncSystemSettings()
    {
        if (_pendingSystemWrites > 0) return;
        _syncSystemSettings = true;
        try
        {
            if (_saverChoicesDirty && !SystemSaverBox.IsDropDownOpen)
            {
                _saverChoices = _screenSaver.GetChoices(_strings["NoneSaver"], _configuration?.Path);
                SystemSaverBox.ItemsSource = _saverChoices;
                _saverChoicesDirty = false;
            }
            if (!SystemSaverBox.IsDropDownOpen)
                SystemSaverBox.SelectedItem = _saverChoices.FirstOrDefault(choice => SameSaver(choice.Path, _configuration?.Path));
            if (!IsEditing(SystemTimeoutBox)) SystemTimeoutBox.Value = _configuration?.TimeoutSeconds ?? double.NaN;
            var editable = _screenReady && _configuration?.IsManaged == false;
            SystemSaverBox.IsEnabled = SystemTimeoutBox.IsEnabled = editable;
            SystemManagedText.Visibility = _configuration?.IsManaged == true ? Visibility.Visible : Visibility.Collapsed;
            SystemSettingsErrorText.Text = _systemSettingsError ?? (_configurationError is null ? "" : _strings.Format("ReadFailed", _configurationError));
            SystemSettingsErrorText.Visibility = string.IsNullOrEmpty(SystemSettingsErrorText.Text) ? Visibility.Collapsed : Visibility.Visible;
        }
        finally { _syncSystemSettings = false; }
    }

    private async void ApplySystemSetting(Action change)
    {
        _pendingSystemWrites++;
        _systemSettingsError = null;
        try { await _systemWrites.Enqueue(change); }
        catch (Exception ex) { _systemSettingsError = _strings.Format("SystemSettingsError", ex.Message is "NoSaverError" or "SystemManaged" ? _strings[ex.Message] : ex.Message); }
        finally
        {
            _pendingSystemWrites--;
            if (!_lifetime.IsCancellationRequested) { RefreshConfiguration(); UpdateDisplay(); }
        }
    }

    private void SystemSaver_Opened(object sender, object args)
    {
        if (_pendingSystemWrites > 0) return;
        RefreshConfiguration();
        _syncSystemSettings = true;
        try
        {
            _saverChoices = _screenSaver.GetChoices(_strings["NoneSaver"], _configuration?.Path);
            SystemSaverBox.ItemsSource = _saverChoices;
            SystemSaverBox.SelectedItem = _saverChoices.FirstOrDefault(choice => SameSaver(choice.Path, _configuration?.Path));
            _saverChoicesDirty = false;
        }
        finally { _syncSystemSettings = false; }
    }

    private void SystemSaver_Changed(object sender, SelectionChangedEventArgs args)
    {
        if (_ready && !_syncSystemSettings && SystemSaverBox.SelectedItem is ScreenSaverChoice choice
            && (_pendingSystemWrites > 0 || !SameSaver(choice.Path, _configuration?.Path)))
        {
            var path = choice.Path;
            ApplySystemSetting(() => _screenSaver.SetScreenSaver(path));
        }
    }

    private void SystemTimeout_Changed(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (!_ready || _syncSystemSettings) return;
        if (!double.IsFinite(sender.Value) || sender.Value < 1 || sender.Value > int.MaxValue || sender.Value != Math.Truncate(sender.Value))
        {
            _systemSettingsError = _strings["InvalidSystemTimeout"];
            UpdateDisplay();
            return;
        }
        var seconds = (int)sender.Value;
        if (_pendingSystemWrites > 0 || seconds != _configuration?.TimeoutSeconds) ApplySystemSetting(() => _screenSaver.SetTimeout(seconds));
    }

    private void OpenScreenSaverSettings_Click(object sender, RoutedEventArgs args)
    {
        try { _screenSaver.OpenSettings(); }
        catch (Exception ex) { _systemSettingsError = _strings.Format("SystemSettingsError", ex.Message); UpdateDisplay(); }
    }

    private void UpdateDisplay(bool force = false)
    {
        // Monitoring keeps running while hidden; only presentation work is skipped.
        if (!force && (!AppWindow.IsVisible || TrayNativeMethods.IsIconic(WinRT.Interop.WindowNative.GetWindowHandle(this)))) return;
        _syncStartup = true;
        StartupToggle.IsOn = _startupEnabled;
        _syncStartup = false;
        StartupErrorText.Text = _startupError is null ? "" : _strings.Format("StartupError", _startupError);
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
        ActivityIcon.Glyph = activity?.Device?.IconGlyph ?? "\uE772";
        ActivityIcon.Visibility = activity is null ? Visibility.Collapsed : Visibility.Visible;
        ActivityText.Text = activity is null ? _strings["NoInput"] : $"{activity.Device?.Name ?? _strings["UnknownDevice"]} · {_strings[activity.Kind.ToString()]} · {_strings[activity.Accepted ? "Accepted" : "Ignored"]}";
        foreach (var device in _input.Catalog.Devices) device.ShowId = ShowDeviceIds.IsOn;
        CountsText.Text = _strings.Format("Counts", _input.AcceptedCount, _input.IgnoredCount);
        EmptyDevicesText.Visibility = _input.Catalog.Devices.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        SyncSystemSettings();
        StartButton.IsEnabled = _screenReady && _configuration?.Path is not null && !_running && !_requestInFlight && _pendingSystemWrites == 0;
        CurrentTimeText.Text = DateTime.Now.ToString("HH:mm:ss");
        ResultText.Text = _running ? _strings["SaverRunning"]
            : _resultKey == "StartObserved" && _observedAt is { } observedAt
            ? _strings.Format("ObservedAt", _observedSaver, observedAt.LocalDateTime)
            : _resultKey == "NoOperation" ? _strings["SaverNotRunning"] : TranslateError(_resultKey, _resultDetail);
        ToolTipService.SetToolTip(ResultText, ResultText.Text);
        var showRequestLink = !_running && _resultKey == "DisplayRequestDeferred" && _displayRequestSources.Length > 0;
        ResultText.Visibility = showRequestLink ? Visibility.Collapsed : Visibility.Visible;
        DeferredResultText.Visibility = showRequestLink ? Visibility.Visible : Visibility.Collapsed;
        DisplayRequestPrefix.Text = _strings["DisplayRequestPrefix"];
        DisplayRequestLinkLabel.Text = _strings["DisplayRequestSourceLink"];
        DisplayRequestSuffix.Text = _strings["DisplayRequestSuffix"];
        PreferencesErrorText.Text = _preferencesErrorKey is null ? "" : _strings.Format(_preferencesErrorKey, _preferencesErrorDetail ?? "");
        AdministratorStatusText.Text = _strings[_isAdministrator ? "AdministratorActive" : "AdministratorInactive"];
        AdministratorRestartButton.Visibility = !_isAdministrator && _settings.RunAsAdministrator ? Visibility.Visible : Visibility.Collapsed;
        foreach (var errorText in new[] { StartupErrorText, AdministratorErrorText, TrayErrorText, PreferencesErrorText })
            errorText.Visibility = string.IsNullOrEmpty(errorText.Text) ? Visibility.Collapsed : Visibility.Visible;
    }

    private async void Start_Click(object sender, RoutedEventArgs args) => await RequestScreenSaverAsync();

    private void DisplayRequestLink_Click(Hyperlink sender, HyperlinkClickEventArgs args)
    {
        if (string.IsNullOrEmpty(_displayRequestSources)) return;
        var message = _strings["RequestPathCopied"];
        try
        {
            var data = new DataPackage();
            data.SetText(_displayRequestSources);
            Clipboard.SetContent(data);
            Clipboard.Flush();
        }
        catch { message = _strings["RequestPathCopyFailed"]; }
        _copyPathFeedbackTimer.Stop();
        _copyPathFeedback.Content = new TextBlock { Text = message };
        _copyPathFeedback.ShowAt(DeferredResultText);
        _copyPathFeedbackTimer.Start();
    }

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

    private async Task RequestScreenSaverAsync(bool automatic = false)
    {
        if (_requestInFlight || _pendingSystemWrites > 0) return;
        _requestInFlight = true;
        var checkingPowerRequests = false;
        try
        {
            if (automatic)
            {
                var cycle = _input.Tracker.Cycle;
                checkingPowerRequests = true;
                var displayRequests = await Task.Run(() => _powerRequests.GetDisplayRequestsAsync(_lifetime.Token), _lifetime.Token);
                checkingPowerRequests = false;
                if (_lifetime.IsCancellationRequested) return;
                // Input, pause/resume, timeout edits or writes during the asynchronous
                // query invalidate this trigger. Never launch using the old idle snapshot.
                if (!_controller.Enabled || _controller.State != IdleControllerState.Triggering) return;
                if (_input.Tracker.Cycle != cycle || _input.Tracker.IsPaused || _pendingSystemWrites > 0)
                {
                    _controller.Enable(_input.Tracker.Cycle);
                    return;
                }
                if (displayRequests.Count > 0)
                {
                    _displayRequestSources = string.Join(Environment.NewLine, displayRequests);
                    ToolTipService.SetToolTip(DisplayRequestLink, new ToolTip
                    {
                        Content = new TextBlock { Text = _displayRequestSources, TextWrapping = TextWrapping.Wrap, MaxWidth = 560 }
                    });
                    _input.Tracker.Restart();
                    _controller.Enable(_input.Tracker.Cycle);
                    _resultKey = "DisplayRequestDeferred";
                    return;
                }
            }
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
            _resultKey = checkingPowerRequests ? "PowerRequestCheckFailed"
                : ex is InvalidOperationException ? "NoSaverError" : "RequestFailed";
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
        if (!_requestInFlight && _pendingSystemWrites == 0 && EffectiveTimeout is { } timeout && _controller.Evaluate(_input.Tracker.Idle,
                TimeSpan.FromSeconds(timeout), _input.Tracker.Cycle, _input.Tracker.IsPaused, _running))
            _ = RequestScreenSaverAsync(automatic: true);
        UpdateDisplay();
    }
}
