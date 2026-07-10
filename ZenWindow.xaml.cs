using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace QuickNotes;

/// <summary>
/// WPF backdrop window for Zen mode.
/// Uses SetWindowCompositionAttribute for native Windows blur on a layered window.
/// 
/// Strategy:
///   1. ACCENT_ENABLE_BLURBEHIND (3) — pure blur, no tint (works on layered windows, even on 24H2)
///   2. WPF TintOverlay on top provides the dark frosted tint
///   3. Fallbacks for systems where blur behind doesn't work
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

    private bool _backdropInitialized;
    private bool _allowClose;
    private readonly IntPtr _noteHandle;

    public ZenWindow(IntPtr noteWindowHandle)
    {
        _noteHandle = noteWindowHandle;
        InitializeComponent();
    }

    public void ShowBehindNote()
    {
        var handle = new WindowInteropHelper(this).Handle;

        // Init backdrop once
        if (!_backdropInitialized)
        {
            TryBackdropBlur(handle);
            _backdropInitialized = true;
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

        // Behind NoteWindow in z-order
        SetWindowPos(handle, _noteHandle, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }

    public void ForceClose()
    {
        _allowClose = true;
        Close();
    }

    /// <summary>
    /// Try accent states in order: BLURBEHIND (3, pure blur + no tint) →
    /// ACRYLICBLURBEHIND (4, blur+tint) → HOSTBACKDROP (5) → TRANSPARENTGRADIENT (2).
    /// 
    /// With BLURBEHIND we get native OS blur; the WPF TintOverlay provides the tint.
    /// </summary>
    private void TryBackdropBlur(IntPtr handle)
    {
        Debug.WriteLine($"[ZenWindow] Applying backdrop, build {Environment.OSVersion.Version}");

        // --- PRIORITY: pure blur (3) + our own tint overlay ---
        // BLURBEHIND works on layered windows and gives a crisp blur even on 24H2
        var states = new[]
        {
            (State: AccentState.ACCENT_ENABLE_BLURBEHIND, Color: 0u, Name: "BLURBEHIND"),
            (State: AccentState.ACCENT_ENABLE_ACRYLICBLURBEHIND, Color: 0x55_11_11_11u, Name: "ACRYLICBLURBEHIND"),
            (State: AccentState.ACCENT_ENABLE_HOSTBACKDROP, Color: 0x55_11_11_11u, Name: "HOSTBACKDROP"),
            (State: AccentState.ACCENT_ENABLE_TRANSPARENTGRADIENT, Color: 0x88_11_11_11u, Name: "TRANSPARENTGRADIENT"),
        };

        int hr = -1;
        int succeededIndex = -1;

        for (int i = 0; i < states.Length; i++)
        {
            var (state, color, name) = states[i];
            hr = SetAccent(handle, state, color, 0);
            Debug.WriteLine($"[ZenWindow] {name} → 0x{hr:X8}");
            if (hr == 0)
            {
                succeededIndex = i;
                Debug.WriteLine($"[ZenWindow] Using {name}");
                break;
            }
        }

        if (succeededIndex == -1)
            Debug.WriteLine("[ZenWindow] ALL accent states FAILED");
    }

    private static int SetAccent(IntPtr handle, AccentState state, uint gradientColor, int accentFlags)
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
        catch (Exception ex)
        {
            Debug.WriteLine($"[ZenWindow] Exception: {ex.Message}");
            return Marshal.GetHRForException(ex);
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
