using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace QuickNotes;

/// <summary>
/// Maximized backdrop window used in Zen mode.
/// Uses the modern DWM backdrop API for glass/Mica effect on Win11.
/// Positioned behind NoteWindow on the same monitor.
/// </summary>
public partial class ZenWindow : Window
{
    // --- DWM backdrop API (Win11 22H2+) ---
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;
    private const int DWMSBT_MAINWINDOW = 2;    // Mica (subtle desktop tint)
    private const int DWMSBT_TABBEDWINDOW = 4;  // Acrylic (blur + tint)

    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_MICA_ALT_DISABLED = 34;

    private bool _allowClose;
    private readonly IntPtr _noteHandle;

    public ZenWindow(IntPtr noteWindowHandle)
    {
        _noteHandle = noteWindowHandle;
        InitializeComponent();
        SourceInitialized += OnSourceInitialized;
    }

    /// <summary>
    /// Shows ZenWindow on the same monitor as NoteWindow with the backdrop effect.
    /// </summary>
    public void ShowBehindNote()
    {
        // Show the window first (small, anywhere)
        Show();

        // Move to the correct monitor, then maximize
        // WPF's window manager handles per-monitor DPI when maximizing
        var hMonitor = MonitorFromWindow(_noteHandle, MONITOR_DEFAULTTONEAREST);
        var mi = new MONITORINFOEX();
        mi.cbSize = (uint)Marshal.SizeOf<MONITORINFOEX>();

        if (GetMonitorInfo(hMonitor, ref mi))
        {
            // Convert physical pixels to WPF DIPs
            uint dpiX = 96, dpiY = 96;
            if (GetDpiForMonitor(hMonitor, MDT_EFFECTIVE_DPI, out dpiX, out dpiY) != 0)
            {
                dpiX = 96; dpiY = 96;
            }
            double sx = 96.0 / dpiX;
            double sy = 96.0 / dpiY;

            // Position to the correct monitor's FULL area (including taskbar) for immersion
            Left   = mi.rcMonitor.Left   * sx;
            Top    = mi.rcMonitor.Top    * sy;
            Width  = (mi.rcMonitor.Right - mi.rcMonitor.Left)   * sx;
            Height = (mi.rcMonitor.Bottom - mi.rcMonitor.Top)   * sy;

            // Place behind NoteWindow in z-order
            var zenHandle = new WindowInteropHelper(this).Handle;
            SetWindowPos(zenHandle, _noteHandle, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }
        else
        {
            // Fallback: just maximize (will go to primary monitor)
            WindowState = WindowState.Maximized;
        }
    }

    /// <summary>
    /// Force-close without cancellation.
    /// </summary>
    public void ForceClose()
    {
        _allowClose = true;
        Close();
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;

        // Try acrylic first (Win11 22H2+ tabbed backdrop = blur + tint)
        int backdrop = DWMSBT_TABBEDWINDOW;
        int hr = DwmSetWindowAttribute(handle, DWMWA_SYSTEMBACKDROP_TYPE, ref backdrop, sizeof(int));

        if (hr != 0)
        {
            // Fallback to Mica (tint only, no blur)
            backdrop = DWMSBT_MAINWINDOW;
            DwmSetWindowAttribute(handle, DWMWA_SYSTEMBACKDROP_TYPE, ref backdrop, sizeof(int));
        }

        // Dark mode for the backdrop tint
        int darkMode = 1;
        DwmSetWindowAttribute(handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkMode, sizeof(int));
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!_allowClose)
        {
            e.Cancel = true;
            Hide();
        }
        base.OnClosing(e);
    }

    // --- Monitor and Win32 helpers ---
    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);
    private const uint MONITOR_DEFAULTTONEAREST = 2;

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX lpmi);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr hMonitor, int dpiType, out uint dpiX, out uint dpiY);
    private const int MDT_EFFECTIVE_DPI = 0;

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MONITORINFOEX
    {
        public uint cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }
}
