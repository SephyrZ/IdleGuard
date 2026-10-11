using System.Runtime.InteropServices;

namespace IdleGuard.Native;

internal static class HidNativeMethods
{
    internal const uint PreparsedData = 0x20000005;
    internal const int Success = 0x00110000;

    [StructLayout(LayoutKind.Explicit, Size = 64)]
    internal struct Caps
    {
        [FieldOffset(0)] internal ushort Usage;
        [FieldOffset(2)] internal ushort UsagePage;
        [FieldOffset(4)] internal ushort InputReportLength;
        [FieldOffset(46)] internal ushort InputButtonCaps;
        [FieldOffset(48)] internal ushort InputValueCaps;
    }

    [StructLayout(LayoutKind.Explicit, Size = 72)]
    internal struct ButtonCaps
    {
        [FieldOffset(0)] internal ushort UsagePage;
        [FieldOffset(2)] internal byte ReportId;
        [FieldOffset(6)] internal ushort LinkCollection;
    }

    [StructLayout(LayoutKind.Explicit, Size = 72)]
    internal struct ValueCaps
    {
        [FieldOffset(0)] internal ushort UsagePage;
        [FieldOffset(2)] internal byte ReportId;
        [FieldOffset(6)] internal ushort LinkCollection;
        [FieldOffset(12)] internal byte IsRange;
        [FieldOffset(15)] internal byte IsAbsolute;
        [FieldOffset(18)] internal ushort BitSize;
        [FieldOffset(20)] internal ushort ReportCount;
        [FieldOffset(40)] internal int LogicalMin;
        [FieldOffset(44)] internal int LogicalMax;
        [FieldOffset(56)] internal ushort UsageMin;
        [FieldOffset(58)] internal ushort UsageMax;
    }

    [DllImport("user32.dll", EntryPoint = "GetRawInputDeviceInfoW", SetLastError = true)]
    internal static extern uint GetDeviceInfo(nint device, uint command, nint data, ref uint size);
    [DllImport("hid.dll")]
    internal static extern int HidP_GetCaps(nint data, out Caps caps);
    [DllImport("hid.dll")]
    internal static extern int HidP_GetButtonCaps(int reportType, [Out] ButtonCaps[] caps, ref ushort count, nint data);
    [DllImport("hid.dll")]
    internal static extern int HidP_GetValueCaps(int reportType, [Out] ValueCaps[] caps, ref ushort count, nint data);
    [DllImport("hid.dll")]
    internal static extern uint HidP_MaxUsageListLength(int reportType, ushort usagePage, nint data);
    [DllImport("hid.dll")]
    internal static extern int HidP_GetUsages(int reportType, ushort usagePage, ushort linkCollection,
        [Out] ushort[] usages, ref uint count, nint data, byte[] report, uint reportLength);
    [DllImport("hid.dll")]
    internal static extern int HidP_GetUsageValue(int reportType, ushort usagePage, ushort linkCollection,
        ushort usage, out uint value, nint data, byte[] report, uint reportLength);
}
