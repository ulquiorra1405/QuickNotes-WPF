using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace QuickNotes;

/// <summary>
/// WPF backdrop window for Zen mode.
///
/// Architecture:
///   - WPF Window with AllowsTransparency=True → WS_EX_LAYERED (per-pixel alpha)
///   - ACCENT_ENABLE_BLURBEHIND via SetWindowCompositionAttribute → system blur
///   - WPF Rectangle TintOverlay (#80000000) → dark tint on top of blur
///   - Z-order set behind NoteWindow via SetWindowPos
///   - ShowActivated=False → no focus steal
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

    // ======================== User32 ========================
    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int X, int Y, int cx, int cy, uint uFlags);
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;

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

    // ======================== Fields ========================
    private readonly IntPtr _noteHandle;
    private bool _shown;

    public ZenWindow(IntPtr noteWindowHandle)
    {
        InitializeComponent();
        _noteHandle = noteWindowHandle;
    }

    public void ShowBehindNote()
    {
        Debug.WriteLine("[ZenWindow] === ShowBehindNote ===");

        if (_shown)
        {
            // Already visible — bring it back behind note
            RepositionZOrder();
            return;
        }

        // ---------- Position on NoteWindow's monitor ----------
        var hMonitor = MonitorFromWindow(_noteHandle, MONITOR_DEFAULTTONEAREST);
        var mi = new MONITORINFO { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(hMonitor, ref mi))
            Debug.WriteLine("[ZenWindow] GetMonitorInfo FAILED");

        // Get monitor DPI
        GetDpiForMonitor(hMonitor, MDT_EFFECTIVE_DPI, out uint dpiX, out uint dpiY);
        if (dpiX == 0) dpiX = 96;
        if (dpiY == 0) dpiY = 96;

        // Convert monitor rect to WPF (device-independent) pixels
        double dpiScaleX = dpiX / 96.0;
        double dpiScaleY = dpiY / 96.0;

        double left = mi.rcMonitor.Left / dpiScaleX;
        double top = mi.rcMonitor.Top / dpiScaleY;
        double width = (mi.rcMonitor.Right - mi.rcMonitor.Left) / dpiScaleX;
        double height = (mi.rcMonitor.Bottom - mi.rcMonitor.Top) / dpiScaleY;

        this.Left = left;
        this.Top = top;
        this.Width = width;
        this.Height = height;

        Debug.WriteLine($"[ZenWindow] WPF size: left={left:F1} top={top:F1} {width:F1}x{height:F1} @ dpi={dpiX}x{dpiY}");

        // ---------- Show the window ----------
        // WPF uses SW_SHOWNOACTIVATE internally because ShowActivated=False
        this.Show();

        // ---------- Apply blur via SWCA ----------
        ApplySystemBlur();

        // ---------- Position z-order behind NoteWindow ----------
        RepositionZOrder();

        _shown = true;
        Debug.WriteLine("[ZenWindow] Window shown");
    }

    public void Hide()
    {
        if (_shown)
        {
            this.Visibility = Visibility.Hidden;
            _shown = false;
        }
    }

    public void ForceClose()
    {
        if (this.IsLoaded)
        {
            this.Close();
        }
        _shown = false;
    }

    private void RepositionZOrder()
    {
        var helper = new WindowInteropHelper(this);
        SetWindowPos(helper.Handle, _noteHandle, 0, 0, 0, 0,
            SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE);
    }

    private void ApplySystemBlur()
    {
        var helper = new WindowInteropHelper(this);
        IntPtr hwnd = helper.Handle;

        Debug.WriteLine("[ZenWindow] === DIAGNOSTIC: ALL accent states ===");

        // Try all 6 states (1-5 are effects, 0=disabled)
        // with different flag combos:
        //   0x00 = no flags
        //   0x20 = DrawAllBorders
        //   0x40 = PostNotBottom
        //   0x20|0x40 = both
        int[][] flagSets = new[] {
            new[] { 0 },
            new[] { 0x20 | 0x40 },
            new[] { 0x20 },
            new[] { 0x40 },
        };

        var stateNames = new Dictionary<int, string>
        {
            { 0, "DISABLED" },
            { 1, "GRADIENT" },
            { 2, "TRANSPARENTGRADIENT" },
            { 3, "BLURBEHIND" },
            { 4, "ACRYLICBLURBEHIND" },
            { 5, "HOSTBACKDROP" },
        };

        foreach (int state in new[] { 1, 2, 3, 4, 5 })
        {
            foreach (var flags in flagSets)
            {
                int flagVal = flags[0];
                uint gradColor = (state == 4) ? 0x55111111u : 0u;
                int hr = TryAccent(hwnd, state, flagVal, gradColor);
                string name = stateNames[state];
                Debug.WriteLine($"[ZenWindow] SWCA {name,-25} flags=0x{flagVal:X2} grad=0x{gradColor:X8} → 0x{hr:X8} {(hr == 0 ? "✓" : "✗")}");
            }
        }

        // Now try the BEST working one, in order of preference:
        // BLURBEHIND > ACRYLICBLURBEHIND > HOSTBACKDROP > TRANSPARENTGRADIENT
        foreach (int prefState in new[] { 3, 4, 5, 2 })
        {
            int bestFlags = FlagComboThatWorks(hwnd, prefState);
            if (bestFlags >= 0)
            {
                uint gc = (prefState == 4) ? 0x55111111u : 0u;
                TryAccent(hwnd, prefState, bestFlags, gc);
                Debug.WriteLine($"[ZenWindow] ✓ APPLIED: {stateNames[prefState]} with flags=0x{bestFlags:X2}");
                return;
            }
        }

        Debug.WriteLine("[ZenWindow] ✗ NO working accent state found");
    }

    private static int FlagComboThatWorks(IntPtr hwnd, int state)
    {
        foreach (int flags in new[] { 0, 0x20, 0x40, 0x20 | 0x40 })
        {
            uint gc = (state == 4) ? 0x55111111u : 0u;
            if (TryAccent(hwnd, state, flags, gc) == 0)
                return flags;
        }
        return -1;
    }

    private static int TryAccent(IntPtr hwnd, int stateInt, int accentFlags, uint gradientColor)
    {
        var accent = new AccentPolicy
        {
            AccentState = (AccentState)stateInt,
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
