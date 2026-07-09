using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace QuickNotes;

/// <summary>
/// Maximized backdrop window used in Zen mode.
/// Renders the native Windows acrylic effect and sits behind NoteWindow.
/// NoteWindow's transparent areas reveal this window's blurred content.
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

    private enum WindowCompositionAttribute
    {
        WCA_ACCENT_POLICY = 19
    }

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

    public ZenWindow()
    {
        InitializeComponent();
        SourceInitialized += OnSourceInitialized;
    }

    private bool _allowClose;

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        EnableAcrylic();
    }

    /// <summary>
    /// Force-close without cancellation (called by NoteWindow during app shutdown).
    /// </summary>
    public void ForceClose()
    {
        _allowClose = true;
        Close();
    }

    /// <summary>
    /// Applies the native acrylic blur + dark tint to this window.
    /// Uses a 38% dark tint so NoteWindow's card content pops clearly.
    /// </summary>
    private void EnableAcrylic()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero) return;

        // GradientColor format: 0xAARRGGBB  (alpha, red, green, blue)
        // 0x66111111 ≈ 40% black tint — dark enough for contrast, blur shows through
        var accent = new AccentPolicy
        {
            AccentState = AccentState.ACCENT_ENABLE_ACRYLICBLURBEHIND,
            AccentFlags = 0,
            GradientColor = 0x66111111,
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
            SetWindowCompositionAttribute(handle, ref data);
        }
        finally
        {
            Marshal.FreeHGlobal(data.Data);
        }
    }

    /// <summary>
    /// Prevents accidental close (e.g. Alt+F4). Use Hide() instead.
    /// </summary>
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
