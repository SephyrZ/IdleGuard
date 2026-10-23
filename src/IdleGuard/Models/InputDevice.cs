using System.ComponentModel;
using System.Runtime.CompilerServices;
using IdleGuard.Services;

namespace IdleGuard.Models;

public enum InputDeviceKind { Mouse, Keyboard, Controller, Hid }

public sealed partial class InputDevice : INotifyPropertyChanged
{
    private bool _enabled = true;
    private bool _connected;
    private bool _showId;

    public required string Path { get; init; }
    public string? HardwareName { get; init; }
    public int Number { get; init; }
    private string? _name;
    public string Name
    {
        get => _name ?? LocalizationService.Current.Format("DeviceName", KindLabel, Number,
            HardwareName ?? LocalizationService.Current["GenericDevice"]);
        init => _name = value;
    }
    public required InputDeviceKind Kind { get; init; }
    public string IconGlyph => Kind switch
    {
        InputDeviceKind.Keyboard => "\uE92E",
        InputDeviceKind.Mouse => "\uE962",
        InputDeviceKind.Controller => "\uE7FC",
        _ => "\uE772"
    };
    public ushort UsagePage { get; init; }
    public ushort Usage { get; init; }
    public bool CanMonitor { get; init; } = true;
    public string KindLabel => LocalizationService.Current[Kind.ToString()];
    public string PickerLabel => $"{Name} · {LocalizationService.Current[CanMonitor ? "SupportedInput" : "UnsupportedInput"]}";
    public string Description => $"{KindLabel} · {LocalizationService.Current[IsConnected ? "Connected" : "Offline"]}";
    public string DisplayName => $"{Name} · {LocalizationService.Current[IsConnected ? "Connected" : "Offline"]}";
    public bool ShowId
    {
        get => _showId;
        set { if (_showId != value) { _showId = value; Notify(); } }
    }
    public void RefreshLanguage() { Notify(nameof(Name)); Notify(nameof(DisplayName)); Notify(nameof(KindLabel)); Notify(nameof(Description)); }
    public bool IsEnabled
    {
        get => _enabled;
        set { if (_enabled != value) { _enabled = value; Notify(); } }
    }
    public bool IsConnected
    {
        get => _connected;
        internal set
        {
            if (_connected == value) return;
            _connected = value;
            Notify();
            Notify(nameof(Description));
            Notify(nameof(DisplayName));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Notify([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}
