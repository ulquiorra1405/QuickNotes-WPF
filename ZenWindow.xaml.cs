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
///   1. Win32 POPUP window with WS_THICKFRAME (frame needed for DWM backdrop)
///   2. DWMWA_SYSTEMBACKDROP_TYPE = FLOATING (acrylic on 24H2)
///   3. DwmExtendFrameIntoClientArea with -1 margins → glass sheet
///      (the DWM frame covers the entire window → backdrop visible everywhere)
///   4. Dark mode tint via DWMWA_USE_IMMERSIVE_DARK_MODE
///   5. Window is invisible to clicks/focus (NOACTIVATE, TRANSPARENT, TOOLWINDOW)
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
    private const int DWMNCRP_DISABLED = 1;
    private const int DWMSBT_MAINWINDOW = 2;       // Mica
    private const int DWMSBT_TABBEDWINDOW = 3;     // Acrylic (Tabbed)
    private const int DWMSBT_FLOATING = 4;          // Acrylic (Floating)

    [StructLayout(LayoutKind.Sequential)]
    private struct MARGINS
    {
        public int leftWidth;
        public int rightWidth;
        public int topHeight;
        public int bottomHeight;
    }

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

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern ushort RegisterClassEx([In] ref WNDCLASSEX lpwcx);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern int GetClassInfoEx(IntPtr hInstance,
        string lpszClass, ref WNDCLASSEX lpwcx);

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
    private bool _dwmConfigured;
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
            CreateHwnd();
            ConfigureBackdrop();
        }

        // Position on NoteWindow's monitor
        var hMonitor = MonitorFromWindow(_noteHandle, MONITOR_DEFAULTTONEAREST);
        var mi = new MONITORINFO { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(hMonitor, ref mi))
            Debug.WriteLine("[ZenWindow] GetMonitorInfo FAILED");

        GetDpiForMonitor(hMonitor, MDT_EFFECTIVE_DPI, out uint dpiX, out uint dpiY);
        if (dpiX == 0) dpiX = 96;
        if (dpiY == 0) dpiY = 96;

        int monitorW = mi.rcMonitor.Right - mi.rcMonitor.Left;
        int monitorH = mi.rcMonitor.Bottom - mi.rcMonitor.Top;

        Debug.WriteLine($"[ZenWindow] DPI={dpiX}x{dpiY}, monitor=({mi.rcMonitor.Left},{mi.rcMonitor.Top}) {monitorW}x{monitorH}");

        SetWindowPos(_hWnd, _noteHandle,
            mi.rcMonitor.Left, mi.rcMonitor.Top,
            monitorW, monitorH,
            SWP_NOACTIVATE | SWP_SHOWWINDOW);

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

    private void CreateHwnd()
    {
        var hInst = GetModuleHandle(null);
        const string className = "QuickNotes_Zen_DWMv4";

        if (!_classRegistered)
        {
            var wc = new WNDCLASSEX
            {
                cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(WndProcCallback),
                hInstance = hInst,
                hbrBackground = IntPtr.Zero,
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
        // WS_POPUP | WS_THICKFRAME:
        //   THICKFRAME gives DWM a frame to render the backdrop into.
        //   POPUP prevents it from appearing as a normal window.
        // SWP_NOZORDER fix: use HWND_NOTOPMOST instead of _noteHandle for initial creation
        const uint WS_POPUP = 0x80000000u;
        const uint WS_THICKFRAME = 0x00040000u;
        const uint WS_VISIBLE = 0x10000000u;
        const uint WS_CLIPCHILDREN = 0x02000000u;

        const uint WS_EX_NOACTIVATE = 0x08000000u;
        const uint WS_EX_TRANSPARENT = 0x00000020u;
        const uint WS_EX_TOOLWINDOW = 0x00000080u;

        _hWnd = CreateWindowEx(
            WS_EX_NOACTIVATE | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW,
            className, IntPtr.Zero,
            WS_POPUP | WS_THICKFRAME | WS_CLIPCHILDREN,
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

    private void ConfigureBackdrop()
    {
        Debug.WriteLine("[ZenWindow] === ConfigureBackdrop ===");

        // ---------- 1. ENABLE DWM NON-CLIENT RENDERING ----------
        // The window frame must be rendered for the backdrop to show.
        int ncrEnabled = DWMNCRP_ENABLED;
        int hr = DwmSetWindowAttribute(_hWnd, DWMWA_NCRENDERING_POLICY, ref ncrEnabled, sizeof(int));
        Debug.WriteLine($"[ZenWindow] NCRENDERING={ncrEnabled} → 0x{hr:X8}");

        // ---------- 2. SYSTEM BACKDROP TYPE ----------
        // Try FLOATING (strongest acrylic) → TABBEDWINDOW → MAINWINDOW (Mica)
        var types = new[]
        {
            (type: DWMSBT_FLOATING, name: "FLOATING (4)"),
            (type: DWMSBT_TABBEDWINDOW, name: "TABBED (3)"),
            (type: DWMSBT_MAINWINDOW, name: "MAIN/ MICA (2)"),
        };

        int appliedType = -1;
        foreach (var (type, name) in types)
        {
            int t = type;
            hr = DwmSetWindowAttribute(_hWnd, DWMWA_SYSTEMBACKDROP_TYPE, ref t, sizeof(int));
            Debug.WriteLine($"[ZenWindow]  {name} → 0x{hr:X8}");
            if (hr == 0)
            {
                appliedType = type;
                Debug.WriteLine($"[ZenWindow] ✓ Using: {name}");
                break;
            }
        }

        if (appliedType == -1)
        {
            Debug.WriteLine("[ZenWindow] ✗ ALL backdrop types failed — no glass effect");
            return;
        }

        // ---------- 3. DARK MODE (acrylic tint) ----------
        int dark = 1;
        hr = DwmSetWindowAttribute(_hWnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
        Debug.WriteLine($"[ZenWindow] DARK_MODE → 0x{hr:X8}");

        // ---------- 4. EXTEND FRAME INTO CLIENT AREA (GLASS SHEET) ----------
        // -1 margins = "glass sheet" – extends the entire window frame
        // over the whole client area, so the backdrop is visible everywhere.
        MARGINS margins = new MARGINS { leftWidth = -1, rightWidth = -1, topHeight = -1, bottomHeight = -1 };
        hr = DwmExtendFrameIntoClientArea(_hWnd, ref margins);
        Debug.WriteLine($"[ZenWindow] EXTEND_FRAME(-1) → 0x{hr:X8}");

        _dwmConfigured = true;
        Debug.WriteLine("[ZenWindow] ✓ DWM configured OK");
    }

    // ======================== Window Procedure ========================
    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    private const uint WM_ERASEBKGND = 0x0014;
    private const uint WM_NCPAINT = 0x0085;

    private static IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        switch (msg)
        {
            case WM_ERASEBKGND:
                // Return FALSE → let DefWindowProc handle erasing.
                // With hbrBackground = NULL, this erases nothing,
                // and the DWM backdrop shines through.
                return IntPtr.Zero;

            case WM_NCPAINT:
                // Let DWM handle the non-client area painting.
                // Returning 0 = we handled it (leaves DWM in control).
                return IntPtr.Zero;
        }

        return DefWindowProc(hWnd, msg, wParam, lParam);
    }
}
