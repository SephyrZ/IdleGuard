using System.Runtime.InteropServices;
using IdleGuard.Native;

namespace IdleGuard.Services.Input;

/// <summary>Only parse documented input controls; changing report bytes alone are not activity.</summary>
internal sealed class HidDeviceReader : IDisposable
{
    private nint _data;
    private readonly HidNativeMethods.ButtonCaps[] _buttons;
    private readonly HidNativeMethods.ValueCaps[] _values;
    private readonly ControlActivityFilter _filter = new();
    internal ushort UsagePage { get; }
    internal ushort Usage { get; }
    internal bool CanMonitor => _buttons.Length > 0 || _values.Length > 0;

    private HidDeviceReader(nint data, HidNativeMethods.Caps caps)
    {
        _data = data;
        UsagePage = caps.UsagePage;
        Usage = caps.Usage;
        ushort count = caps.InputButtonCaps;
        var buttons = new HidNativeMethods.ButtonCaps[count];
        if (count > 0 && HidNativeMethods.HidP_GetButtonCaps(0, buttons, ref count, data) != HidNativeMethods.Success)
            throw new InvalidDataException("Cannot read HID button capabilities.");
        // Standard keyboard, button, consumer and desktop controls. Vendor reports may contain telemetry.
        _buttons = buttons.Take(count).Where(c => c.UsagePage is 1 or 7 or 9 or 12)
            .DistinctBy(c => (c.ReportId, c.UsagePage, c.LinkCollection)).ToArray();
        count = caps.InputValueCaps;
        var values = new HidNativeMethods.ValueCaps[count];
        if (count > 0 && HidNativeMethods.HidP_GetValueCaps(0, values, ref count, data) != HidNativeMethods.Success)
            throw new InvalidDataException("Cannot read HID value capabilities.");
        _values = values.Take(count).Where(c => c.UsagePage == 1 && (c.IsRange != 0 || c.ReportCount == 1) && c.BitSize is > 0 and <= 32
            && c.UsageMin <= 0x39 && (c.IsRange == 0 ? c.UsageMin : c.UsageMax) >= 0x30).ToArray();
    }

    internal static HidDeviceReader? TryCreate(nint device)
    {
        uint size = 0;
        if (HidNativeMethods.GetDeviceInfo(device, HidNativeMethods.PreparsedData, 0, ref size) == uint.MaxValue || size == 0)
            return null;
        var data = Marshal.AllocHGlobal(checked((int)size));
        try
        {
            if (HidNativeMethods.GetDeviceInfo(device, HidNativeMethods.PreparsedData, data, ref size) == uint.MaxValue
                || HidNativeMethods.HidP_GetCaps(data, out var caps) != HidNativeMethods.Success) return null;
            var reader = new HidDeviceReader(data, caps);
            data = 0;
            return reader;
        }
        catch (InvalidDataException) { return null; }
        finally { if (data != 0) Marshal.FreeHGlobal(data); }
    }

    internal bool IsActivity(byte[] report)
    {
        if (report.Length == 0) return false;
        var changed = false;
        foreach (var caps in _buttons)
        {
            if (caps.ReportId != 0 && caps.ReportId != report[0]) continue;
            var maximum = HidNativeMethods.HidP_MaxUsageListLength(0, caps.UsagePage, _data);
            if (maximum == 0 || maximum > 65536) continue;
            var usages = new ushort[maximum];
            var count = maximum;
            if (HidNativeMethods.HidP_GetUsages(0, caps.UsagePage, caps.LinkCollection, usages, ref count,
                    _data, report, (uint)report.Length) != HidNativeMethods.Success) continue;
            changed |= _filter.ButtonsChanged($"b:{caps.ReportId}:{caps.UsagePage}:{caps.LinkCollection}", usages.Take((int)count));
        }
        foreach (var caps in _values)
        {
            if (caps.ReportId != 0 && caps.ReportId != report[0]) continue;
            var last = caps.IsRange == 0 ? caps.UsageMin : caps.UsageMax;
            for (var usage = Math.Max(0x30, (int)caps.UsageMin); usage <= Math.Min(0x39, (int)last); usage++)
            {
                if (HidNativeMethods.HidP_GetUsageValue(0, caps.UsagePage, caps.LinkCollection, (ushort)usage,
                        out var raw, _data, report, (uint)report.Length) != HidNativeMethods.Success) continue;
                var value = SignedValue(raw, caps.BitSize, caps.LogicalMin < 0);
                if (usage != 0x39 && (value < caps.LogicalMin || value > caps.LogicalMax)) continue;
                var key = $"v:{caps.ReportId}:{caps.LinkCollection}:{usage}";
                if (caps.IsAbsolute == 0) { changed |= value != 0; continue; }
                var range = (double)caps.LogicalMax - caps.LogicalMin;
                // Hat switches are discrete. Axes require 10% travel to suppress analog jitter.
                changed |= _filter.ValueChanged(key, value, usage == 0x39 ? 0 : Math.Max(1, range * 0.10));
            }
        }
        return changed;
    }

    internal static long SignedValue(uint value, int bits, bool signed)
    {
        if (bits is < 1 or > 32) throw new ArgumentOutOfRangeException(nameof(bits));
        var mask = (1UL << bits) - 1;
        long result = (long)(value & mask);
        return signed && (result & (1L << (bits - 1))) != 0 ? result - (1L << bits) : result;
    }

    public void Dispose() { if (_data != 0) { Marshal.FreeHGlobal(_data); _data = 0; } }
}
