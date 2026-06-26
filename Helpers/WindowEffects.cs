using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

// ReSharper disable InconsistentNaming

namespace QuickNotes.Helpers;

public static class WindowEffects
{
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateRoundRectRgn(int x1, int y1, int x2, int y2, int cx, int cy);

    [DllImport("user32.dll")]
    private static extern int SetWindowRgn(IntPtr hWnd, IntPtr hRgn, bool bRedraw);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    // DWMWA constants (Windows 11 22H2+)
    private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;
    private const int DWMWA_USE_HOSTBACKDROPBRUSH = 17;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;

    /// <summary>
    /// Sets the DWM system backdrop type for the window (Windows 11 22H2+).
    /// </summary>
    public static void SetBackdropType(Window window, DwmBackdropType type)
    {
        if (window == null) return;

        var helper = new WindowInteropHelper(window);
        var hwnd = helper.Handle;
        if (hwnd == IntPtr.Zero) return;

        int backdropType = (int)type;
        DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref backdropType, sizeof(int));
    }

    /// <summary>
    /// Enables the host backdrop brush, needed for acrylic/mica to render
    /// correctly on WindowStyle=None windows.
    /// </summary>
    public static void UseHostBackdropBrush(Window window, bool enable = true)
    {
        if (window == null) return;

        var helper = new WindowInteropHelper(window);
        var hwnd = helper.Handle;
        if (hwnd == IntPtr.Zero) return;

        int value = enable ? 1 : 0;
        DwmSetWindowAttribute(hwnd, DWMWA_USE_HOSTBACKDROPBRUSH, ref value, sizeof(int));
    }

    /// <summary>
    /// Sets the window corner preference (Windows 11).
    /// RoundSmall gives ≈4px radius, Round gives ≈8px.
    /// </summary>
    public static void SetCornerPreference(Window window, DwmCornerPreference pref)
    {
        if (window == null) return;

        var helper = new WindowInteropHelper(window);
        var hwnd = helper.Handle;
        if (hwnd == IntPtr.Zero) return;

        int cornerPref = (int)pref;
        DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref cornerPref, sizeof(int));
    }

    /// <summary>
    /// Clips the window to a rounded rectangle shape so the backdrop
    /// doesn't show beyond the rounded corners.
    /// </summary>
    public static void SetRoundRectRegion(Window window, int cornerRadius = 10)
    {
        if (window == null) return;

        var helper = new WindowInteropHelper(window);
        var hwnd = helper.Handle;
        if (hwnd == IntPtr.Zero) return;

        if (!GetWindowRect(hwnd, out var rect)) return;

        int w = rect.Right - rect.Left;
        int h = rect.Bottom - rect.Top;
        if (w <= 0 || h <= 0) return;

        IntPtr rgn = CreateRoundRectRgn(0, 0, w, h, cornerRadius, cornerRadius);
        if (rgn != IntPtr.Zero)
        {
            SetWindowRgn(hwnd, rgn, true);
        }
    }
}

public enum DwmBackdropType
{
    Disable = 1,
    Mica = 2,
    Acrylic = 4,
}

public enum DwmCornerPreference
{
    Default = 0,
    DoNotRound = 1,
    Round = 2,
    RoundSmall = 3,
}
