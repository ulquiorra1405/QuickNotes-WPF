using System;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using QuickNotes.Models;

namespace QuickNotes.Views;

public partial class DockWindow : Window
{
    private readonly NotesStore _store;
    private readonly Action _onExit;
    private readonly Rect _monitorBounds;
    private readonly SolidColorBrush _dockBgBrush;
    private readonly DispatcherTimer _tooltipTimer;
    private Border? _hoveredIcon;

    public DockWindow(NotesStore store, Rect monitorBounds, Action onExit)
    {
        InitializeComponent();
        _store = store;
        _monitorBounds = monitorBounds;
        _onExit = onExit;
        AnimationHelper.Enabled = _store.AnimationsEnabled;

        // Create an unfrozen brush for hover-to-opaque animation
        _dockBgBrush = new SolidColorBrush(Color.FromArgb(0x4C, 0x1A, 0x1A, 0x1A));
        dockBorder.Background = _dockBgBrush;

        // Timer for delayed tooltip hide (avoids flicker when moving to/from popup)
        _tooltipTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(200),
        };
        _tooltipTimer.Tick += TooltipTimer_Tick;

        // Position: right edge of the correct monitor, vertically centered
        Left = monitorBounds.Right - 70;
        Top = monitorBounds.Top + (monitorBounds.Height - 300) / 2;
    }

    private void DockWindow_Loaded(object sender, RoutedEventArgs e)
    {
        dockBorder.BeginAnimation(OpacityProperty, AnimationHelper.MakeAnimation(0, 1, 200));
    }

    private void DockWindow_MouseMove(object sender, MouseEventArgs e)
    {
        // If mouse moves over the dock (but not over any icon), hide tooltip
        if (tooltipPopup.IsOpen && _hoveredIcon == null && !IsMouseOverTooltip())
        {
            StartTooltipHideTimer();
        }
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        RefreshNotes();
    }

    public void RefreshNotes()
    {
        var items = _store.Notes
            .OrderBy(n => n.Order)
            .Select(n => new DockNoteItem(n, _store)).ToList();
        notesList.ItemsSource = items;

        int count = items.Count;
        double h = 8 + Math.Min(count, 6) * 38 + 32 + 8;
        Height = Math.Max(h, 80);
    }

    // ── Note icon hover (custom tooltip) ──

    private void NoteIcon_MouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is not Border border) return;

        _tooltipTimer.Stop();
        _hoveredIcon = border;

        var item = border.DataContext as DockNoteItem;
        if (item == null) return;

        // Update tooltip content and colors (always, before any open/reopen)
        tooltipTitle.Text = item.FullTitle;
        tooltipBorder.Background = border.Background;
        tooltipTitle.Foreground = new SolidColorBrush(item.IsNoteDark ? Colors.White : Color.FromArgb(0xCC, 0x1A, 0x1A, 0x1A));

        // Position the popup to the left of this icon
        // When switching icons while popup is open, close & reopen to force repositioning
        bool needsReopen = tooltipPopup.IsOpen && tooltipPopup.PlacementTarget != border;
        if (needsReopen)
        {
            tooltipPopup.IsOpen = false;
            // Stop any running animations that could cause a flash
            tooltipBorder.BeginAnimation(UIElement.OpacityProperty, null);
            var tt = (TranslateTransform)tooltipBorder.RenderTransform;
            tt.BeginAnimation(TranslateTransform.XProperty, null);
            // Force close to render, then reopen via BeginInvoke
            Dispatcher.BeginInvoke(() =>
            {
                // If mouse already moved to another icon, skip
                if (_hoveredIcon != border) return;
                tooltipPopup.PlacementTarget = border;
                tooltipPopup.HorizontalOffset = -16;
                tooltipPopup.IsOpen = true;
                ResetTooltipAnimation();
                AnimateTooltipIn();
            }, System.Windows.Threading.DispatcherPriority.Render);
            return;
        }

        tooltipPopup.PlacementTarget = border;
        tooltipPopup.HorizontalOffset = -16;

        // Open (or reopen) — animation covers the brief close
        if (!tooltipPopup.IsOpen)
        {
            tooltipPopup.IsOpen = true;
            ResetTooltipAnimation();
        }

        AnimateTooltipIn();
    }

    private void NoteIcon_MouseLeave(object sender, MouseEventArgs e)
    {
        _hoveredIcon = null;
        StartTooltipHideTimer();
    }

    private void Tooltip_MouseEnter(object sender, MouseEventArgs e)
    {
        // Mouse entered the popup itself — keep it open
        _tooltipTimer.Stop();
    }

    private void Tooltip_MouseLeave(object sender, MouseEventArgs e)
    {
        StartTooltipHideTimer();
    }

    private void TooltipTimer_Tick(object? sender, EventArgs e)
    {
        _tooltipTimer.Stop();

        if (_hoveredIcon != null || IsMouseOverTooltip())
            return;

        AnimateTooltipOut(() =>
        {
            tooltipPopup.IsOpen = false;
        });
    }

    private void StartTooltipHideTimer()
    {
        _tooltipTimer.Stop();
        _tooltipTimer.Start();
    }

    private bool IsMouseOverTooltip()
    {
        if (!tooltipPopup.IsOpen || tooltipBorder.Visibility != Visibility.Visible)
            return false;

        var pos = Mouse.GetPosition(tooltipBorder);
        return pos.X >= 0 && pos.X < tooltipBorder.ActualWidth &&
               pos.Y >= 0 && pos.Y < tooltipBorder.ActualHeight;
    }

    // ── Tooltip animations ──

    private void ResetTooltipAnimation()
    {
        tooltipBorder.Opacity = 0;
        var tt = (TranslateTransform)tooltipBorder.RenderTransform;
        tt.X = 6;
    }

    private void AnimateTooltipIn()
    {
        if (!AnimationHelper.Enabled)
        {
            tooltipBorder.Opacity = 1;
            var tt = (TranslateTransform)tooltipBorder.RenderTransform;
            tt.X = 0;
            return;
        }

        var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(120));
        fadeIn.EasingFunction = new QuadraticEase();
        tooltipBorder.BeginAnimation(UIElement.OpacityProperty, fadeIn);

        var slideIn = new DoubleAnimation(6, 0, TimeSpan.FromMilliseconds(120));
        slideIn.EasingFunction = new QuadraticEase();
        var tt2 = (TranslateTransform)tooltipBorder.RenderTransform;
        tt2.BeginAnimation(TranslateTransform.XProperty, slideIn);
    }

    private void AnimateTooltipOut(Action? onComplete = null)
    {
        if (!AnimationHelper.Enabled)
        {
            tooltipBorder.Opacity = 0;
            onComplete?.Invoke();
            return;
        }

        var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(100));
        fadeOut.EasingFunction = new QuadraticEase();
        fadeOut.Completed += (_, _) => onComplete?.Invoke();
        tooltipBorder.BeginAnimation(UIElement.OpacityProperty, fadeOut);

        var slideOut = new DoubleAnimation(0, 6, TimeSpan.FromMilliseconds(100));
        slideOut.EasingFunction = new QuadraticEase();
        var tt = (TranslateTransform)tooltipBorder.RenderTransform;
        tt.BeginAnimation(TranslateTransform.XProperty, slideOut);
    }

    // ── Dock background animation (hover-to-opaque) ──

    private void DockBorder_MouseEnter(object sender, MouseEventArgs e)
    {
        var anim = new ColorAnimation(
            Color.FromArgb(0x4C, 0x1A, 0x1A, 0x1A),
            Color.FromArgb(0xFF, 0x1A, 0x1A, 0x1A),
            AnimationHelper.Dur(200));
        if (AnimationHelper.Enabled) anim.EasingFunction = new QuadraticEase();
        _dockBgBrush.BeginAnimation(SolidColorBrush.ColorProperty, anim);
    }

    private void DockBorder_MouseLeave(object sender, MouseEventArgs e)
    {
        var anim = new ColorAnimation(
            Color.FromArgb(0xFF, 0x1A, 0x1A, 0x1A),
            Color.FromArgb(0x4C, 0x1A, 0x1A, 0x1A),
            AnimationHelper.Dur(200));
        if (AnimationHelper.Enabled) anim.EasingFunction = new QuadraticEase();
        _dockBgBrush.BeginAnimation(SolidColorBrush.ColorProperty, anim);
    }

    // ── Note opening ──

    private void NoteIcon_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed &&
            sender is Border border && border.Tag is Guid noteId)
        {
            OpenNote(noteId);
        }
    }

    private void OpenNote(Guid noteId)
    {
        var note = _store.Notes.FirstOrDefault(n => n.Id == noteId);
        if (note == null) return;

        var existing = Application.Current.Windows.OfType<NoteWindow>()
            .FirstOrDefault(w => w.DataContext is Note n && n.Id == noteId);

        if (existing != null)
        {
            if (!existing.IsVisible)
            {
                if (!double.IsNaN(note.WinLeft) && note.WinLeft > 0) existing.Left = note.WinLeft;
                if (!double.IsNaN(note.WinTop) && note.WinTop > 0) existing.Top = note.WinTop;
                existing.Show();
                existing.Focus();
            }
            else
            {
                existing.Left = Left - existing.Width - 10;
                existing.Top = Top;
                existing.Focus();
            }
        }
        else
        {
            var win = new NoteWindow(note, _store);
            win.Left = Left - 350;
            win.Top = Top;
            win.Show();
            win.Focus();
        }

        RefreshNotes();
    }

    // ── Exit ──

    private void ExitDock_Click(object sender, RoutedEventArgs e)
    {
        if (AnimationHelper.Enabled)
        {
            var fadeOut = AnimationHelper.MakeAnimation(0, 150);
            fadeOut.Completed += (_, _) =>
            {
                Hide();
                _onExit();
            };
            dockBorder.BeginAnimation(OpacityProperty, fadeOut);
        }
        else
        {
            Hide();
            _onExit();
        }
    }

    public void Cleanup()
    {
        Close();
    }
}

