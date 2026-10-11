using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace IdleGuard.Models;

public enum InputDeviceKind { Mouse, Keyboard }

public sealed class InputDevice : INotifyPropertyChanged
{
    private bool _enabled = true;
    private bool _connected;

    public required string Path { get; init; }
    public required string Name { get; init; }
    public required InputDeviceKind Kind { get; init; }
    public string KindLabel => Kind == InputDeviceKind.Keyboard ? "键盘" : "鼠标";
    public string Description => $"{KindLabel} · {(IsConnected ? "已连接" : "离线")}";
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
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Notify([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}
