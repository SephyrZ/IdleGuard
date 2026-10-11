using System.Runtime.InteropServices;

namespace IdleGuard.Native;

internal static class ControllerNativeMethods
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct Gamepad
    {
        internal ushort Buttons;
        internal byte LeftTrigger, RightTrigger;
        internal short LeftX, LeftY, RightX, RightY;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct State { internal uint Packet; internal Gamepad Gamepad; }

    [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")]
    internal static extern uint GetState(uint index, out State state);
}
