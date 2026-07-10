using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace QuickNotes;

/// <summary>
/// WPF layered backdrop window for Zen mode.
///
/// Architecture:
///   1. WPF Window with AllowsTransparency=True → WS_EX_LAYERED with per-pixel alpha
///   2. TintOverlay Rectangle (semi-transparent black) fills the entire window,
///      providing alpha pixels so DWM renders the SWCA blur effect on them
///   3. ACCENT_ENABLE_BLURBEHIND applied via SWCA in SourceInitialized
///   4. Positioned behind NoteWindow on the same monitor
///   5. No focus stealing
/// </summary>
public partial class ZenWindow : Window
{
    // ======================== SWCA ========================
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
        ACCENT_ENABLE_HOSTBACKDROP = 5,
    }

    // ======================== Win32 ========================
    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    private const int SW_HIDE = 0;
    private const int SW_SHOWNA = 8;

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int X, int Y, int cx, int cy, uint uFlags);
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
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

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr hMonitor, int dpiType, out uint dpiX, out uint dpiY);
    private const int MDT_EFFECTIVE_DPI = 0;

    // ======================== DWM ========================
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    // ======================== Fields ========================
    private readonly IntPtr _noteHandle;
    private bool _swcaApplied;
    private bool _hasBeenShown;

    public ZenWindow(IntPtr noteWindowHandle)
    {
        _noteHandle = noteWindowHandle;
        InitializeComponent();

        // Subscribe early — before anything touches the HWND
        SourceInitialized += OnSourceInitialized;

        // Force HWND creation now while we control the flow
        _ = new WindowInteropHelper(this).Handle;
        Debug.WriteLine("[ZenWindow] Constructor: HWND created");
    }

    public void ShowBehindNote()
    {
        Debug.WriteLine("[ZenWindow] === ShowBehindNote ===");

        if (_hasBeenShown)
        {
            // Already created — just reposition and show
            var hwnd = new WindowInteropHelper(this).Handle;
            ShowWindow(hwnd, SW_SHOWNA);
            SetWindowPos(hwnd, _noteHandle, 0, 0, 0, 0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
            Debug.WriteLine("[ZenWindow] Shown again");
            return;
        }

        // First time: position via Win32 before WPF measures, then show
        var helper = new WindowInteropHelper(this);

        // Set the WPF window position for the SourceInitialized callback
        var hMonitor = MonitorFromWindow(_noteHandle, MONITOR_DEFAULTTONEAREST);
        var mi = new MONITORINFO { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
        GetMonitorInfo(hMonitor, ref mi);

        int width = mi.rcMonitor.Right - mi.rcMonitor.Left;
        int height = mi.rcMonitor.Bottom - mi.rcMonitor.Top;
        Debug.WriteLine($"[ZenWindow] Placing at ({mi.rcMonitor.Left},{mi.rcMonitor.Top}) size={width}x{height}");

        // Set WPF properties so the initial layout uses correct size
        Left = mi.rcMonitor.Left;
        Top = mi.rcMonitor.Top;
        Width = width;
        Height = height;

        // Show the window
        ShowWindow(helper.Handle, SW_SHOWNA);

        // Position behind NoteWindow (z-order)
        SetWindowPos(helper.Handle, _noteHandle, 0, 0, 0, 0,
            SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);

        // Apply blur AFTER WPF has rendered the tint overlay
        // (Dispatcher.Background priority = after layout/render)
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background,
            new Action(() =>
            {
                Debug.WriteLine("[ZenWindow] Dispatcher callback: applying blur");
                ApplyBlur(helper.Handle);
            }));

        _hasBeenShown = true;
        Debug.WriteLine("[ZenWindow] Window shown");
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        Debug.WriteLine("[ZenWindow] SourceInitialized");

        if (_swcaApplied) return;

        var hwnd = new WindowInteropHelper(this).Handle;

        // Apply DWM dark mode early
        int dark = 1;
        DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
        Debug.WriteLine($"[ZenWindow] DARK_MODE set early");

        // Store HWND for later use
    }

    public void Hide()
    {
        Debug.WriteLine("[ZenWindow] Hide");
        var helper = new WindowInteropHelper(this);
        ShowWindow(helper.Handle, SW_HIDE);
    }

    public void ForceClose()
    {
        Debug.WriteLine("[ZenWindow] ForceClose");
        Close();
    }

    private void ApplyBlur(IntPtr hwnd)
    {
        Debug.WriteLine("[ZenWindow] === ApplyBlur (WPF layered, with content) ===");

        var stateNames = new Dictionary<int, string>
        {
            {1, "GRADIENT"}, {2, "TRANSPARENTGRADIENT"}, {3, "BLURBEHIND"},
            {4, "ACRYLICBLURBEHIND"}, {5, "HOSTBACKDROP"},
        };
        int[] flagSets = { 0x00, 0x20, 0x40, 0x20 | 0x40 };

        // Try all state+flag combos
        foreach (int state in new[] { 1, 2, 3, 4, 5 })
        {
            foreach (int flags in flagSets)
            {
                uint grad = (state == 4) ? 0x55111111u : 0u;
                int hr = TrySwca(hwnd, state, flags, grad);
                string name = stateNames[state];
                Debug.WriteLine($"[ZenWindow] SWCA {name,-25} flags=0x{flags:X2} → 0x{hr:X8} {(hr == 0 ? "✓" : "✗")}");
            }
        }

        // Apply best working: BLURBEHIND > HOSTBACKDROP > ACRYLIC > TRANSPARENT
        foreach (int prefState in new[] { 3, 5, 4, 2 })
        {
            foreach (int flags in flagSets)
            {
                uint gc = (prefState == 4) ? 0x55111111u : 0u;
                int hr = TrySwca(hwnd, prefState, flags, gc);
                if (hr == 0)
                {
                    Debug.WriteLine($"[ZenWindow] ✓ Applied: {stateNames[prefState]} flags=0x{flags:X2}");
                    TrySwca(hwnd, prefState, flags, gc); // apply again to be sure
                    _swcaApplied = true;
                    return;
                }
            }
        }

        Debug.WriteLine("[ZenWindow] ✗ ALL accent states failed");
    }

    private int TrySwca(IntPtr hwnd, int state, int accentFlags, uint gradientColor)
    {
        var accent = new AccentPolicy
        {
            AccentState = (AccentState)state,
            AccentFlags = accentFlags,
            GradientColor = gradientColor,
            AnimationId = 0,
        };

        var data = new WindowCompositionAttributeData
        {
            Attribute = WindowCompositionAttribute.WCA_ACCENT_POLICY,
            Data = Marshal.AllocHGlobal(Marshal.SizeOf<AccentPolicy>()),
            SizeOfData = Marshal.SizeOf<AccentPolicy>(),
        };

        try
        {
            Marshal.StructureToPtr(accent, data.Data, false);
            return SetWindowCompositionAttribute(hwnd, ref data);
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
}
