using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;

namespace QuickNotes.Models;

/// <summary>
/// Información básica de un monitor, obtenida via Win32 API.
/// </summary>
public class MonitorInfo
{
    public string DeviceName { get; init; } = "";
    public bool IsPrimary { get; init; }
    public Rect WorkingArea { get; init; }
}

/// <summary>
/// Helper P/Invoke para información de monitores sin depender de WinForms.
/// </summary>
public static class MonitorHelper
{
    /// <summary>Retorna el monitor donde está un HWND.</summary>
    public static MonitorInfo GetMonitorForHwnd(IntPtr hwnd)
    {
        var hMonitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
        return GetMonitorInfo(hMonitor);
    }

    /// <summary>Retorna todos los monitores activos.</summary>
    public static List<MonitorInfo> GetAllMonitors()
    {
        var monitors = new List<MonitorInfo>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero,
            (hMonitor, _, _, _) =>
            {
                monitors.Add(GetMonitorInfo(hMonitor));
                return true;
            }, IntPtr.Zero);
        return monitors;
    }

    /// <summary>Retorna el monitor primario.</summary>
    public static MonitorInfo GetPrimary()
    {
        var pt = default(POINT);
        var hMonitor = MonitorFromPoint(pt, MONITOR_DEFAULTTONEAREST);
        return GetMonitorInfo(hMonitor);
    }

    /// <summary>
    /// Detecta si un rect (ej. la posición de una ventana) está cerca de
    /// algún borde de un monitor. Retorna "left"/"right"/"top"/"bottom" o null.
    /// </summary>
    public static string? DetectEdge(Rect windowRect, MonitorInfo monitor, double threshold = 35)
    {
        var wa = monitor.WorkingArea;

        double distLeft = Math.Abs(windowRect.Left - wa.Left);
        double distRight = Math.Abs(windowRect.Right - wa.Right);
        double distTop = Math.Abs(windowRect.Top - wa.Top);
        double distBottom = Math.Abs(windowRect.Bottom - wa.Bottom);

        double min = Math.Min(Math.Min(distLeft, distRight), Math.Min(distTop, distBottom));
        if (min > threshold) return null;

        if (min == distLeft) return "left";
        if (min == distRight) return "right";
        if (min == distTop) return "top";
        return "bottom";
    }

    /// <summary>
    /// Encuentra el monitor que contiene un punto dado.
    /// </summary>
    public static MonitorInfo? FindMonitorAt(double x, double y)
    {
        foreach (var m in GetAllMonitors())
        {
            var wa = m.WorkingArea;
            if (x >= wa.Left && x <= wa.Right && y >= wa.Top && y <= wa.Bottom)
                return m;
        }
        return null;
    }

    /// <summary>
    /// Clampa un rect de ventana a la pantalla visible más cercana.
    /// Si la ventana queda completamente fuera de todos los monitores,
    /// la centra en el monitor primario.
    /// </summary>
    public static Rect ClampToScreen(double left, double top, double width, double height)
    {
        // Guard against uninitialized values (new notes, default NaN/zero)
        if (double.IsNaN(width) || width <= 0) width = 380;
        if (double.IsNaN(height) || height <= 0) height = 420;

        var monitors = GetAllMonitors();
        if (monitors.Count == 0)
            return new Rect(0, 0, width, height);

        // If left/top are NaN, center on primary monitor
        if (double.IsNaN(left) || double.IsNaN(top))
        {
            var primary = monitors.Find(m => m.IsPrimary) ?? monitors[0];
            return new Rect(
                primary.WorkingArea.Left + (primary.WorkingArea.Width - width) / 2,
                primary.WorkingArea.Top + (primary.WorkingArea.Height - height) / 2,
                width, height);
        }

        // Find which monitor the window center is on
        double cx = left + width / 2;
        double cy = top + height / 2;
        var target = FindMonitorAt(cx, cy);

        if (target == null)
        {
            // Center of window is off-screen — find nearest monitor
            double bestDist = double.MaxValue;
            foreach (var m in monitors)
            {
                double mx = m.WorkingArea.Left + m.WorkingArea.Width / 2;
                double my = m.WorkingArea.Top + m.WorkingArea.Height / 2;
                double dist = Math.Pow(cx - mx, 2) + Math.Pow(cy - my, 2);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    target = m;
                }
            }

            // Fallback: if still null (e.g. all NaN distances), use primary
            target ??= monitors.Find(m => m.IsPrimary) ?? monitors[0];

            // Center on that monitor
            var wa = target.WorkingArea;
            return new Rect(
                wa.Left + (wa.Width - width) / 2,
                wa.Top + (wa.Height - height) / 2,
                width, height);
        }

        // Window center is on a valid monitor — just clamp
        var work = target.WorkingArea;
        double cl = Math.Max(Math.Min(left, work.Right - width), work.Left);
        double ct = Math.Max(Math.Min(top, work.Bottom - height), work.Top);
        return new Rect(cl, ct, width, height);
    }

    // --- Win32 P/Invoke ---

    private const uint MONITOR_DEFAULTTONEAREST = 2;
    private const uint MONITORINFOF_PRIMARY = 1;

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfoW(IntPtr hMonitor, ref MONITORINFOEX lpmi);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);

    private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, IntPtr lprcMonitor, IntPtr dwData);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int x, y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int left, top, right, bottom; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MONITORINFOEX
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string? szDevice;
    }

    private static MonitorInfo GetMonitorInfo(IntPtr hMonitor)
    {
        var mi = new MONITORINFOEX();
        mi.cbSize = Marshal.SizeOf<MONITORINFOEX>();
        GetMonitorInfoW(hMonitor, ref mi);

        return new MonitorInfo
        {
            DeviceName = (mi.szDevice ?? "").TrimEnd('\0'),
            IsPrimary = (mi.dwFlags & MONITORINFOF_PRIMARY) != 0,
            WorkingArea = new Rect(
                mi.rcWork.left, mi.rcWork.top,
                mi.rcWork.right - mi.rcWork.left,
                mi.rcWork.bottom - mi.rcWork.top)
        };
    }
}
