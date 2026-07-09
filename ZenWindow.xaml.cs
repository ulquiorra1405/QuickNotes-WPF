using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace QuickNotes;

/// <summary>
/// Maximized backdrop window used in Zen mode.
/// Renders the native Windows acrylic/blur effect and sits behind NoteWindow.
/// NoteWindow's transparent areas reveal this window's blurred content.
/// </summary>
public partial class ZenWindow : Window
{
    // --- SetWindowCompositionAttribute (acrylic/blur) ---
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

    // --- Monitor helpers for multi-monitor ---
    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);
    private const uint MONITOR_DEFAULTTONEAREST = 2;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOACTIVATE = 0x0010;
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MONITORINFO
    {
        public uint cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    // --- Custom helper for DWM blur behind (alternative on some builds) ---
    [DllImport("dwmapi.dll")]
    private static extern int DwmEnableBlurBehindWindow(IntPtr hwnd, ref DWM_BLURBEHIND blurBehind);

    [StructLayout(LayoutKind.Sequential)]
    private struct DWM_BLURBEHIND
    {
        public uint dwFlags;
        public bool fEnable;
        public IntPtr hRgnBlur;
        public bool fTransitionOnMaximized;
    }
    private const uint DWM_BB_ENABLE = 1;

    private bool _allowClose;
    private readonly IntPtr _noteHandle;

    public ZenWindow(IntPtr noteWindowHandle)
    {
        _noteHandle = noteWindowHandle;
        InitializeComponent();
        SourceInitialized += OnSourceInitialized;
    }

    /// <summary>
    /// Shows ZenWindow on the same monitor as NoteWindow, applies native blur,
    /// and places it behind NoteWindow in z-order.
    /// </summary>
    public void ShowBehindNote()
    {
        // Move to the correct monitor before showing
        var mi = new MONITORINFO { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
        var monitor = MonitorFromWindow(_noteHandle, MONITOR_DEFAULTTONEAREST);
        bool gotMonitor = GetMonitorInfo(monitor, ref mi);
        if (gotMonitor)
        {
            Left = mi.rcWork.Left;
            Top = mi.rcWork.Top;
            Width = mi.rcWork.Right - mi.rcWork.Left;
            Height = mi.rcWork.Bottom - mi.rcWork.Top;
        }

        Show();

        // Place directly behind NoteWindow in z-order
        var zenHandle = new WindowInteropHelper(this).Handle;
        SetWindowPos(zenHandle, _noteHandle, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }

    /// <summary>
    /// Force-close without cancellation (called by NoteWindow during app shutdown).
    /// </summary>
    public void ForceClose()
    {
        _allowClose = true;
        Close();
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;

        // Try accent states in order: 4→5→3→(DwmEnableBlurBehind)
        if (!TrySetAccent(handle, AccentState.ACCENT_ENABLE_ACRYLICBLURBEHIND, 0x66111111) &&
            !TrySetAccent(handle, AccentState.ACCENT_ENABLE_HOSTBACKDROP, 0x66111111) &&
            !TrySetAccent(handle, AccentState.ACCENT_ENABLE_BLURBEHIND, 0))
        {
            // Last resort: DWM blur behind (no tint, just blur)
            var bb = new DWM_BLURBEHIND
            {
                dwFlags = DWM_BB_ENABLE,
                fEnable = true,
                hRgnBlur = IntPtr.Zero,
                fTransitionOnMaximized = true
            };
            DwmEnableBlurBehindWindow(handle, ref bb);
        }
    }

    private bool TrySetAccent(IntPtr handle, AccentState state, uint gradientColor)
    {
        var accent = new AccentPolicy
        {
            AccentState = state,
            AccentFlags = 0,
            GradientColor = gradientColor,
            AnimationId = 0
        };

        var data = new WindowCompositionAttributeData
        {
            Attribute = WindowCompositionAttribute.WCA_ACCENT_POLICY,
            Data = Marshal.AllocHGlobal(Marshal.SizeOf(accent)),
            SizeOfData = Marshal.SizeOf(accent)
        };

        try
        {
            Marshal.StructureToPtr(accent, data.Data, false);
            int result = SetWindowCompositionAttribute(handle, ref data);
            return result == 0; // 0 = success
        }
        catch
        {
            return false;
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
