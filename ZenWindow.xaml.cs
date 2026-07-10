using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;

namespace QuickNotes;

/// <summary>
/// Pure Win32 backdrop window for Zen mode using DwmEnableBlurBehindWindow.
///
/// Architecture:
///   1. Win32 overlapped window (WS_OVERLAPPEDWINDOW) – non-layered, proper non-client area
///   2. DwmEnableBlurBehindWindow → legacy DWM API that blurs the desktop content
///      behind the window into the client area (Vista/7-era API, works on 24H2?)
///   3. Dark tint overlay via layered secondary approach or fallback
///   4. Positioned behind NoteWindow on same monitor
///   5. No focus stealing
/// </summary>
public class ZenWindow
{
    // ======================== DWM BlurBehind ========================
    [DllImport("dwmapi.dll")]
    private static extern int DwmEnableBlurBehindWindow(IntPtr hwnd, ref DWM_BLURBEHIND pBlurBehind);

    [StructLayout(LayoutKind.Sequential)]
    private struct DWM_BLURBEHIND
    {
        public uint dwFlags;
        public bool fEnable;
        public IntPtr hRgnBlur;
        public bool fTransitionOnMaximized;
    }

    private const uint DWM_BB_ENABLE = 1;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    // ======================== User32 ========================
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern IntPtr CreateWindowEx(
        uint dwExStyle, string lpClassName,
        IntPtr lpWindowName, uint dwStyle,
        int x, int y, int nWidth, int nHeight,
        IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    private const int SW_HIDE = 0;
    private const int SW_SHOWNA = 8;

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int X, int Y, int cx, int cy, uint uFlags);
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_FRAMECHANGED = 0x0020;

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern ushort RegisterClassEx([In] ref WNDCLASSEX lpwcx);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern int GetClassInfoEx(IntPtr hInstance, string lpszClass, ref WNDCLASSEX lpwcx);

    [DllImport("gdi32.dll")]
    private static extern IntPtr GetStockObject(int fnObject);
    private const int NULL_BRUSH = 5;

