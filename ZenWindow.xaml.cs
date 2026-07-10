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
///   2. ACCENT_ENABLE_BLURBEHIND applied via SWCA after window is ready
///   3. TintOverlay (semi-transparent black Rectangle in XAML) provides the dark tint
///      on top of the blurred desktop
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
    private const uint SWP_SHOWWINDOW = 0x0040;
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

    public ZenWindow(IntPtr noteWindowHandle)
    {
        _noteHandle = noteWindowHandle;
        InitializeComponent();
    }

    public void ShowBehindNote()
    {
        Debug.WriteLine("[ZenWindow] === ShowBehindNote ===");

        var helper = new WindowInteropHelper(this);
        IntPtr hwnd = helper.Handle; // forces creation of the Hwnd

        // Position on NoteWindow's monitor BEFORE showing
        var hMonitor = MonitorFromWindow(_noteHandle, MONITOR_DEFAULTTONEAREST);
        var mi = new MONITORINFO { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
        GetMonitorInfo(hMonitor, ref mi);

        int width = mi.rcMonitor.Right - mi.rcMonitor.Left;
        int height = mi.rcMonitor.Bottom - mi.rcMonitor.Top;
        Debug.WriteLine($"[ZenWindow] Placing at ({mi.rcMonitor.Left},{mi.rcMonitor.Top}) size={width}x{height}");

        // Set size/position via SetWindowPos (bypasses WPF layout for speed)
        SetWindowPos(hwnd, _noteHandle,
            mi.rcMonitor.Left, mi.rcMonitor.Top,
            width, height,
            SWP_NOACTIVATE | SWP_SHOWWINDOW | SWP_FRAMECHANGED);

        // Show without activating
        ShowWindow(hwnd, SW_SHOWNA);

        // Apply SWCA blur NOW, after the window is visible
        if (!_swcaApplied)
        {
            ApplySystemBlur(hwnd);
        }

        // Force WPF to re-render the tint overlay on top of the blur
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background,
            new Action(() => TintOverlay.InvalidateVisual()));

        Debug.WriteLine("[ZenWindow] Window shown and blur applied");
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

    private void ApplySystemBlur(IntPtr hwnd)
    {
        Debug.WriteLine("[ZenWindow] === ApplySystemBlur (WPF layered) ===");

        // Force DWM dark mode on this window for deep dark appearance
        int dark = 1;
        DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
        Debug.WriteLine($"[ZenWindow] DARK_MODE → dark mode set");

        // ---- Try all accent states and log ----
        var stateNames = new Dictionary<int, string>
        {
            {1, "GRADIENT"}, {2, "TRANSPARENTGRADIENT"}, {3, "BLURBEHIND"},
            {4, "ACRYLICBLURBEHIND"}, {5, "HOSTBACKDROP"},
        };
        int[] flagSets = { 0x00, 0x20, 0x40, 0x20 | 0x40 };

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

        // ---- Apply best working state ----
        // Preference: BLURBEHIND > HOSTBACKDROP > ACRYLICBLURBEHIND > TRANSPARENTGRADIENT
        foreach (int prefState in new[] { 3, 5, 4, 2 })
        {
            foreach (int flags in flagSets)
            {
                uint gc = (prefState == 4) ? 0x55111111u : 0u;
                int hr = TrySwca(hwnd, prefState, flags, gc);
                if (hr == 0)
                {
                    Debug.WriteLine($"[ZenWindow] ✓ Applied: {stateNames[prefState]} flags=0x{flags:X2}");
                    _swcaApplied = true;
                    return;
                }
            }
        }

        // ---- Fallback: try DISABLED first then BLURBEHIND with no flags ----
        // Sometimes calling ACCENT_DISABLED resets the state and then BLURBEHIND works
        Debug.WriteLine("[ZenWindow] Trying reset+apply pattern...");
        TrySwca(hwnd, 0, 0, 0); // disable first
        int hr2 = TrySwca(hwnd, 3, 0, 0); // BLURBEHIND with no flags
        Debug.WriteLine($"[ZenWindow] SWCA reset+BLURBEHIND(flags=0) → 0x{hr2:X8}");
        if (hr2 == 0)
        {
            _swcaApplied = true;
            return;
        }

        Debug.WriteLine("[ZenWindow] ✗ ALL accent states failed on WPF layered");
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
