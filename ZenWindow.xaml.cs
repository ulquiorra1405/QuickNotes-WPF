using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;

namespace QuickNotes;

/// <summary>
/// Pure Win32 backdrop window for Zen mode.
///
/// Architecture:
///   1. Win32 overlapped window (WS_OVERLAPPEDWINDOW) with proper non-client area
///   2. DWMWA_SYSTEMBACKDROP_TYPE = FLOATING (acrylic on 24H2)
///      → renders acrylic effect on the non-client frame area
///   3. DwmExtendFrameIntoClientArea(-1) → glass sheet — frame covers entire window
///   4. DWMWA_USE_IMMERSIVE_DARK_MODE = dark tint
///   5. hbrBackground = NULL_BRUSH → no background painting over the DWM effect
///   6. No WS_EX_LAYERED → DWM backdrop works correctly
///   7. Transparent to mouse/focus (NOACTIVATE, TRANSPARENT, TOOLWINDOW)
/// </summary>
public class ZenWindow
{
    // ======================== DWM ========================
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS pMarInset);

    private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;
    private const int DWMWA_NCRENDERING_POLICY = 2;
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMNCRP_ENABLED = 2;
    private const int DWMSBT_TABBEDWINDOW = 3;
    private const int DWMSBT_FLOATING = 4;

    [StructLayout(LayoutKind.Sequential)]
    private struct MARGINS { public int leftWidth, rightWidth, topHeight, bottomHeight; }

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
    private const uint SWP_SHOWWINDOW = 0x0040;
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
        Debug.WriteLine("[ZenWindow] === ShowBehindNote ===");

        if (_hWnd == IntPtr.Zero)
        {
            CreateOverlappedHwnd();
            ApplyDwmBackdrop();
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
            SWP_NOACTIVATE | SWP_SHOWWINDOW | SWP_FRAMECHANGED);

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
        const string className = "QuickNotes_Zen_DWM_OVERLAPPED";

        if (!_classRegistered)
        {
            var wc = new WNDCLASSEX
            {
                cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(WndProcCallback),
                hInstance = hInst,
                // NULL_BRUSH → the background is NOT painted → DWM backdrop shows through
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

        // ---- Window styles ----
        // WS_OVERLAPPEDWINDOW = WS_OVERLAPPED | WS_CAPTION | WS_SYSMENU
        //                       | WS_THICKFRAME | WS_MINIMIZEBOX | WS_MAXIMIZEBOX
        // This gives DWM a proper non-client frame to render the backdrop onto.
        const uint WS_OVERLAPPEDWINDOW = 0x00CF0000u;

        // Extended: no activate, no mouse capture, no taskbar entry
        const uint EX_NOACTIVATE = 0x08000000u;
        const uint EX_TRANSPARENT = 0x00000020u;
        const uint EX_TOOLWINDOW = 0x00000080u;

        _hWnd = CreateWindowEx(
            EX_NOACTIVATE | EX_TRANSPARENT | EX_TOOLWINDOW,
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

    private void ApplyDwmBackdrop()
    {
        Debug.WriteLine("[ZenWindow] === ApplyDwmBackdrop ===");

        // ---------- 1. NCRENDERING — ensure DWM renders the non-client frame ----------
        int ncr = DWMNCRP_ENABLED;
        int hr = DwmSetWindowAttribute(_hWnd, DWMWA_NCRENDERING_POLICY, ref ncr, sizeof(int));
        Debug.WriteLine($"[ZenWindow] NCRENDERING={ncr} → 0x{hr:X8} {(hr == 0 ? "✓" : "✗")}");

        // ---------- 2. SYSTEM BACKDROP TYPE ----------
        // Try FLOATING (strong acrylic) → TABBEDWINDOW (standard acrylic)
        int[] dwmTypes = { DWMSBT_FLOATING, DWMSBT_TABBEDWINDOW };
        string[] dwmNames = { "FLOATING (4)", "TABBED (3)" };
        bool backdropOk = false;
        for (int i = 0; i < dwmTypes.Length; i++)
        {
            int t = dwmTypes[i];
            hr = DwmSetWindowAttribute(_hWnd, DWMWA_SYSTEMBACKDROP_TYPE, ref t, sizeof(int));
            Debug.WriteLine($"[ZenWindow] DWM {dwmNames[i]} → 0x{hr:X8} {(hr == 0 ? "✓" : "✗")}");
            if (hr == 0)
            {
                Debug.WriteLine($"[ZenWindow] ✓ Using DWM: {dwmNames[i]}");
                backdropOk = true;
                break;
            }
        }

        if (!backdropOk)
        {
            Debug.WriteLine("[ZenWindow] ✗ ALL DWM backdrop types failed");
            return;
        }

        // ---------- 3. DARK MODE — provides the dark acrylic tint ----------
        int dark = 1;
        hr = DwmSetWindowAttribute(_hWnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
        Debug.WriteLine($"[ZenWindow] DARK_MODE → 0x{hr:X8} {(hr == 0 ? "✓" : "✗")}");

        // ---------- 4. EXTEND FRAME (glass sheet) ----------
        // -1 margins make the DWM non-client frame cover the entire window,
        // so the acrylic backdrop is visible across the full surface.
        var margins = new MARGINS { leftWidth = -1, rightWidth = -1, topHeight = -1, bottomHeight = -1 };
        hr = DwmExtendFrameIntoClientArea(_hWnd, ref margins);
        Debug.WriteLine($"[ZenWindow] EXTEND_FRAME(-1) → 0x{hr:X8} {(hr == 0 ? "✓" : "✗")}");

        // Re-apply the extended frame
        SetWindowPos(_hWnd, IntPtr.Zero, 0, 0, 0, 0,
            SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_FRAMECHANGED);

        Debug.WriteLine("[ZenWindow] ✓ DWM backdrop configured");
    }

    // ======================== Window Procedure ========================
    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    private const uint WM_ERASEBKGND = 0x0014;

    private static IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        switch (msg)
        {
            case WM_ERASEBKGND:
                // Return nonzero = we erased the background.
                // With hbrBackground = NULL_BRUSH, WM_ERASEBKGND paints nothing,
                // but returning 0 might let DefWindowProc paint something.
                // Return 1 → prevents DefWindowProc from painting opaque background
                return (IntPtr)1;
        }
        return DefWindowProc(hWnd, msg, wParam, lParam);
    }
}
