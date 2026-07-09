using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace QuickNotes;

/// <summary>
/// Maximized backdrop window used in Zen mode.
/// Uses the modern DWM SystemBackdrop API (Win11 22H2+ build 22621+)
/// to render native acrylic behind NoteWindow.
/// </summary>
public partial class ZenWindow : Window
{
    // --- Modern DWM backdrop API (Win11 22H2+) ---
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;
    private const int DWMSBT_TABBEDWINDOW = 4;  // Acrylic blur + tint
    private const int DWMSBT_MAINWINDOW = 2;    // Mica (fallback)

    // --- Monitor DPI helpers ---
    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);
    private const uint MONITOR_DEFAULTTONEAREST = 2;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr hMonitor, int dpiType, out uint dpiX, out uint dpiY);
    private const int MDT_EFFECTIVE_DPI = 0;

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MONITORINFO
    {
        public uint cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    private bool _allowClose;
    private readonly IntPtr _noteHandle;

    public ZenWindow(IntPtr noteWindowHandle)
    {
        _noteHandle = noteWindowHandle;
        InitializeComponent();
        SourceInitialized += OnSourceInitialized;
    }

    /// <summary>
    /// Shows ZenWindow on the same monitor as NoteWindow, applies native acrylic,
    /// and places it behind NoteWindow in z-order.
    /// </summary>
    public void ShowBehindNote()
    {
        // Position and size to the correct monitor before showing
        var mi = new MONITORINFO { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
        var hMonitor = MonitorFromWindow(_noteHandle, MONITOR_DEFAULTTONEAREST);

        if (GetMonitorInfo(hMonitor, ref mi))
        {
            // Get DPI for this specific monitor
            uint dpiX = 96, dpiY = 96;
            if (GetDpiForMonitor(hMonitor, MDT_EFFECTIVE_DPI, out dpiX, out dpiY) != 0)
            {
                dpiX = 96; dpiY = 96; // fallback
            }

            // Convert physical pixels → WPF device-independent pixels
            double scaleX = 96.0 / dpiX;
            double scaleY = 96.0 / dpiY;

            Left   = mi.rcWork.Left   * scaleX;
            Top    = mi.rcWork.Top    * scaleY;
            Width  = (mi.rcWork.Right - mi.rcWork.Left)   * scaleX;
            Height = (mi.rcWork.Bottom - mi.rcWork.Top)   * scaleY;
        }

        Show();

        // Place directly behind NoteWindow in z-order
        var zenHandle = new WindowInteropHelper(this).Handle;
        SetWindowPos(zenHandle, _noteHandle, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }

    /// <summary>
    /// Force-close without cancellation (called by NoteWindow during app shutdown).
    /// </summary>
    public void ForceClose()
    {
        _allowClose = true;
        Close();
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;

        // ---- Modern approach: DWM SystemBackdrop (Win11 22H2+) ----
        // Try acrylic first (tabbed = blurred + tinted), fall back to mica
        int backdropType = DWMSBT_TABBEDWINDOW;
        int hr = DwmSetWindowAttribute(handle, DWMWA_SYSTEMBACKDROP_TYPE, ref backdropType, sizeof(int));

        if (hr != 0)
        {
            // Fallback to Mica
            backdropType = DWMSBT_MAINWINDOW;
            DwmSetWindowAttribute(handle, DWMWA_SYSTEMBACKDROP_TYPE, ref backdropType, sizeof(int));
        }

        // ---- Dark mode for the backdrop ----
        int darkMode = 1;
        DwmSetWindowAttribute(handle, 20 /* DWMWA_USE_IMMERSIVE_DARK_MODE */, ref darkMode, sizeof(int));
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
