using System;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using QuickNotes.Models;

namespace QuickNotes.Views;

public partial class DockWindow : Window
{
    private readonly NotesStore _store;
    private readonly Action _onExit;
    private readonly Rect _monitorBounds;
    private readonly SolidColorBrush _dockBgBrush;

    public DockWindow(NotesStore store, Rect monitorBounds, Action onExit)
    {
        InitializeComponent();
        _store = store;
        _monitorBounds = monitorBounds;
        _onExit = onExit;
        AnimationHelper.Enabled = _store.AnimationsEnabled;

        // Create an unfrozen brush so we can animate its alpha on hover
        _dockBgBrush = new SolidColorBrush(Color.FromArgb(0x4C, 0x1A, 0x1A, 0x1A));
        dockBorder.Background = _dockBgBrush;

        // Position: right edge of the correct monitor, vertically centered
        Left = monitorBounds.Right - 70;
        Top = monitorBounds.Top + (monitorBounds.Height - 300) / 2;
    }

    private void DockWindow_Loaded(object sender, RoutedEventArgs e)
    {
        // Fade in from opacity 0 (set in XAML)
        dockBorder.BeginAnimation(OpacityProperty, AnimationHelper.MakeAnimation(0, 1, 200));
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

        // Auto-size height: up to 6 items + button row
        int count = items.Count;
        double h = 8 + Math.Min(count, 6) * 38 + 32 + 8;
        Height = Math.Max(h, 80);
    }

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
                // Restore minimized
                if (!double.IsNaN(note.WinLeft) && note.WinLeft > 0) existing.Left = note.WinLeft;
                if (!double.IsNaN(note.WinTop) && note.WinTop > 0) existing.Top = note.WinTop;
                existing.Show();
                existing.Focus();
            }
            else
            {
                // Reposition next to dock
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
    public Brush BgColor { get; }
    public Brush FgColor { get; }
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

        // Tooltip: title + last modified
        var dateStr = note.LastModified.Date == DateTime.Today
            ? note.LastModified.ToString("t")
            : note.LastModified.ToString("d");
        TooltipContent = $"{FullTitle}\n{dateStr}";

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
        FgColor = new SolidColorBrush(IsDark(bgColor) ? Colors.White : Color.FromArgb(0xCC, 0x1A, 0x1A, 0x1A));

        // State indicator
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
