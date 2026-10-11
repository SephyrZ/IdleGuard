using System.ComponentModel;
using System.Runtime.InteropServices;

namespace IdleGuard.Services.Windows;

/// <summary>Owns the native icon handles while the window and tray use them.</summary>
internal sealed class AppIcons : IDisposable
{
    internal nint Small { get; private set; }
    internal nint Large { get; private set; }

    internal AppIcons(nint window)
    {
        var dpi = Native.NativeMethods.GetDpiForWindow(window);
        Small = Load(32, Math.Max(16, (int)(16 * dpi / 96)));
        try { Large = Load(48, Math.Max(32, (int)(32 * dpi / 96))); }
        catch { Dispose(); throw; }
        SendMessage(window, 0x80, 0, Small); // WM_SETICON / ICON_SMALL
        SendMessage(window, 0x80, 1, Large); // WM_SETICON / ICON_BIG
    }

    private static nint Load(int frameSize, int outputSize)
    {
        using var stream = typeof(AppIcons).Assembly.GetManifestResourceStream("IdleGuard.Assets.AppIcon.ico")!;
        using var reader = new BinaryReader(stream);
        reader.ReadUInt16(); reader.ReadUInt16();
        var count = reader.ReadUInt16();
        for (var index = 0; index < count; index++)
        {
            var width = reader.ReadByte();
            reader.ReadBytes(7);
            var length = reader.ReadUInt32();
            var offset = reader.ReadUInt32();
            if (width != frameSize) continue;
            stream.Position = offset;
            var bytes = reader.ReadBytes(checked((int)length));
            var icon = CreateIconFromResourceEx(bytes, (uint)bytes.Length, true, 0x00030000, outputSize, outputSize, 0);
            if (icon == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
            return icon;
        }
        throw new InvalidDataException($"Missing {frameSize}px application icon.");
    }

    public void Dispose()
    {
        if (Small != 0) { DestroyIcon(Small); Small = 0; }
        if (Large != 0) { DestroyIcon(Large); Large = 0; }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint CreateIconFromResourceEx(byte[] bits, uint size, [MarshalAs(UnmanagedType.Bool)] bool icon, uint version, int width, int height, uint flags);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DestroyIcon(nint icon);
    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern nint SendMessage(nint window, uint message, nuint parameter, nint value);
}
