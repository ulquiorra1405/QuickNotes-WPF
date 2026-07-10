using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace QuickNotes;

/// <summary>
/// WPF backdrop window for Zen mode.
/// Uses SetWindowCompositionAttribute with ACCENT_ENABLE_ACRYLICBLURBEHIND 
/// on a layered (AllowsTransparency=True) WPF window.
/// 
/// This is the original UWP acrylic approach — it works on layered windows
/// because WPF's AllowsTransparency=True creates WS_EX_LAYERED, and
/// SetWindowCompositionAttribute was designed for WS_EX_LAYERED windows.
/// </summary>
public partial class ZenWindow : Window
{
    // ======================== SetWindowCompositionAttribute ========================
    [DllImport("user32.dll")]
    private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowCompositionAttributeData
    {
        public WindowCompositionAttribute Attribute;
        public IntPtr Data;
        public int SizeOfData;
    }
    private enum WindowCompositionAttribute { WCA_ACCENT_POLICY = 19 }

    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy
    {
        public AccentState AccentState;
        public int AccentFlags;
        public uint GradientColor;
        public int AnimationId;
    }
    private enum AccentState
    {
        ACCENT_DISABLED = 0,
        ACCENT_ENABLE_GRADIENT = 1,
        ACCENT_ENABLE_TRANSPARENTGRADIENT = 2,
        ACCENT_ENABLE_BLURBEHIND = 3,
        ACCENT_ENABLE_ACRYLICBLURBEHIND = 4,
        ACCENT_ENABLE_HOSTBACKDROP = 5
    }

    // ======================== DWM backdrop as fallback ========================
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);
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
        SourceInitialized += OnSourceInitialized;
    }

    public void ShowBehindNote()
    {
        var handle = new WindowInteropHelper(this).Handle;

        if (!_dwmInitialized)
        {
            ApplyBackdrop(handle);
            _dwmInitialized = true;
        }

        // Position on NoteWindow's monitor
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

        Show();

        SetWindowPos(handle, _noteHandle, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }

    public void ForceClose()
    {
        _allowClose = true;
        Close();
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        ApplyBackdrop(handle);
        _dwmInitialized = true;
    }

    private void ApplyBackdrop(IntPtr handle)
    {
        // Try acrylic states in order: 4 → 5 → 3 → 2
        Debug.WriteLine($"[ZenWindow] Applying backdrop on Win11 build {Environment.OSVersion.Version}");

        // ACCENT_ENABLE_ACRYLICBLURBEHIND (4) — real acrylic with tint
        int result = TryAccent(handle, AccentState.ACCENT_ENABLE_ACRYLICBLURBEHIND, 0x55111111, 0);
        Debug.WriteLine($"[ZenWindow] ACRYLICBLURBEHIND → 0x{result:X8}");

        if (result != 0)
        {
            // ACCENT_ENABLE_HOSTBACKDROP (5) — Win11 alternative
            result = TryAccent(handle, AccentState.ACCENT_ENABLE_HOSTBACKDROP, 0x55111111, 0);
            Debug.WriteLine($"[ZenWindow] HOSTBACKDROP → 0x{result:X8}");
        }

        if (result != 0)
        {
            // ACCENT_ENABLE_BLURBEHIND (3) — no tint, just blur
            result = TryAccent(handle, AccentState.ACCENT_ENABLE_BLURBEHIND, 0, 0);
            Debug.WriteLine($"[ZenWindow] BLURBEHIND → 0x{result:X8}");
        }

        if (result != 0)
        {
            // ACCENT_ENABLE_TRANSPARENTGRADIENT (2) — just a tint overlay
            result = TryAccent(handle, AccentState.ACCENT_ENABLE_TRANSPARENTGRADIENT, 0x88111111, 0);
            Debug.WriteLine($"[ZenWindow] TRANSPARENTGRADIENT → 0x{result:X8}");
        }

        Debug.WriteLine($"[ZenWindow] Backdrop result: 0x{result:X8}");
    }

    private static int TryAccent(IntPtr handle, AccentState state, uint gradientColor, int accentFlags)
    {
        var accent = new AccentPolicy
        {
            AccentState = state,
            AccentFlags = accentFlags,
            GradientColor = gradientColor,
            AnimationId = 0
        };

        var data = new WindowCompositionAttributeData
        {
            Attribute = WindowCompositionAttribute.WCA_ACCENT_POLICY,
            Data = Marshal.AllocHGlobal(Marshal.SizeOf<AccentPolicy>()),
            SizeOfData = Marshal.SizeOf<AccentPolicy>()
        };

        try
        {
            Marshal.StructureToPtr(accent, data.Data, false);
            return SetWindowCompositionAttribute(handle, ref data);
        }
        catch
        {
            return -1;
        }
        finally
        {
            Marshal.FreeHGlobal(data.Data);
        }
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
