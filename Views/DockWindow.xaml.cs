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
using System.Windows.Media.Effects;
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
    private ContentPresenter? _dragSourceContainer;
    private List<DockNoteItem>? _dragItems;
    // Flag so we don't re-save on drop if the order never changed
    private bool _orderChangedDuringDrag;


    private readonly NotesStore _store;
    private readonly Action _onExit;
    private readonly Rect _monitorBounds;
    private readonly SolidColorBrush _dockBgBrush;
    private Color _dockBgNormal = Color.FromArgb(0x4C, 0x1A, 0x1A, 0x1A);
    private Color _dockBgHover = Color.FromArgb(0xFF, 0x1A, 0x1A, 0x1A);
    private Color _exitBtnHoverBg = Color.FromArgb(0xFF, 0x2A, 0x2A, 0x2A);

    public DockWindow(NotesStore store, Rect monitorBounds, Action onExit)
    {
        InitializeComponent();
        _store = store;
        _monitorBounds = monitorBounds;
        _onExit = onExit;
        AnimationHelper.Enabled = _store.AnimationsEnabled;

        // Create an unfrozen brush for hover-to-opaque animation
        _dockBgBrush = new SolidColorBrush(_dockBgNormal);
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
            .Where(n => !n.IsArchived && !n.IsDeleted)
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
        var anim = new ColorAnimation(_dockBgNormal, _dockBgHover, AnimationHelper.Dur(200));
        if (AnimationHelper.Enabled) anim.EasingFunction = new QuadraticEase();
        _dockBgBrush.BeginAnimation(SolidColorBrush.ColorProperty, anim);
    }

    private void DockBorder_MouseLeave(object sender, MouseEventArgs e)
    {
        var anim = new ColorAnimation(_dockBgHover, _dockBgNormal, AnimationHelper.Dur(200));
        if (AnimationHelper.Enabled) anim.EasingFunction = new QuadraticEase();
        _dockBgBrush.BeginAnimation(SolidColorBrush.ColorProperty, anim);
    }

    private void ExitBtn_MouseEnter(object sender, MouseEventArgs e)
    {
        exitDockBtn.Background = new SolidColorBrush(_exitBtnHoverBg);
    }

    private void ExitBtn_MouseLeave(object sender, MouseEventArgs e)
    {
        exitDockBtn.Background = Brushes.Transparent;
    }

    // ── Note opening (click) + reorder (drag with live ghost + shifts) ──

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

        _dragSourceContainer = container;

        // Find the Border to copy its appearance
        var border = FindBorderInContainer(container, _reorderNoteId);
        if (border == null) return;

        // Dim the source item
        border.Opacity = 0.25;

        // Build a temp ordered list for live reorder
        _dragItems = (notesList.ItemsSource as System.Collections.IList)
            ?.Cast<DockNoteItem>().ToList() ?? new();
        _orderChangedDuringDrag = false;
    }

    private int FindItemIndex(Guid noteId)
    {
        var items = notesList.ItemsSource as System.Collections.IList;
        if (items == null) return 0;
        for (int i = 0; i < items.Count; i++)
            if (items[i] is DockNoteItem di && di.NoteId == noteId) return i;
        return 0;
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
        if (_dragItems == null) return;

        // Find which visual slot the cursor is at by checking actual item positions
        int slot = GetSlotFromVisualPosition(pos);

        int srcIdx = _dragItems.FindIndex(d => d.NoteId == _reorderNoteId);
        if (srcIdx < 0) return;

        // Adjust insert for removal when dragging down
        int insertAt = slot;
        if (srcIdx < slot) insertAt--;
        insertAt = Math.Clamp(insertAt, 0, _dragItems.Count - 1);

        // If already at the right spot, just move the ghost
        if (insertAt == srcIdx) return;

        // Reorder: create a NEW list so WPF sees a different reference
        var newList = new List<DockNoteItem>(_dragItems);
        var movedItem = newList[srcIdx];
        newList.RemoveAt(srcIdx);
        newList.Insert(insertAt, movedItem);
        _dragItems = newList;

        // Set fresh ItemsSource so WPF regenerates containers
        notesList.ItemsSource = _dragItems;
        UpdateLayout();

        // Keep source item dimmed in its new container
        var newContainer = FindNoteContainer(_reorderNoteId);
        if (newContainer != null)
        {
            var srcBorder = FindBorderInContainer(newContainer, _reorderNoteId);
            if (srcBorder != null) srcBorder.Opacity = 0.25;
        }

        _orderChangedDuringDrag = true;
    }

    private int GetSlotFromVisualPosition(Point pos)
    {
        var source = notesList.ItemsSource as System.Collections.IList;
        if (source == null) return 0;
        int count = source.Count;
        if (count == 0) return 0;

        // Walk containers and find where the cursor Y falls
        for (int i = 0; i < count; i++)
        {
            var cp = notesList.ItemContainerGenerator.ContainerFromIndex(i) as ContentPresenter;
            if (cp == null) continue;

            var itemPos = cp.TransformToAncestor(dockScroller).Transform(new Point(0, 0));
            double itemCenter = itemPos.Y + cp.ActualHeight / 2;

            if (pos.Y < itemCenter)
                return i;
        }

        return count; // past the last item
    }



    private void DockScroller_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        dockScroller.ReleaseMouseCapture();

        // Restore source opacity
        RestoreSourceOpacity();

        if (_isReorderDragging)
        {
            _isReorderDragging = false;

            // Persist the new order from _dragItems to the store (only if changed)
            if (_orderChangedDuringDrag && _dragItems != null)
            {
                // Re-order the store's ObservableCollection to match _dragItems
                var storeNotes = _store.Notes;
                var ordered = _dragItems
                    .Select(di => storeNotes.FirstOrDefault(n => n.Id == di.NoteId))
                    .Where(n => n != null)
                    .ToList();

                storeNotes.Clear();
                int order = 0;
                foreach (var note in ordered)
                {
                    if (note == null) continue;
                    storeNotes.Add(note);
                    note.Order = order++;
                }

                _store.Save();
            }

            RefreshNotes();
            _dragItems = null;
            _dragSourceContainer = null;
            _dragStartPoint = null;
            _reorderNoteId = Guid.Empty;
            return;
        }

        // Was a click, not drag.
        // NOTE: e.OriginalSource is the ScrollViewer (mouse capture target), NOT the note icon.
        // Use _reorderNoteId captured at mouse-down instead.
        Guid noteId = _reorderNoteId;
        if (noteId != Guid.Empty)
        {
            if (e.ClickCount >= 2)
            {
                var note = _store.Notes.FirstOrDefault(n => n.Id == noteId);
                if (note == null) { _dragStartPoint = null; _reorderNoteId = Guid.Empty; return; }

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
        _reorderNoteId = Guid.Empty;
    }

    private void RestoreSourceOpacity()
    {
        // After live reorder the original container may be gone,
        // so find the source by NoteId in the current ItemsSource
        var container = FindNoteContainer(_reorderNoteId);
        if (container != null)
        {
            var b = FindBorderInContainer(container, _reorderNoteId);
            if (b != null) b.Opacity = 1.0;
        }
        _dragSourceContainer = null;
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

    public void ApplyTheme(string theme)
    {
        if (theme == "light")
        {
            _dockBgNormal = Color.FromArgb(0x4C, 0xDD, 0xDD, 0xDD);
            _dockBgHover = Color.FromArgb(0xFF, 0xDD, 0xDD, 0xDD);
            dockBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(0xFF, 0xCC, 0xCC, 0xCC));
            exitDockBtn.Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0x88, 0x88, 0x88));
            _exitBtnHoverBg = Color.FromArgb(0xFF, 0xCC, 0xCC, 0xCC);
            dockBorder.Effect = null;
        }
        else
        {
            _dockBgNormal = Color.FromArgb(0x4C, 0x1A, 0x1A, 0x1A);
            _dockBgHover = Color.FromArgb(0xFF, 0x1A, 0x1A, 0x1A);
            dockBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(0xFF, 0x3A, 0x3A, 0x3A));
            exitDockBtn.Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0x55, 0x55, 0x55));
            _exitBtnHoverBg = Color.FromArgb(0xFF, 0x2A, 0x2A, 0x2A);
            dockBorder.Effect = new DropShadowEffect
            {
                ShadowDepth = 4,
                Color = Colors.Black,
                Opacity = 0.5,
                BlurRadius = 12,
                Direction = 270
            };
        }

        // Reset current state
        var currentAlpha = _dockBgBrush.Color.A;
        if (currentAlpha > 0xA0)
            _dockBgBrush.Color = _dockBgHover;
        else
            _dockBgBrush.Color = _dockBgNormal;
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
