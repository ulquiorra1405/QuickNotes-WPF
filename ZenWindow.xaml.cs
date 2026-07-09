using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;

namespace QuickNotes;

/// <summary>
/// Pure Win32 backdrop window for Zen mode.
/// No WPF rendering — uses SetWindowCompositionAttribute for native acrylic blur.
/// </summary>
public partial class ZenWindow : Window
{
    // --- Acrylic P/Invoke ---
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
        ACCENT_DISABLED,
        ACCENT_ENABLE_GRADIENT,
        ACCENT_ENABLE_TRANSPARENTGRADIENT,
        ACCENT_ENABLE_BLURBEHIND,
        ACCENT_ENABLE_ACRYLICBLURBEHIND,
        ACCENT_ENABLE_HOSTBACKDROP
    }

    // --- Win32 helpers ---
    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);
    private const uint MONITOR_DEFAULTTONEAREST = 2;

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr hMonitor, int dpiType, out uint dpiX, out uint dpiY);
    private const int MDT_EFFECTIVE_DPI = 0;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CreateWindowEx(
        uint dwExStyle, IntPtr lpClassName, IntPtr lpWindowName,
        uint dwStyle, int x, int y, int nWidth, int nHeight,
        IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    private const int SW_HIDE = 0;
    private const int SW_SHOWNOACTIVATE = 4;

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_SHOWWINDOW = 0x0040;

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetModuleHandle(string lpModuleName);

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public uint cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    private IntPtr _hWnd = IntPtr.Zero;
    private bool _shown;
    private readonly IntPtr _noteHandle;

    public ZenWindow(IntPtr noteWindowHandle)
    {
        _noteHandle = noteWindowHandle;
        InitializeComponent();
    }

    /// <summary>
    /// Creates (if needed) and shows the acrylic backdrop on the same monitor as NoteWindow.
    /// </summary>
    public void ShowBehindNote()
    {
        if (_hWnd == IntPtr.Zero)
            CreateBackdrop();

        // Get NoteWindow's monitor
        var hMonitor = MonitorFromWindow(_noteHandle, MONITOR_DEFAULTTONEAREST);
        var mi = new MONITORINFO { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
        GetMonitorInfo(hMonitor, ref mi);

        // Position and show behind NoteWindow in one call
        SetWindowPos(_hWnd, _noteHandle,
            mi.rcMonitor.Left, mi.rcMonitor.Top,
            mi.rcMonitor.Right - mi.rcMonitor.Left,
            mi.rcMonitor.Bottom - mi.rcMonitor.Top,
            SWP_NOACTIVATE | SWP_SHOWWINDOW);

        _shown = true;
    }

    /// <summary>
    /// Hides the backdrop window (can be re-shown later).
    /// </summary>
    public void HideBackdrop()
    {
        if (_hWnd != IntPtr.Zero && _shown)
        {
            ShowWindow(_hWnd, SW_HIDE);
            _shown = false;
        }
    }

    /// <summary>
    /// Destroys the window permanently (called when NoteWindow closes).
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

    private void CreateBackdrop()
    {
        // Register a minimal window class (no background → DWM acrylic visible)
        var hInst = GetModuleHandle(null);
        var className = "QuickNotes_ZenWindow_v2";

        var wndClass = new WNDCLASSEX
        {
            cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
            style = 0,
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate<WndProcDelegate>(WndProc),
            hInstance = hInst,
            hbrBackground = IntPtr.Zero, // NO background brush → acrylic shows through
            lpszClassName = className,
            lpszMenuName = null
        };

        ushort atom = RegisterClassEx(ref wndClass);
        if (atom == 0)
        {
            // If already registered, that's fine — get the atom
            atom = (ushort)GetClassInfoEx(hInst, className, ref wndClass);
        }

        // Create the window: POPUP style, NO layered extensions → acrylic works
        const uint WS_POPUP = 0x80000000u;
        // No WS_EX_LAYERED → SetWindowCompositionAttribute works
        // No WS_EX_TRANSPARENT → we want the window to accept clicks (not through to NoteWindow)
        const uint WS_EX_NOACTIVATE = 0x08000000u;

        _hWnd = CreateWindowEx(
            WS_EX_NOACTIVATE,
            new IntPtr(atom), IntPtr.Zero,
            WS_POPUP,
            0, 0, 0, 0,
            IntPtr.Zero, IntPtr.Zero, hInst, IntPtr.Zero);

        if (_hWnd == IntPtr.Zero)
        {
            int err = Marshal.GetLastWin32Error();
            throw new Win32Exception(err, $"CreateWindowEx failed (0x{err:X8})");
        }

        // Set dark mode for the window frame (affects the acrylic tint)
        int darkMode = 1;
        DwmSetWindowAttribute(_hWnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkMode, sizeof(int));

        // Apply acrylic blur
        ApplyAcrylic();
    }

    private void ApplyAcrylic()
    {
        if (_hWnd == IntPtr.Zero) return;

        // GradientColor: 0xAABBGGRR — dark tint for frosted look
        // 0x66111111 ≈ 40% dark (alpha=102, R=17, G=17, B=17)
        var accent = new AccentPolicy
        {
            AccentState = AccentState.ACCENT_ENABLE_ACRYLICBLURBEHIND,
            AccentFlags = 0,
            GradientColor = 0x66111111,
            AnimationId = 0
        };

        int result = TryApplyAccent(accent);
        if (result != 0)
        {
            // Fallback to blur behind (no tint)
            accent.AccentState = AccentState.ACCENT_ENABLE_BLURBEHIND;
            accent.GradientColor = 0;
            result = TryApplyAccent(accent);
        }
        if (result != 0)
        {
            // Fallback to transparent gradient (just tint, no blur)
            accent.AccentState = AccentState.ACCENT_ENABLE_TRANSPARENTGRADIENT;
            accent.GradientColor = 0x66111111;
            TryApplyAccent(accent);
        }
    }

    private int TryApplyAccent(AccentPolicy accent)
    {
        var data = new WindowCompositionAttributeData
        {
            Attribute = WindowCompositionAttribute.WCA_ACCENT_POLICY,
            Data = Marshal.AllocHGlobal(Marshal.SizeOf(accent)),
            SizeOfData = Marshal.SizeOf(accent)
        };

        try
        {
            Marshal.StructureToPtr(accent, data.Data, false);
            return SetWindowCompositionAttribute(_hWnd, ref data);
        }
        finally
        {
            Marshal.FreeHGlobal(data.Data);
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_hWnd != IntPtr.Zero)
        {
            if (_shown) ShowWindow(_hWnd, SW_HIDE);
            DestroyWindow(_hWnd);
            _hWnd = IntPtr.Zero;
        }
        base.OnClosing(e);
    }

    // --- Window procedure (minimal — just passes to DefWindowProc) ---
    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern ushort RegisterClassEx([In] ref WNDCLASSEX lpWndClass);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern int GetClassInfoEx(IntPtr hInstance, string lpClassName, ref WNDCLASSEX lpWndClass);

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
        public string lpszMenuName;
        public string lpszClassName;
        public IntPtr hIconSm;
    }

    private static readonly WndProcDelegate WndProc = DefWindowProc;
}
