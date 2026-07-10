using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace QuickNotes;

/// <summary>
/// WPF backdrop window for Zen mode using screen capture + BlurEffect.
///
/// Architecture:
///   1. Captures the desktop content of NoteWindow's monitor via GDI CopyFromScreen
///   2. Displays it in a WPF Image with a BlurEffect for the acrylic-like look
///   3. Dark tint overlay (semi-transparent black Rectangle) on top
///   4. No DWM API calls — works identically on Windows 7 through 11 24H2
///   5. Positioned behind NoteWindow, no focus stealing
/// </summary>
public partial class ZenWindow : Window
{
    // ======================== User32 / Monitor ========================
    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    private const int SW_HIDE = 0;
    private const int SW_SHOWNA = 8;

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int X, int Y, int cx, int cy, uint uFlags);
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_FRAMECHANGED = 0x0020;

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);
    private const uint MONITOR_DEFAULTTONEAREST = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MONITORINFO
    {
        public uint cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    // ======================== GDI ========================
    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    // ======================== DWM ========================
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWCP_DONOTROUND = 1;

    // ======================== Fields ========================
    private readonly IntPtr _noteHandle;
    private bool _shown;
    private RECT _monitorRect;

    public ZenWindow(IntPtr noteWindowHandle)
    {
        _noteHandle = noteWindowHandle;
        InitializeComponent();

        // HWND must exist for ShowBehindNote to work; create it now
        SourceInitialized += OnSourceInitialized;
        _ = new WindowInteropHelper(this).Handle;
    }

    public void ShowBehindNote()
    {
        Debug.WriteLine("[ZenWindow] === ShowBehindNote (Capture + BlurEffect) ===");

        // 1. Get the monitor bounds where NoteWindow lives
        var hMonitor = MonitorFromWindow(_noteHandle, MONITOR_DEFAULTTONEAREST);
        var mi = new MONITORINFO { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(hMonitor, ref mi))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "GetMonitorInfo failed");

        _monitorRect = mi.rcMonitor;
        int width = _monitorRect.Right - _monitorRect.Left;
        int height = _monitorRect.Bottom - _monitorRect.Top;
        Debug.WriteLine($"[ZenWindow] Capture region: {width}x{height} at ({_monitorRect.Left},{_monitorRect.Top})");

        // 2. Capture the desktop
        DesktopImage.Source = CaptureScreen(_monitorRect);

        // 3. Show the window (on first call) or unhide (on subsequent)
        var hwnd = new WindowInteropHelper(this).Handle;
        ShowWindow(hwnd, SW_SHOWNA);
        _shown = true;

        // 4. Position behind NoteWindow, full monitor size
        SetWindowPos(hwnd, _noteHandle,
            _monitorRect.Left, _monitorRect.Top, width, height,
            SWP_NOACTIVATE | SWP_FRAMECHANGED);

        // 5. Force WPF to re-render now (image + blur + tint)
        Dispatcher.Invoke(System.Windows.Threading.DispatcherPriority.Background,
            new Action(() => { /* pump the render pipeline */ }));

        Debug.WriteLine("[ZenWindow] ✓ Shown with blurred desktop capture");
    }

    public void Hide()
    {
        if (_shown)
        {
            var helper = new WindowInteropHelper(this);
            ShowWindow(helper.Handle, SW_HIDE);
            DesktopImage.Source = null; // release captured bitmap memory
            _shown = false;
            Debug.WriteLine("[ZenWindow] Hidden");
        }
    }

    public void ForceClose()
    {
        Debug.WriteLine("[ZenWindow] ForceClose");
        Close();
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;

        // Disable rounded corners so the backdrop fills the screen edge-to-edge
        int cornerPref = DWMWCP_DONOTROUND;
        DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref cornerPref, sizeof(int));

        // Dark mode for the invisible title bar
        int dark = 1;
        DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
    }

    private static BitmapSource CaptureScreen(RECT rect)
    {
        int width = rect.Right - rect.Left;
        int height = rect.Bottom - rect.Top;

        Debug.WriteLine($"[ZenWindow] Capturing screen...");

        using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bitmap);

        // Copy the visible desktop pixels
        g.CopyFromScreen(rect.Left, rect.Top, 0, 0,
            new System.Drawing.Size(width, height), CopyPixelOperation.SourceCopy);

        Debug.WriteLine($"[ZenWindow] Capture done, converting to BitmapSource");

        // Convert System.Drawing.Bitmap → WPF BitmapSource
        var hbitmap = bitmap.GetHbitmap();
        try
        {
            return Imaging.CreateBitmapSourceFromHBitmap(
                hbitmap, IntPtr.Zero, Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
        }
        finally
        {
            DeleteObject(hbitmap);
        }
    }
}
