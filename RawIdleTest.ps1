
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

Add-Type -ReferencedAssemblies @(
    'System.Windows.Forms'
    'System.Windows.Forms.Primitives'
    'System.Drawing'
    'System.Drawing.Common'
    'System.Drawing.Primitives'
    'System.ComponentModel.Primitives'
    'System.ComponentModel'
    'System.Runtime'
) -TypeDefinition @'
using System;
using System.Drawing;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using System.ComponentModel;

public class RawIdleWindow : Form
{
    const int WM_INPUT = 0x00FF;
    const uint RIDEV_INPUTSINK = 0x00000100;

    [StructLayout(LayoutKind.Sequential)]
    struct RAWINPUTDEVICE
    {
        public ushort usUsagePage;
        public ushort usUsage;
        public uint dwFlags;
        public IntPtr hwndTarget;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct LASTINPUTINFO
    {
        public uint cbSize;
        public uint dwTime;
    }

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool RegisterRawInputDevices(
        RAWINPUTDEVICE[] devices,
        uint count,
        uint size);

    [DllImport("user32.dll")]
    static extern bool GetLastInputInfo(
        ref LASTINPUTINFO info);

    Label windowsLabel;
    Label rawLabel;
    Label eventsLabel;
    Timer timer;

    long lastRawTick;
    long rawEvents = 0;

    public RawIdleWindow()
    {
        Text = "Raw Input Idle Test";
        ClientSize = new Size(390, 180);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;

        windowsLabel = new Label {
            Location = new Point(20, 20),
            Size = new Size(350, 30),
            Font = new Font("Segoe UI", 12)
        };

        rawLabel = new Label {
            Location = new Point(20, 60),
            Size = new Size(350, 30),
            Font = new Font("Segoe UI", 12)
        };

        eventsLabel = new Label {
            Location = new Point(20, 100),
            Size = new Size(350, 30),
            Font = new Font("Segoe UI", 11)
        };

        Controls.Add(windowsLabel);
        Controls.Add(rawLabel);
        Controls.Add(eventsLabel);

        lastRawTick = Environment.TickCount64;

        timer = new Timer();
        timer.Interval = 500;
        timer.Tick += (s, e) => UpdateLabels();
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);

        var devices = new RAWINPUTDEVICE[] {
            new RAWINPUTDEVICE {
                usUsagePage = 1,
                usUsage = 2,
                dwFlags = RIDEV_INPUTSINK,
                hwndTarget = Handle
            },
            new RAWINPUTDEVICE {
                usUsagePage = 1,
                usUsage = 6,
                dwFlags = RIDEV_INPUTSINK,
                hwndTarget = Handle
            }
        };

        if (!RegisterRawInputDevices(
            devices,
            (uint)devices.Length,
            (uint)Marshal.SizeOf(typeof(RAWINPUTDEVICE))))
        {
            throw new Exception(
                "Raw Input registration failed. Error: " +
                Marshal.GetLastWin32Error());
        }

        timer.Start();
        UpdateLabels();
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_INPUT)
        {
            lastRawTick = Environment.TickCount64;
            rawEvents++;
        }

        base.WndProc(ref m);
    }

    void UpdateLabels()
    {
        LASTINPUTINFO info = new LASTINPUTINFO();
        info.cbSize = (uint)Marshal.SizeOf(typeof(LASTINPUTINFO));

        double winIdle = -1;

        if (GetLastInputInfo(ref info))
        {
            winIdle = unchecked(
                (uint)Environment.TickCount - info.dwTime
            ) / 1000.0;
        }

        double rawIdle =
            (Environment.TickCount64 - lastRawTick) / 1000.0;

        windowsLabel.Text =
            $"Windows Idle: {winIdle:F1} seconds";

        rawLabel.Text =
            $"Raw Input Idle: {rawIdle:F1} seconds";

        eventsLabel.Text =
            $"Raw Input Events: {rawEvents}";
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        timer.Stop();
        timer.Dispose();
        base.OnFormClosed(e);
    }
}
'@

[System.Windows.Forms.Application]::Run(
    [RawIdleWindow]::new()
)
