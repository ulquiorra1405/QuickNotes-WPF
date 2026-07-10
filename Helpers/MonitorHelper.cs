using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace QuickNotes.Helpers;

internal static class MonitorHelper
{
    public static Rect GetMonitorWorkingArea(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        var hMonitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);

        var info = new MONITORINFOEX();
        info.cbSize = Marshal.SizeOf(typeof(MONITORINFOEX));

        if (!GetMonitorInfo(hMonitor, ref info))
            return SystemParameters.WorkArea; // fallback

        GetDpiForMonitor(hMonitor, MDT_EFFECTIVE_DPI, out uint dpiX, out _);
        double scale = dpiX / 96.0;

        return new Rect(
            info.rcWork.Left / scale,
            info.rcWork.Top / scale,
            (info.rcWork.Right - info.rcWork.Left) / scale,
            (info.rcWork.Bottom - info.rcWork.Top) / scale);
    }

    /// <summary>
    /// Gets the raw physical pixel bounds of the monitor that contains the given window.
    /// Uses GetWindowRect + MonitorFromPoint (more reliable for AllowsTransparency windows
    /// on multi-monitor setups than MonitorFromWindow).
    /// </summary>
    public static (int Left, int Top, int Width, int Height) GetMonitorPhysicalRect(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;

        // Get the window's actual physical screen rect
        RECT winRect;
        GetWindowRect(hwnd, out winRect);

        // Find the monitor containing the center of the window rect
        var center = new POINT
        {
            x = (winRect.Left + winRect.Right) / 2,
            y = (winRect.Top + winRect.Bottom) / 2
        };
        var hMonitor = MonitorFromPoint(center, MONITOR_DEFAULTTONEAREST);

        var info = new MONITORINFOEX();
        info.cbSize = Marshal.SizeOf(typeof(MONITORINFOEX));

        if (!GetMonitorInfo(hMonitor, ref info))
            return (0, 0, 1920, 1080); // fallback

        int w = info.rcMonitor.Right - info.rcMonitor.Left;
        int h = info.rcMonitor.Bottom - info.rcMonitor.Top;
        return (info.rcMonitor.Left, info.rcMonitor.Top, w, h);
    }

    private const uint MONITOR_DEFAULTTONEAREST = 2;
    private const int MDT_EFFECTIVE_DPI = 0;

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hwnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX lpmi);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int x, y;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MONITORINFOEX
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;
    }
}
