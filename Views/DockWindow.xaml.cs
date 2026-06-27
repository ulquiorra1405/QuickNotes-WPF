using System;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using QuickNotes.Models;

namespace QuickNotes.Views;

public partial class DockWindow : Window
{
    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReleaseCapture();

    private const int WM_NCLBUTTONDOWN = 0xA1;
    private const int HTCAPTION = 2;

    // Drag-vs-click detection
    private Point? _dragStartPoint;
    private const double DragThreshold = 6;

    // Reorder state
    private Guid _reorderNoteId;
    private bool _isReorderDragging;
    private Border? _dragSourceBorder;
    private int _dragHoverSlot = -1;


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

        // Create an unfrozen brush for hover-to-opaque animation
        _dockBgBrush = new SolidColorBrush(Color.FromArgb(0x4C, 0x1A, 0x1A, 0x1A));
        dockBorder.Background = _dockBgBrush;

        // Position: right edge of the correct monitor, vertically centered
        Left = monitorBounds.Right - 70;
        Top = monitorBounds.Top + (monitorBounds.Height - 300) / 2;
    }

    private void DockWindow_Loaded(object sender, RoutedEventArgs e)
    {
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

        int count = items.Count;
        double h = 8 + Math.Min(count, 9) * 38 + 32 + 8;
        Height = Math.Max(h, 80);
    }

    // ── Drag to reposition (P/Invoke for WindowStyle=None) ──

    private void DockBorder_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;

        // Don't start drag if clicking on a note icon, the exit button, or the scroll viewer
        var src = e.OriginalSource as DependencyObject;
        if (src != null && FindParent<Border>(src, note => note.Tag is Guid) != null) return;
        if (src != null && FindParent<Button>(src) != null) return;
        if (src != null && FindParent<ScrollViewer>(src) != null) return;

        // Use WM_NCLBUTTONDOWN / HTCAPTION to simulate title bar drag
        // (DragMove() doesn't work with WindowStyle=None + AllowsTransparency=True)
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero)
        {
            ReleaseCapture();
            SendMessage(hwnd, WM_NCLBUTTONDOWN, HTCAPTION, 0);
        }
    }

    private static T? FindParent<T>(DependencyObject? child, Func<T, bool>? predicate = null) where T : DependencyObject
    {
        while (child != null)
        {
            if (child is T t && (predicate == null || predicate(t)))
                return t;
            child = VisualTreeHelper.GetParent(child);
        }
        return null;
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

    // ── Note opening (click) + reorder (drag with live ghost) ──

    private Border? FindNoteIcon(DependencyObject? el)
    {
        while (el != null)
        {
            if (el is Border b && b.Tag is Guid) return b;
            el = VisualTreeHelper.GetParent(el);
        }
        return null;
    }

    private ContentPresenter? FindNoteContainer(Guid noteId)
    {
        var source = notesList.ItemsSource as System.Collections.IList;
        if (source == null) return null;
        foreach (var item in source)
        {
            if (item is DockNoteItem di && di.NoteId == noteId)
                return notesList.ItemContainerGenerator.ContainerFromItem(item) as ContentPresenter;
        }
        return null;
    }

    private void DockScroller_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var border = FindNoteIcon(e.OriginalSource as DependencyObject);
        if (border?.Tag is not Guid noteId) return;

        _dragStartPoint = e.GetPosition(dockScroller);
        _reorderNoteId = noteId;
        _isReorderDragging = false;
        dockScroller.CaptureMouse();
        e.Handled = true;
    }

    private void DockScroller_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragStartPoint == null) return;
        var pos = e.GetPosition(dockScroller);

        if (_isReorderDragging)
        {
            UpdateDragVisual(pos);
            e.Handled = true;
            return;
        }

        if ((pos - _dragStartPoint.Value).Length > DragThreshold)
        {
            _isReorderDragging = true;
            ShowDragGhost();
            UpdateDragVisual(pos);
        }
    }

    private void ShowDragGhost()
    {
        var container = FindNoteContainer(_reorderNoteId);
        if (container == null) return;

        _dragSourceBorder = FindBorderInContainer(container, _reorderNoteId);
        if (_dragSourceBorder == null) return;

        dragGhost.Width = Math.Max(_dragSourceBorder.ActualWidth, 32);
        dragGhost.Height = Math.Max(_dragSourceBorder.ActualHeight, 32);
        dragGhost.Fill = _dragSourceBorder.Background;
        dragGhost.Opacity = 0.85;
        dragGhost.Visibility = Visibility.Visible;

        var ghostStart = container.TransformToAncestor(this).Transform(new Point(0, 0));
        Canvas.SetLeft(dragGhost, this.ActualWidth / 2 - 16);
        Canvas.SetTop(dragGhost, ghostStart.Y);

        _dragSourceBorder.Visibility = Visibility.Collapsed;
    }

    private static Border? FindBorderInContainer(DependencyObject parent, Guid noteId)
    {
        int count = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is Border b && b.Tag is Guid id && id == noteId)
                return b;
            var found = FindBorderInContainer(child, noteId);
            if (found != null) return found;
        }
        return null;
    }

    private void UpdateDragVisual(Point pos)
    {
        var items = _store.Notes.OrderBy(n => n.Order).ToList();
        int count = items.Count;
        int slot = (int)(pos.Y / 38);
        slot = Math.Clamp(slot, 0, count);

        if (_dragHoverSlot != slot)
        {
            _dragHoverSlot = slot;
            ApplyItemShifts(slot, items);
        }

        Canvas.SetTop(dragGhost, pos.Y - 16);
    }

    private void ApplyItemShifts(int slot, System.Collections.Generic.List<Note> orderedNotes)
    {
        var source = notesList.ItemsSource as System.Collections.IList;
        if (source == null) return;

        int srcIdx = orderedNotes.FindIndex(n => n.Id == _reorderNoteId);
        if (srcIdx < 0) return;
        if (slot == srcIdx)
        {
            ClearItemShifts();
            return;
        }

        if (slot > srcIdx)
        {
            // Dragging DOWN: items between srcIdx+1 and slot shift UP to close the source gap
            for (int i = 0; i < source.Count; i++)
            {
                if (i > srcIdx && i <= slot)
                    SetItemShift(i, -38);
                else
                    SetItemShift(i, 0);
            }
        }
        else
        {
            // Dragging UP: items from slot to srcIdx-1 shift DOWN to open a gap at target
            for (int i = 0; i < source.Count; i++)
            {
                if (i >= slot && i < srcIdx)
                    SetItemShift(i, 38);
                else
                    SetItemShift(i, 0);
            }
        }
    }

    private void ClearItemShifts()
    {
        var source = notesList.ItemsSource as System.Collections.IList;
        if (source == null) return;
        for (int i = 0; i < source.Count; i++)
            SetItemShift(i, 0);
    }

    private void SetItemShift(int index, double shiftY)
    {
        var item = notesList.ItemsSource is System.Collections.IList list && index < list.Count
            ? list[index] : null;
        if (item == null) return;
        var container = notesList.ItemContainerGenerator.ContainerFromItem(item) as ContentPresenter;
        if (container == null) return;

        var transform = container.RenderTransform as TranslateTransform;
        if (transform == null)
        {
            transform = new TranslateTransform();
            container.RenderTransform = transform;
        }
        transform.Y = shiftY;
    }

    private void DockScroller_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        dockScroller.ReleaseMouseCapture();

        // Hide ghost
        dragGhost.Visibility = Visibility.Collapsed;
        ClearItemShifts();
        _dragHoverSlot = -1;
        _dragSourceBorder = null;

        if (_isReorderDragging)
        {
            _isReorderDragging = false;

            var pos = e.GetPosition(dockScroller);
            int slot = (int)(pos.Y / 38);
            var notes = _store.Notes.OrderBy(n => n.Order).ToList();
            int count = notes.Count;
            slot = Math.Clamp(slot, 0, count - 1);

            var srcIndex = notes.FindIndex(n => n.Id == _reorderNoteId);
            if (srcIndex >= 0 && srcIndex != slot)
            {
                int insertAt = slot;
                if (srcIndex < slot) insertAt--;

                var note = notes[srcIndex];
                notes.RemoveAt(srcIndex);
                notes.Insert(insertAt, note);

                var obs = _store.Notes;
                obs.Clear();
                foreach (var n in notes)
                {
                    obs.Add(n);
                    n.Order = obs.Count - 1;
                }

                _store.Save();
                RefreshNotes();
            }

            _dragStartPoint = null;
            return;
        }

        // Was a click, not drag
        if (FindNoteIcon(e.OriginalSource as DependencyObject) is Border border && border.Tag is Guid noteId)
        {
            if (e.ClickCount >= 2)
            {
                var note = _store.Notes.FirstOrDefault(n => n.Id == noteId);
                if (note == null) { _dragStartPoint = null; return; }

                var existing = Application.Current.Windows.OfType<NoteWindow>()
                    .FirstOrDefault(w => w.DataContext is Note n && n.Id == noteId);

                if (existing != null)
                {
                    if (!existing.IsVisible) { existing.Show(); existing.Focus(); }
                    NoteWindow.ResetToDefaultPosition(existing, Left);
                }
                else OpenNote(noteId, forceDefault: true);
            }
            else
            {
                OpenNote(noteId);
            }
        }

        _dragStartPoint = null;
    }


    private void OpenNote(Guid noteId, bool forceDefault = false)
    {
        var note = _store.Notes.FirstOrDefault(n => n.Id == noteId);
        if (note == null) return;

        var existing = Application.Current.Windows.OfType<NoteWindow>()
            .FirstOrDefault(w => w.DataContext is Note n && n.Id == noteId);

        if (existing != null)
        {
            if (!existing.IsVisible)
            {
                if (!forceDefault)
                {
                    if (!double.IsNaN(note.WinLeft) && note.WinLeft > 0) existing.Left = note.WinLeft;
                    if (!double.IsNaN(note.WinTop) && note.WinTop > 0) existing.Top = note.WinTop;
                }
                existing.Show();
                existing.Focus();
                if (forceDefault)
                    NoteWindow.ResetToDefaultPosition(existing, Left);
            }
            else
            {
                if (forceDefault)
                    NoteWindow.ResetToDefaultPosition(existing, Left);
                else
                {
                    existing.Left = Left - existing.Width - 10;
                    existing.Top = Top;
                }
                existing.Focus();
            }
        }
        else
        {
            var win = new NoteWindow(note, _store);
            if (forceDefault)
                NoteWindow.ResetToDefaultPosition(win, Left);
            else
            {
                win.Left = Left - 350;
                win.Top = Top;
            }
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
    public Brush BgColor { get; }
    public Brush FgColor { get; }
    public Brush TtBgColor { get; }
    public Brush TtFgColor { get; }
    public double OpenIndicatorOpacity { get; private set; }
    public double MinIndicatorOpacity { get; private set; }
    public double IconSize { get; private set; }
    public double IconOpacity { get; private set; }

    public DockNoteItem(Note note, NotesStore store)
    {
        NoteId = note.Id;
        FullTitle = string.IsNullOrWhiteSpace(note.Title) ? "(sin título)" : note.Title;

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
        bool isDark = IsDark(bgColor);
        FgColor = new SolidColorBrush(isDark ? Colors.White : Color.FromArgb(0xCC, 0x1A, 0x1A, 0x1A));
        TtBgColor = new SolidColorBrush(bgColor);
        TtFgColor = new SolidColorBrush(isDark ? Colors.White : Color.FromArgb(0xCC, 0x1A, 0x1A, 0x1A));

        RefreshState();
    }

    public void RefreshState()
    {
        var existingWin = Application.Current.Windows.OfType<NoteWindow>()
            .FirstOrDefault(w => w.DataContext is Note n && n.Id == NoteId);

        bool isOpen = existingWin != null && existingWin.IsVisible;
        bool isMinimized = existingWin != null && !existingWin.IsVisible;

        if (isOpen)
        {
            OpenIndicatorOpacity = 1;
            MinIndicatorOpacity = 0;
            IconSize = 32;
            IconOpacity = 1.0;
        }
        else if (isMinimized)
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
