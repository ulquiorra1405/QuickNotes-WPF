using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace QuickNotes;

/// <summary>
/// WPF backdrop window for Zen mode.
/// Uses AllowsTransparency=True + DWM SystemBackdrop (DWMWA_SYSTEMBACKDROP_TYPE)
/// for native acrylic blur on Win11 22H2+.
/// 
/// Why WPF instead of pure Win32:
/// - WPF with AllowsTransparency=True creates a WS_EX_LAYERED window with 
///   proper per-pixel alpha → the DWM backdrop shows through transparent areas.
/// - Pure Win32 POPUP windows always paint an opaque surface → they hide the backdrop.
/// </summary>
public partial class ZenWindow : Window
{
    // ======================== DWM Backdrop ========================
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;
    private const int DWMSBT_TABBEDWINDOW = 3;  // Acrylic blur + tint
    private const int DWMSBT_FLOATING = 4;      // Acrylic, more pronounced
    private const int DWMSBT_MAINWINDOW = 2;    // Mica (no blur)
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    // ======================== Monitor/DPI ========================
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
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int X, int Y, int cx, int cy, uint uFlags);
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
    /// Shows the acrylic backdrop on the same monitor as NoteWindow.
    /// Safe to call multiple times.
    /// </summary>
    public void ShowBehindNote()
    {
        var handle = new WindowInteropHelper(this).Handle;

        // Init DWM backdrop once
        if (!_dwmInitialized)
        {
            Debug.WriteLine("[ZenWindow] Init DWM");
            InitializeDwm(handle);
            _dwmInitialized = true;
        }

        Debug.WriteLine("[ZenWindow] Positioning to NoteWindow's monitor");

        // Position on NoteWindow's monitor
        var hMonitor = MonitorFromWindow(_noteHandle, MONITOR_DEFAULTTONEAREST);
        var mi = new MONITORINFO { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
        GetMonitorInfo(hMonitor, ref mi);

        GetDpiForMonitor(hMonitor, MDT_EFFECTIVE_DPI, out uint dpiX, out uint dpiY);
        if (dpiX == 0) dpiX = 96;
        if (dpiY == 0) dpiY = 96;

        double sx = 96.0 / dpiX;
        double sy = 96.0 / dpiY;

        Debug.WriteLine($"[ZenWindow] Monitor: L={mi.rcMonitor.Left} T={mi.rcMonitor.Top} R={mi.rcMonitor.Right} B={mi.rcMonitor.Bottom}, DPI={dpiX}x{dpiY}");

        Left   = mi.rcMonitor.Left   * sx;
        Top    = mi.rcMonitor.Top    * sy;
        Width  = (mi.rcMonitor.Right - mi.rcMonitor.Left)   * sx;
        Height = (mi.rcMonitor.Bottom - mi.rcMonitor.Top)   * sy;

        Show();

        // Behind NoteWindow in z-order
        SetWindowPos(handle, _noteHandle, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }

    public void ForceClose()
    {
        _allowClose = true;
        Close();
    }

    private void InitializeDwm(IntPtr handle)
    {
        // Try Tabbed (acrylic) → Floating → MainWindow (mica)
        int[] types = [DWMSBT_TABBEDWINDOW, DWMSBT_FLOATING, DWMSBT_MAINWINDOW];
        int hr = -1;
        int applied = -1;

        foreach (int t in types)
        {
            int typeVal = t;
            hr = DwmSetWindowAttribute(handle, DWMWA_SYSTEMBACKDROP_TYPE, ref typeVal, sizeof(int));
            Debug.WriteLine($"[ZenWindow] Backdrop type={t} hResult=0x{hr:X8}");
            if (hr == 0)
            {
                applied = t;
                break;
            }
        }

        Debug.WriteLine($"[ZenWindow] Applied backdrop type={applied}, hr={hr}");

        // Dark mode
        int darkMode = 1;
        hr = DwmSetWindowAttribute(handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkMode, sizeof(int));
        Debug.WriteLine($"[ZenWindow] Dark mode hResult=0x{hr:X8}");
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