    // ======================== Monitor/DPI ========================
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
        public IntPtr hbrBackground;
        public string? lpszMenuName;
        public string? lpszClassName;
        public IntPtr hIconSm;
    }

    // ======================== Fields ========================
    private IntPtr _hWnd = IntPtr.Zero;
    private bool _shown;
    private readonly IntPtr _noteHandle;
    private static bool _classRegistered;
    private static readonly WndProcDelegate WndProcCallback = WndProc;

    public ZenWindow(IntPtr noteWindowHandle)
    {
        _noteHandle = noteWindowHandle;
    }

    public void ShowBehindNote()
    {
        Debug.WriteLine("[ZenWindow] === ShowBehindNote (DwmBlurBehind) ===");

        if (_hWnd == IntPtr.Zero)
        {
            CreateOverlappedHwnd();
            ApplyBlurBehind();
        }

        // Position on NoteWindow's monitor
        var hMonitor = MonitorFromWindow(_noteHandle, MONITOR_DEFAULTTONEAREST);
        var mi = new MONITORINFO { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(hMonitor, ref mi))
            Debug.WriteLine("[ZenWindow] GetMonitorInfo FAILED");

        Debug.WriteLine($"[ZenWindow] Placing at ({mi.rcMonitor.Left},{mi.rcMonitor.Top}) size={mi.rcMonitor.Right - mi.rcMonitor.Left}x{mi.rcMonitor.Bottom - mi.rcMonitor.Top}");

        SetWindowPos(_hWnd, _noteHandle,
            mi.rcMonitor.Left, mi.rcMonitor.Top,
            mi.rcMonitor.Right - mi.rcMonitor.Left,
            mi.rcMonitor.Bottom - mi.rcMonitor.Top,
            SWP_NOACTIVATE | SWP_FRAMECHANGED);

        ShowWindow(_hWnd, SW_SHOWNA);
        _shown = true;
        Debug.WriteLine("[ZenWindow] Window shown");
    }

    public void Hide()
    {
        if (_hWnd != IntPtr.Zero && _shown)
        {
            ShowWindow(_hWnd, SW_HIDE);
            _shown = false;
        }
    }

    public void ForceClose()
    {
        if (_hWnd != IntPtr.Zero)
        {
            ShowWindow(_hWnd, SW_HIDE);
            DestroyWindow(_hWnd);
            _hWnd = IntPtr.Zero;
            _shown = false;
            _classRegistered = false;
        }
    }

    private void CreateOverlappedHwnd()
    {
        var hInst = GetModuleHandle(null);
        const string className = "QuickNotes_Zen_BlurBehind";

        if (!_classRegistered)
        {
            var wc = new WNDCLASSEX
            {
                cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(WndProcCallback),
                hInstance = hInst,
                hbrBackground = GetStockObject(NULL_BRUSH),
                lpszClassName = className,
            };

            ushort atom = RegisterClassEx(ref wc);
            if (atom == 0)
            {
                var check = new WNDCLASSEX { cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>() };
                if (GetClassInfoEx(hInst, className, ref check) != 0)
                    atom = 1;
                else
                    Debug.WriteLine($"[ZenWindow] RegisterClassEx failed: 0x{Marshal.GetLastWin32Error():X8}");
            }
            _classRegistered = true;
        }

        // WS_OVERLAPPEDWINDOW for proper non-client area
        const uint WS_OVERLAPPEDWINDOW = 0x00CF0000u;
        const uint EX_NOACTIVATE = 0x08000000u;
        const uint EX_TOOLWINDOW = 0x00000080u;

        _hWnd = CreateWindowEx(
            EX_NOACTIVATE | EX_TOOLWINDOW,
            className, IntPtr.Zero,
            WS_OVERLAPPEDWINDOW,
            0, 0, 100, 100,
            IntPtr.Zero, IntPtr.Zero, hInst, IntPtr.Zero);

        if (_hWnd == IntPtr.Zero)
        {
            int err = Marshal.GetLastWin32Error();
            Debug.WriteLine($"[ZenWindow] CreateWindowEx FAILED: 0x{err:X8}");
            throw new Win32Exception(err, $"CreateWindowEx failed: 0x{err:X8}");
        }

        Debug.WriteLine($"[ZenWindow] hWnd = 0x{_hWnd.ToString("X8")}");
    }

    private void ApplyBlurBehind()
    {
        Debug.WriteLine("[ZenWindow] === ApplyBlurBehind ===");

        // ---------- 1. DwmEnableBlurBehindWindow ----------
        var blur = new DWM_BLURBEHIND
        {
            dwFlags = DWM_BB_ENABLE,
            fEnable = true,
            hRgnBlur = IntPtr.Zero,
            fTransitionOnMaximized = false,
        };

        int hr = DwmEnableBlurBehindWindow(_hWnd, ref blur);
        Debug.WriteLine($"[ZenWindow] DwmEnableBlurBehindWindow → 0x{hr:X8} {(hr == 0 ? "✓" : "✗")}");

        // ---------- 2. DARK MODE ----------
        int dark = 1;
        hr = DwmSetWindowAttribute(_hWnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
        Debug.WriteLine($"[ZenWindow] DARK_MODE → 0x{hr:X8} {(hr == 0 ? "✓" : "✗")}");

        Debug.WriteLine("[ZenWindow] ✓ BlurBehind configured");
    }

    // ======================== Window Procedure ========================
    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    private const uint WM_ERASEBKGND = 0x0014;

    private static IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        switch (msg)
        {
            case WM_ERASEBKGND:
                // Return 1 → DefWindowProc doesn't paint background
                return (IntPtr)1;
        }
        return DefWindowProc(hWnd, msg, wParam, lParam);
    }
}