/// <summary>
/// ViewModel for a single note icon in the dock.
/// </summary>
public class DockNoteItem : INotifyPropertyChanged
{
    public Guid NoteId { get; }
    public string Label { get; }
    public string FullTitle { get; }
    public string TooltipContent { get; }
    public string TooltipDate { get; }
    public Brush BgColor { get; }
    public Brush FgColor { get; }
    public bool IsNoteDark { get; }
    public double OpenIndicatorOpacity { get; private set; }
    public double MinIndicatorOpacity { get; private set; }
    public double IconSize { get; private set; }
    public double IconOpacity { get; private set; }

    private bool _isMinimized;
    private bool _isOpen;

    public DockNoteItem(Note note, NotesStore store)
    {
        NoteId = note.Id;
        FullTitle = string.IsNullOrWhiteSpace(note.Title) ? "(sin título)" : note.Title;

        // Tooltip text (kept for backward compat) + formatted date for custom tooltip
        var dateStr = note.LastModified.Date == DateTime.Today
            ? note.LastModified.ToString("t")
            : note.LastModified.ToString("d");
        TooltipContent = $"{FullTitle}\n{dateStr}";
        TooltipDate = dateStr;

        // Emoji icon or fallback to 2-letter label
        if (!string.IsNullOrEmpty(note.Icon))
        {
            Label = note.Icon;
        }
        else
        {
            var parts = (note.Title ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
            Label = parts.Length switch
            {
                0 => "📝",
                1 => parts[0].Length >= 2 ? parts[0][..2].ToUpper() : parts[0].ToUpper(),
                _ => (parts[0][..1] + parts[1][..1]).ToUpper()
            };
        }

        // Colors
        var hex = note.Color ?? "#F8F9FA";
        var bgColor = ParseHex(hex);
        BgColor = new SolidColorBrush(bgColor);
        IsNoteDark = IsDark(bgColor);
        FgColor = new SolidColorBrush(IsNoteDark ? Colors.White : Color.FromArgb(0xCC, 0x1A, 0x1A, 0x1A));

        RefreshState();
    }

    public void RefreshState()
    {
        var existingWin = Application.Current.Windows.OfType<NoteWindow>()
            .FirstOrDefault(w => w.DataContext is Note n && n.Id == NoteId);

        _isOpen = existingWin != null && existingWin.IsVisible;
        _isMinimized = existingWin != null && !existingWin.IsVisible;

        if (_isOpen)
        {
            OpenIndicatorOpacity = 1;
            MinIndicatorOpacity = 0;
            IconSize = 32;
            IconOpacity = 1.0;
        }
        else if (_isMinimized)
        {
            OpenIndicatorOpacity = 0;
            MinIndicatorOpacity = 1;
            IconSize = 32;
            IconOpacity = 0.7;
        }
        else
        {
            OpenIndicatorOpacity = 0;
            MinIndicatorOpacity = 0;
            IconSize = 32;
            IconOpacity = 0.5;
        }

        OnPropertyChanged(nameof(OpenIndicatorOpacity));
        OnPropertyChanged(nameof(MinIndicatorOpacity));
        OnPropertyChanged(nameof(IconSize));
        OnPropertyChanged(nameof(IconOpacity));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged(string name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private static Color ParseHex(string hex)
    {
        if (string.IsNullOrEmpty(hex) || hex.Length < 7) return Color.FromRgb(0xF8, 0xF9, 0xFA);
        try { return (Color)ColorConverter.ConvertFromString(hex); }
        catch { return Color.FromRgb(0xF8, 0xF9, 0xFA); }
    }

    private static bool IsDark(Color c) => (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) < 140;
}
