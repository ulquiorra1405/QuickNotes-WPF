using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;

namespace QuickNotes;

/// <summary>
/// Pure Win32 backdrop window for Zen mode.
/// Creates a non‑layered POPUP window and applies DWM SystemBackdrop (DWMWA_SYSTEMBACKDROP_TYPE).
/// NO layered style → DWM backdrop works correctly.
/// NO WPF rendering → no opaque surface to block the backdrop.
/// The window is invisible to the mouse (WS_EX_TRANSPARENT + WS_EX_NOACTIVATE).
/// </summary>
public class ZenWindow
{
    // ======================== DWM SystemBackdrop (Win11 22H2+) ========================
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;
    private const int DWMSBT_AUTO = 0;
    private const int DWMSBT_MAINWINDOW = 2;      // Mica
    private const int DWMSBT_TABBEDWINDOW = 3;    // Acrylic (Tabbed)
    private const int DWMSBT_FLOATING = 4;         // Acrylic (Floating, more pronounced)
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_MICA_ALT_COMPAT_MODE = 34;

    // ======================== Win32 Window ========================
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern IntPtr CreateWindowEx(
        uint dwExStyle, string lpClassName,
        IntPtr lpWindowName,
        uint dwStyle, int x, int y, int nWidth, int nHeight,
        IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    private const int SW_HIDE = 0;
    private const int SW_SHOWNA = 8;  // Show without activating

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
    private static extern int GetClassInfoEx(IntPtr hInstance,
        string lpszClass, ref WNDCLASSEX lpwcx);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

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

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("gdi32.dll")]
    private static extern int FillRect(IntPtr hDC, ref RECT lprc, IntPtr hbr);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateSolidBrush(int color);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

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
    private static bool _useCustomTint; // set if DWM backdrop doesn't provide enough tint
    private static AccentState _fallbackTint; // if DWMSBT fails entirely, try SWCA

