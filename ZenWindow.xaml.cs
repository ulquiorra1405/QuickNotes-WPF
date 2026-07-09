using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace QuickNotes;

/// <summary>
/// WPF backdrop window for Zen mode.
/// Uses modern DWM SystemBackdrop API (Win11 22H2+ build 22621) for real acrylic.
/// Reliable on Win11 24H2 (build 26100).
/// </summary>
public partial class ZenWindow : Window
{
    // --- DWM backdrop API ---
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;
    private const int DWMSBT_MAINWINDOW = 2;    // Mica
    private const int DWMSBT_TABBEDWINDOW = 3;  // Tabbed/acrylic (UWP-style blur+tint)
    private const int DWMSBT_FLOATING = 4;      // Floating acrylic
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    // --- Win32 positioning ---
    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);
    private const uint MONITOR_DEFAULTTONEAREST = 2;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MONITORINFO
    {
        public uint cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr hMonitor, int dpiType, out uint dpiX, out uint dpiY);
    private const int MDT_EFFECTIVE_DPI = 0;

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;

    private bool _dwmInitialized;
    private bool _allowClose;
    private readonly IntPtr _noteHandle;

    public ZenWindow(IntPtr noteWindowHandle)
    {
        _noteHandle = noteWindowHandle;
        InitializeComponent();
    }

    /// <summary>
    /// Shows the backdrop on the same monitor as NoteWindow.
    /// Safe to call multiple times (re-positions on re-show).
    /// </summary>
    public void ShowBehindNote()
    {
        // Force HWND creation (so we can call Win32 APIs)
        var handle = new WindowInteropHelper(this).Handle;

        // Initialize DWM backdrop on first call
        if (!_dwmInitialized)
        {
            InitializeDwm(handle);
            _dwmInitialized = true;
        }

        // Position on NoteWindow's monitor with correct DPI
        var hMonitor = MonitorFromWindow(_noteHandle, MONITOR_DEFAULTTONEAREST);
        var mi = new MONITORINFO { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
        GetMonitorInfo(hMonitor, ref mi);

        GetDpiForMonitor(hMonitor, MDT_EFFECTIVE_DPI, out uint dpiX, out uint dpiY);
        if (dpiX == 0) dpiX = 96;
        if (dpiY == 0) dpiY = 96;

        double sx = 96.0 / dpiX;
        double sy = 96.0 / dpiY;

        Left   = mi.rcMonitor.Left   * sx;
        Top    = mi.rcMonitor.Top    * sy;
        Width  = (mi.rcMonitor.Right - mi.rcMonitor.Left)   * sx;
        Height = (mi.rcMonitor.Bottom - mi.rcMonitor.Top)   * sy;

        // Show the window
        Show();

        // Place behind NoteWindow in z-order
        SetWindowPos(handle, _noteHandle, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }

    public void ForceClose()
    {
        _allowClose = true;
        Close();
    }

    private void InitializeDwm(IntPtr handle)
    {
        // Try DWM backdrops in preference order (best → fallback)
        int backdrop = DWMSBT_TABBEDWINDOW;  // Acrylic: blur + tint
        int hr = DwmSetWindowAttribute(handle, DWMWA_SYSTEMBACKDROP_TYPE, ref backdrop, sizeof(int));

        if (hr != 0)
        {
            backdrop = DWMSBT_FLOATING;     // Floating acrylic variant
            hr = DwmSetWindowAttribute(handle, DWMWA_SYSTEMBACKDROP_TYPE, ref backdrop, sizeof(int));
        }

        if (hr != 0)
        {
            backdrop = DWMSBT_MAINWINDOW;   // Mica (no blur, desktop tint)
            DwmSetWindowAttribute(handle, DWMWA_SYSTEMBACKDROP_TYPE, ref backdrop, sizeof(int));
        }

        // Dark mode — gives DWM backdrop a darker tint
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
}
