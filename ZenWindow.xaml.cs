using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;

namespace QuickNotes;

/// <summary>
/// Pure Win32 backdrop window for Zen mode.
/// Uses modern DWM SystemBackdrop API (DWMWA_SYSTEMBACKDROP_TYPE) for acrylic on Win11 22H2+.
/// No WPF rendering — just a native Win32 window with acrylic effect.
/// Window is mouse-transparent so clicks pass through to NoteWindow.
/// </summary>
public class ZenWindow
{
    // ======================== DWM Backdrop ========================
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;
    private const int DWMSBT_TABBEDWINDOW = 3;  // Acrylic blur + tint
    private const int DWMSBT_FLOATING = 4;      // Acrylic, more pronounced
    private const int DWMSBT_MAINWINDOW = 2;    // Mica (no blur)
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    // ======================== Win32 Window Creation ========================
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern IntPtr CreateWindowEx(
        uint dwExStyle, string lpClassName, IntPtr lpWindowName,
        uint dwStyle, int x, int y, int nWidth, int nHeight,
        IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    private const int SW_HIDE = 0;
    private const int SW_SHOWNOACTIVATE = 4;

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int X, int Y, int cx, int cy, uint uFlags);
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_SHOWWINDOW = 0x0040;

    [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern ushort RegisterClassEx([In] ref WNDCLASSEX lpwcx);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern int GetClassInfoEx(IntPtr hInstance, string lpszClass, ref WNDCLASSEX lpwcx);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

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

    // ======================== Structs ========================
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct WNDCLASSEX
    {
        public uint cbSize;
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;  // NULL = no background paint → DWM backdrop visible
        public string? lpszMenuName;
        public string? lpszClassName;
        public IntPtr hIconSm;
    }

    // ======================== Fields ========================
    private IntPtr _hWnd = IntPtr.Zero;
    private bool _shown;
    private readonly IntPtr _noteHandle;
    private static readonly WndProcDelegate WndProcCallback = WndProc;
    private static bool _classRegistered;

    public ZenWindow(IntPtr noteWindowHandle)
    {
        _noteHandle = noteWindowHandle;
    }

    /// <summary>
    /// Creates the backdrop and shows it on the same monitor as NoteWindow.
    /// </summary>
    public void ShowBehindNote()
    {
        Debug.WriteLine("[ZenWindow] ShowBehindNote");

        if (_hWnd == IntPtr.Zero)
        {
            CreateHwnd();
            ApplyDwmBackdrop();
        }

        var hMonitor = MonitorFromWindow(_noteHandle, MONITOR_DEFAULTTONEAREST);
        var mi = new MONITORINFO { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
        GetMonitorInfo(hMonitor, ref mi);

        GetDpiForMonitor(hMonitor, MDT_EFFECTIVE_DPI, out uint dpiX, out uint dpiY);
        if (dpiX == 0) dpiX = 96;
        if (dpiY == 0) dpiY = 96;

        SetWindowPos(_hWnd, _noteHandle,
            mi.rcMonitor.Left, mi.rcMonitor.Top,
            mi.rcMonitor.Right - mi.rcMonitor.Left,
            mi.rcMonitor.Bottom - mi.rcMonitor.Top,
            SWP_NOACTIVATE | SWP_SHOWWINDOW);

        _shown = true;
    }

    /// <summary>
    /// Hides the backdrop (re-shows on next ShowBehindNote).
    /// </summary>
    public void Hide()
    {
        if (_hWnd != IntPtr.Zero && _shown)
        {
            ShowWindow(_hWnd, SW_HIDE);
            _shown = false;
        }
    }

    /// <summary>
    /// Destroys the backdrop permanently.
    /// </summary>
    public void ForceClose()
    {
        if (_hWnd != IntPtr.Zero)
        {
            ShowWindow(_hWnd, SW_HIDE);
            DestroyWindow(_hWnd);
            _hWnd = IntPtr.Zero;
            _shown = false;
        }
    }

    private void CreateHwnd()
    {
        var hInst = GetModuleHandle(null);
        const string className = "QuickNotes_ZenWindow_v3";

        // Register class once
        if (!_classRegistered)
        {
            var wc = new WNDCLASSEX
            {
                cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
                style = 0,
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(WndProcCallback),
                hInstance = hInst,
                hbrBackground = IntPtr.Zero,  // ← NO background brush → DWM backdrop visible!
                lpszClassName = className,
                lpszMenuName = null
            };

            ushort atom = RegisterClassEx(ref wc);
            if (atom == 0)
            {
                // May already be registered from a previous close
                var check = new WNDCLASSEX { cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>() };
                if (GetClassInfoEx(hInst, className, ref check) == 0)
                    Debug.WriteLine($"[ZenWindow] RegisterClassEx failed: 0x{Marshal.GetLastWin32Error():X8}");
            }
            _classRegistered = true;
        }

        const uint WS_POPUP = 0x80000000u;
        const uint WS_EX_NOACTIVATE = 0x08000000u;
        const uint WS_EX_TRANSPARENT = 0x00000020u;  // mouse clicks pass through

        _hWnd = CreateWindowEx(
            WS_EX_NOACTIVATE | WS_EX_TRANSPARENT,
            className, IntPtr.Zero,
            WS_POPUP,
            -5000, -5000, 100, 100,
            IntPtr.Zero, IntPtr.Zero, hInst, IntPtr.Zero);

        if (_hWnd == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateWindowEx failed");
    }

    private void ApplyDwmBackdrop()
    {
        // Try Tabbed (acrylic) → Floating → MainWindow (mica)
        int[] types = [DWMSBT_TABBEDWINDOW, DWMSBT_FLOATING, DWMSBT_MAINWINDOW];
        int hr = -1;
        int appliedType = 0;

        foreach (int type in types)
        {
            int t = type;
            hr = DwmSetWindowAttribute(_hWnd, DWMWA_SYSTEMBACKDROP_TYPE, ref t, sizeof(int));
            Debug.WriteLine($"[ZenWindow] DwmSetWindowAttribute(type={type}) → 0x{hr:X8}");
            if (hr == 0)
            {
                appliedType = type;
                break;
            }
        }

        if (hr == 0)
            Debug.WriteLine($"[ZenWindow] DWM backdrop type {appliedType} OK");
        else
            Debug.WriteLine($"[ZenWindow] ALL DWM backdrops FAILED");

        // Dark mode
        int darkMode = 1;
        DwmSetWindowAttribute(_hWnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkMode, sizeof(int));
    }

    // ======================== Window Procedure ========================
    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    private const uint WM_ERASEBKGND = 0x0014;

    private static IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WM_ERASEBKGND)
        {
            // Return TRUE without painting → DWM backdrop shows through
            return new IntPtr(1);
        }
        return DefWindowProc(hWnd, msg, wParam, lParam);
    }
}