    // ======================== SWCA fallback ========================
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
        ACCENT_ENABLE_BLURBEHIND = 3,
        ACCENT_ENABLE_HOSTBACKDROP = 5,
    }

    private enum AccentFlags
    {
        DrawAllBorders = 0x20,
        PostNotBottom = 0x40,
    }

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
            ApplyDwmBackdrop();
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

        Debug.WriteLine($"[ZenWindow] Placing at ({mi.rcMonitor.Left},{mi.rcMonitor.Top}) size={monitorW}x{monitorH}");

        bool ok = SetWindowPos(_hWnd, _noteHandle,
            mi.rcMonitor.Left, mi.rcMonitor.Top,
            monitorW, monitorH,
            SWP_NOACTIVATE | SWP_SHOWWINDOW);

        Debug.WriteLine($"[ZenWindow] SetWindowPos = {ok}");

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
            _classRegistered = false; // reset for next creation if needed
        }
    }

    private void CreateHwnd()
    {
        var hInst = GetModuleHandle(null);
        const string className = "QuickNotes_Zen_DWMSBT";

        // Register class once
        if (!_classRegistered)
        {
            var wc = new WNDCLASSEX
            {
                cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
                style = 0,
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(WndProcCallback),
                hInstance = hInst,
                hbrBackground = IntPtr.Zero,  // NULL brush → DWM backdrop visible!
                lpszClassName = className,
                lpszMenuName = null
            };

            ushort atom = RegisterClassEx(ref wc);
            if (atom == 0)
            {
                // Already registered?
                var check = new WNDCLASSEX { cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>() };
                if (GetClassInfoEx(hInst, className, ref check) != 0)
                    atom = 1;
                else
                    Debug.WriteLine($"[ZenWindow] RegisterClassEx failed: 0x{Marshal.GetLastWin32Error():X8}");
            }
            _classRegistered = true;
        }

        const uint WS_POPUP = 0x80000000u;
        const uint WS_VISIBLE = 0x10000000u;
        const uint WS_EX_NOACTIVATE = 0x08000000u;
        const uint WS_EX_TRANSPARENT = 0x00000020u;
        const uint WS_EX_TOOLWINDOW = 0x00000080u; // hides from Alt+Tab

        // NOT layered: no WS_EX_LAYERED
        // NOT WS_EX_APPWINDOW: no taskbar entry
        _hWnd = CreateWindowEx(
            WS_EX_NOACTIVATE | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW,
            className, IntPtr.Zero,
            WS_POPUP,
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
        Debug.WriteLine($"[ZenWindow] === Applying DWM SystemBackdrop ===");
        Debug.WriteLine($"[ZenWindow] OS: {Environment.OSVersion}");

        // ======================== Try DWM SystemBackdrop ========================
        // DWM SystemBackdrop requires a NON-layered window.
        // Our window is WS_POPUP without WS_EX_LAYERED → perfect.

        var attempts = new[]
        {
            (type: DWMSBT_FLOATING,   name: "FLOATING (4)"),
            (type: DWMSBT_TABBEDWINDOW, name: "TABBEDWINDOW (3)"),
            (type: DWMSBT_MAINWINDOW,  name: "MAINWINDOW (2)"),
        };

        int hr = -1;
        int applied = -1;
        string? appliedName = null;

        foreach (var (type, name) in attempts)
        {
            int t = type;
            hr = DwmSetWindowAttribute(_hWnd, DWMWA_SYSTEMBACKDROP_TYPE, ref t, sizeof(int));
            Debug.WriteLine($"[ZenWindow]  {name} → 0x{hr:X8}");
            if (hr == 0)
            {
                applied = type;
                appliedName = name;
                break;
            }
        }

        if (hr == 0)
        {
            Debug.WriteLine($"[ZenWindow] ✓ DWM backdrop: {appliedName}");

            // Dark mode
            int dark = 1;
            hr = DwmSetWindowAttribute(_hWnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
            Debug.WriteLine($"[ZenWindow]  DARK_MODE → 0x{hr:X8}");
        }
        else
        {
            Debug.WriteLine($"[ZenWindow] ✗ DWM SystemBackdrop ALL FAILED");

            // ======================== FALLBACK: SetWindowCompositionAttribute ========================
            Debug.WriteLine("[ZenWindow] Trying SWCA fallback...");

            // Try BLURBEHIND (works on non-layered windows)
            hr = TrySwca(_hWnd, AccentState.ACCENT_ENABLE_BLURBEHIND, 0);
            Debug.WriteLine($"[ZenWindow]  SWCA BLURBEHIND → 0x{hr:X8}");

            if (hr != 0)
            {
                hr = TrySwca(_hWnd, AccentState.ACCENT_ENABLE_HOSTBACKDROP, 0x55111111);
                Debug.WriteLine($"[ZenWindow]  SWCA HOSTBACKDROP → 0x{hr:X8}");
            }

            if (hr != 0)
                Debug.WriteLine("[ZenWindow] ✗ SWCA fallback also FAILED");
        }
    }

    private static int TrySwca(IntPtr handle, AccentState state, uint gradientColor)
    {
        var accent = new AccentPolicy
        {
            AccentState = state,
            AccentFlags = (int)(AccentFlags.DrawAllBorders | AccentFlags.PostNotBottom),
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
        catch (Exception ex)
        {
            Debug.WriteLine($"[ZenWindow] SWCA exception: {ex.Message}");
            return -1;
        }
        finally
        {
            Marshal.FreeHGlobal(data.Data);
        }
    }

    // ======================== Window Procedure ========================
    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    private const uint WM_ERASEBKGND = 0x0014;
    private const uint WM_WINDOWPOSCHANGING = 0x0046;

    private static IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        switch (msg)
        {
            case WM_ERASEBKGND:
                // Return TRUE = we handled it → don't paint background → DWM backdrop visible
                return new IntPtr(1);

            case WM_WINDOWPOSCHANGING:
                // Don't let anything mess with our z-order
                break;
        }

        return DefWindowProc(hWnd, msg, wParam, lParam);
    }
}
