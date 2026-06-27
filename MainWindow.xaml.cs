using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Data;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using QuickNotes.Models;
using QuickNotes.Views;
using System.Text.RegularExpressions;

namespace QuickNotes;

public partial class MainWindow : Window
{
    private readonly NotesStore store = new();
    private readonly ListCollectionView _view;
    private readonly DispatcherTimer _saveTimer = new();
    private readonly DispatcherTimer _undoTimer = new();
    private Note? _deletedNote;
    private Note? _deletedBackup;
    private Note? _dragNote;
    private FrameworkElement? _dragCard;
    private Point _dragStart;
    private bool _isDragging;
    private DockWindow? _dockWindow;

    public MainWindow()
    {
        InitializeComponent();
        try
        {
            store.Load();
        }
        catch (Exception ex)
        {
            ErrorLog.Write(ex, "MainWindow.Load");
            ShowStatus("Error al cargar notas", true);
        }
        _view = (ListCollectionView)CollectionViewSource.GetDefaultView(store.Notes);
        notesList.ItemsSource = _view;

        Left = store.MainLeft;
        Top = store.MainTop;
        Width = store.MainWidth;
        Height = store.MainHeight;
        // Clamp to visible screen
        var mr = MonitorHelper.ClampToScreen(Left, Top, Width, Height);
        Left = mr.Left;
        Top = mr.Top;

        statusText.Text = "Ready";
        UpdateStats();
        ApplyTheme(store.Theme);
        SetStartWithWindows(store.StartWithWindows);
        AnimationHelper.Enabled = store.AnimationsEnabled;
        ApplyCompactMode(store.CompactMode);
        Loaded += RestoreTabs;
        Loaded += RestoreOpenWindows;
        _saveTimer.Interval = TimeSpan.FromSeconds(store.AutoSaveInterval);
        _saveTimer.Tick += (_, _) =>
        {
            _saveTimer.Stop();
            try { store.Save(); ShowStatus($"Saved {DateTime.Now:t}"); foreach (var n in store.Notes) n.IsDirty = false; }
            catch (Exception ex) { ErrorLog.Write(ex, "AutoSave"); ShowStatus("Error al guardar", true); }
            UpdateStats();
        };
        _undoTimer.Interval = TimeSpan.FromMilliseconds(5000);
        _undoTimer.Tick += (_, _) => FinalizeDelete();

        // Wire NoteCard routed events
        AddHandler(NoteCard.PinToggleEvent, new RoutedEventHandler(NoteCard_PinToggle));
        AddHandler(NoteCard.ColorChangedEvent, new RoutedEventHandler(NoteCard_ColorChanged));
        AddHandler(NoteCard.TitleChangedEvent, new RoutedEventHandler(NoteCard_TitleChanged));
        AddHandler(NoteCard.ContextMenuActionEvent, new RoutedEventHandler(NoteCard_ContextMenuAction));

        // Handle action button clicks via standard Button.Click (reliable across DataTemplates)
        AddHandler(Button.ClickEvent, new RoutedEventHandler(NoteCardAction_Click));

        // Save on critical events for data safety
        Deactivated += (_, _) => { store.Save(); };
        StateChanged += (_, _) =>
        {
            if (WindowState == WindowState.Minimized)
            {
                store.Save();
                store.SaveSettings();
            }
        };
    }

    private void RestoreTabs(object? sender, RoutedEventArgs e)
    {
        // No-op: Tab restoration removed in cleanup.
    }

    private void RestoreOpenWindows(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(store.OpenNoteIds)) return;
        var ids = store.OpenNoteIds.Split(',', StringSplitOptions.RemoveEmptyEntries);
        foreach (var idStr in ids)
        {
            if (Guid.TryParse(idStr, out var id))
            {
                var note = store.Notes.FirstOrDefault(n => n.Id == id);
                if (note == null) continue;
                bool alreadyOpen = Application.Current.Windows.OfType<NoteWindow>().Any(w => w.DataContext == note);
                if (!alreadyOpen)
                {
                    var win = new NoteWindow(note, store);
                    win.Show();
                }
            }
        }

        // Clean up stale IDs that no longer exist
        var validIds = new List<string>();
        foreach (var idStr in ids)
        {
            if (Guid.TryParse(idStr, out var id) && store.Notes.Any(n => n.Id == id))
                validIds.Add(idStr);
        }
        store.OpenNoteIds = string.Join(",", validIds);
    }

    private void AddNote_Click(object sender, RoutedEventArgs e)
    {
        var color = !string.IsNullOrEmpty(store.DefaultColor)
            ? store.DefaultColor
            : Note.RandomColor();
        var note = new Note { Color = color };
        store.Notes.Add(note);
        store.Save();
        statusText.Text = "Note added";
        UpdateStats();
        scrollViewer.ScrollToBottom();
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            switch (e.Key)
            {
                case Key.N: AddNote_Click(this, null!); e.Handled = true; break;
                case Key.W: Close(); e.Handled = true; break;
                case Key.F: searchBox.Focus(); e.Handled = true; break;
                case Key.S: store.Save(); store.SaveSettings(); statusText.Text = "Saved"; e.Handled = true; break;
                case Key.Z: UndoLastDelete(); e.Handled = true; break;
                case Key.D1: Topmost = !Topmost; pinBtn.IsChecked = Topmost; e.Handled = true; break;
            }
        }

        if (Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key == Key.N)
        {
            AddNote_Click(this, null!);
            e.Handled = true;
        }
    }

    private void UndoLastDelete()
    {
        if (_deletedNote != null) Undo_Click(null!, null!);
    }

    private void NoteCardAction_Click(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is Button btn && btn.DataContext is Note note)
        {
            switch (btn.Tag?.ToString())
            {
                case "PopOut":
                    PopOutNote(note);
                    break;
                case "Copy":
                    CopyNote(note);
                    break;
                case "Delete":
                    DeleteNote(note, btn);
                    break;
            }
        }
    }

    private void PopOutNote(Note note)
    {
        foreach (Window w in Application.Current.Windows)
        {
            if (w is NoteWindow nw && nw.DataContext == note)
            {
                nw.Show();
                nw.Activate();
                nw.WindowState = WindowState.Normal;
                statusText.Text = "Note opened";
                return;
            }
        }

        var win = new NoteWindow(note, store);
        win.Show();
        statusText.Text = "Note popped out";
    }

    private void CopyNote(Note src)
    {
        var copy = new Note
        {
            Title = src.Title + " (copia)",
            Text = src.Text,
            Color = src.Color,
            LastModified = DateTime.Now,
        };
        store.Notes.Add(copy);
        store.Save();
        statusText.Text = "Nota duplicada";
        UpdateStats();
        scrollViewer.ScrollToBottom();
    }

    private void DeleteNote(Note note, Button btn)
    {
        _undoTimer.Stop();
        if (_deletedNote != null) FinalizeDelete();

        if (notesList.ItemContainerGenerator.ContainerFromItem(note) is ContentPresenter cp)
        {
            var noteCard = FindChild<NoteCard>(cp);
                if (noteCard != null)
                {
                    noteCard.MaxHeight = noteCard.ActualHeight;
                    var fade = AnimationHelper.MakeAnimation(0, 180);
                    var shrink = AnimationHelper.MakeAnimation(0, 180);
                    fade.Completed += (_, _) =>
                    {
                        noteCard.Visibility = Visibility.Collapsed;
                        ShowUndo(note);
                    };
                    noteCard.BeginAnimation(OpacityProperty, fade);
                    noteCard.BeginAnimation(MaxHeightProperty, shrink);
                    return;
                }
            }
    }

    private void ShowUndo(Note note)
    {
        _deletedNote = note;
        _deletedBackup = new Note
        {
            Id = note.Id,
            Title = note.Title,
            Text = note.Text,
            Color = note.Color,
            LastModified = note.LastModified,
            Order = note.Order,
            IsPinned = note.IsPinned,
            WinLeft = note.WinLeft,
            WinTop = note.WinTop,
            WinWidth = note.WinWidth,
            WinHeight = note.WinHeight,
        };
        // Close any NoteWindows for this note (in Mini or Normal mode)
        for (int i = Application.Current.Windows.Count - 1; i >= 0; i--)
        {
            if (Application.Current.Windows[i] is NoteWindow nw && nw.DataContext == note)
                nw.Close();
        }
        store.Notes.Remove(note);
        store.Save();
        statusText.Text = "Nota eliminada";
        undoLink.Visibility = Visibility.Visible;
        _undoTimer.Start();
    }

    private void Undo_Click(object sender, MouseButtonEventArgs e)
    {
        _undoTimer.Stop();
        if (_deletedNote == null || _deletedBackup == null) return;
        store.Notes.Add(_deletedNote);
        store.Save();
        if (notesList.ItemContainerGenerator.ContainerFromItem(_deletedNote) is ContentPresenter cp)
        {
            var card = FindChild<NoteCard>(cp);
            if (card != null)
            {
                card.Visibility = Visibility.Visible;
                var fadeIn = AnimationHelper.MakeAnimation(0, 1, 220);
                fadeIn.EasingFunction = new QuadraticEase();
                card.BeginAnimation(OpacityProperty, fadeIn);
            }
        }
        _deletedNote = null;
        _deletedBackup = null;
        undoLink.Visibility = Visibility.Collapsed;
        statusText.Text = "Restaurada";
        UpdateStats();
    }

    private void FinalizeDelete()
    {
        _deletedNote = null;
        _deletedBackup = null;
        undoLink.Visibility = Visibility.Collapsed;
        statusText.Text = "Nota eliminada";
        UpdateStats();
    }

    private void UpdateStats()
    {
        int totalWords = 0, totalChars = 0;
        foreach (var n in store.Notes)
        {
            totalChars += n.Text?.Length ?? 0;
            totalWords += CountWords(n.Text);
        }
        statsText.Text = $"{totalWords}w \u00b7 {totalChars}c";
    }

    private static int CountWords(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0;
        return Regex.Matches(text, @"\S+").Count;
    }

    private void ClearAll_Click(object sender, RoutedEventArgs e)
    {
        if (ShowConfirm("¿Borrar todas las notas?", "Esta acción no se puede deshacer."))
        {
            store.Notes.Clear();
            store.Save();
            statusText.Text = "All notes cleared";
        }
    }

    public NotesStore GetStore() => store;

    public void DebounceSave()
    {
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    public void ShowStatus(string message, bool isError = false)
    {
        statusText.Text = message;
        statusText.Foreground = isError
            ? new SolidColorBrush(Color.FromRgb(0xFF, 0x88, 0x88))
            : new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0xAA));
    }

    private void Card_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;
        if (e.OriginalSource is ButtonBase or TextBox or TextBlock) return;

        var card = FindParent<NoteCard>(e.OriginalSource as DependencyObject);
        if (card != null && card.DataContext is Note note)
        {
            _dragNote = note;
            _dragCard = card;
            _dragStart = e.GetPosition(notesList);
            _isDragging = false;
        }
    }

    private void Card_MouseMove(object sender, MouseEventArgs e)
    {
        if (_dragNote == null || _dragCard == null || e.LeftButton != MouseButtonState.Pressed) return;

        var pos = e.GetPosition(notesList);
        var delta = pos.Y - _dragStart.Y;

        if (!_isDragging && Math.Abs(delta) > 6)
        {
            _isDragging = true;
            _dragCard.CaptureMouse();
            _dragCard.Opacity = 0.85;
        }

        if (!_isDragging) return;

        int from = store.Notes.IndexOf(_dragNote);
        if (from < 0) return;

        int pinnedCount = store.Notes.Count(n => n.IsPinned);
        int minIdx = _dragNote.IsPinned ? 0 : pinnedCount;
        int maxIdx = _dragNote.IsPinned ? pinnedCount - 1 : store.Notes.Count - 1;

        double h = _dragCard.ActualHeight;
        double threshold = h / 2;

        if (delta < -threshold && from > minIdx)
        {
            store.Notes.Move(from, from - 1);
            _dragStart = new Point(_dragStart.X, _dragStart.Y - h);
        }
        else if (delta > threshold && from < maxIdx)
        {
            store.Notes.Move(from, from + 1);
            _dragStart = new Point(_dragStart.X, _dragStart.Y + h);
        }

        _dragCard.RenderTransform = new TranslateTransform(0, delta);
    }

    private void Card_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_isDragging && _dragCard != null)
        {
            _dragCard.RenderTransform = null;
            _dragCard.Opacity = 1;
            _dragCard.ReleaseMouseCapture();
            if (_dragNote != null) _dragNote.IsDirty = false;
            store.Save();
            UpdateStats();
        }
        _isDragging = false;
        _dragCard = null;
        _dragNote = null;
    }

    private void MoveNoteToCorrectPosition(Note note)
    {
        int idx = store.Notes.IndexOf(note);
        if (idx < 0) return;

        int target = 0;
        for (int i = 0; i < store.Notes.Count; i++)
            if (store.Notes[i].IsPinned && store.Notes[i].Id != note.Id)
                target++;

        if (idx != target)
            store.Notes.Move(idx, target);
    }

    // === NoteCard routed event handlers ===

    private void NoteCard_PinToggle(object sender, RoutedEventArgs e)
    {
        if (e.Source is NoteCard card && card.DataContext is Note note)
        {
            note.IsPinned = !note.IsPinned;
            note.LastModified = DateTime.Now;
            note.IsDirty = false;
            MoveNoteToCorrectPosition(note);
            store.Save();
        }
    }

    private void NoteCard_ColorChanged(object sender, RoutedEventArgs e)
    {
        if (e.Source is NoteCard card && card.DataContext is Note note)
        {
            note.LastModified = DateTime.Now;
            store.Save();
            statusText.Text = "Color actualizado";
        }
    }

    private void NoteCard_TitleChanged(object sender, RoutedEventArgs e)
    {
        if (e.Source is NoteCard card && card.DataContext is Note note)
        {
            note.LastModified = DateTime.Now;
            note.IsDirty = true;
        }
        DebounceSave();
    }

    private void NoteCard_ContextMenuAction(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is not MenuItem mi) return;

        // Walk up to find the NoteCard
        NoteCard? card = null;
        for (var el = mi as DependencyObject; el != null; el = VisualTreeHelper.GetParent(el))
        {
            if (el is NoteCard c) { card = c; break; }
        }
        if (card?.DataContext is not Note note) return;

        switch (mi.Tag?.ToString())
        {
            case "Duplicate":
                CopyNote(note);
                break;
            case "Delete":
                // Find the delete button within this card for undo animation
                var delBtn = FindCardButton(card, "Delete");
                DeleteNote(note, delBtn ?? new Button { Tag = "Delete" });
                break;
            case "Pin":
                note.IsPinned = !note.IsPinned;
                note.LastModified = DateTime.Now;
                note.IsDirty = false;
                MoveNoteToCorrectPosition(note);
                store.Save();
                statusText.Text = note.IsPinned ? "Nota anclada" : "Nota desanclada";
                break;
            case "Archive":
                statusText.Text = "⏳ Archivar disponible en Fase 2";
                break;
            case "Export":
                statusText.Text = "⏳ Exportar disponible en Fase 4";
                break;
        }
    }

    private static Button? FindCardButton(DependencyObject parent, string tag)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is Button btn && btn.Tag?.ToString() == tag) return btn;
            var found = FindCardButton(child, tag);
            if (found != null) return found;
        }
        return null;
    }

    private void Topmost_Changed(object sender, RoutedEventArgs e)
    {
        Topmost = pinBtn.IsChecked == true;
    }

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            var src = e.OriginalSource as DependencyObject;
            if (src is ButtonBase or TextBox) return;
            DragMove();
        }
    }

    private void Minimize_Click(object sender, RoutedEventArgs e)
    {
        if (_dockWindow == null || !_dockWindow.IsVisible)
        {
            var bounds = Helpers.MonitorHelper.GetMonitorWorkingArea(this);

            _dockWindow = new DockWindow(store, bounds, () =>
            {
                Show();
                Focus();
                RestoreFromDock();
            });
            _dockWindow.RefreshNotes();
            _dockWindow.Show();
        }
        Hide();
    }

    public void RestoreFromDock()
    {
        Show();
        Focus();
        _view?.Refresh();
        _dockWindow?.RefreshNotes();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void MenuBtn_Click(object sender, RoutedEventArgs e)
    {
        menuPopup.IsOpen = !menuPopup.IsOpen;
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        menuPopup.IsOpen = false;
        new SettingsWindow(store, this).ShowDialog();
    }

    public void ApplyTheme(string theme)
    {
        var effective = theme;
        if (effective == "system")
        {
            try
            {
                var reg = Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", "1");
                effective = (reg is int i && i == 1) ? "light" : "dark";
            }
            catch { effective = "dark"; }
        }

        Color bg, titleBg, statusBg, textColor, textMuted;
        if (effective == "light")
        {
            bg = Color.FromRgb(0xF0, 0xF0, 0xF0);
            titleBg = Color.FromRgb(0xE0, 0xE0, 0xE0);
            statusBg = Color.FromRgb(0xE0, 0xE0, 0xE0);
            textColor = Color.FromRgb(0x1A, 0x1A, 0x1A);
            textMuted = Color.FromRgb(0x88, 0x88, 0x88);
        }
        else
        {
            bg = Color.FromRgb(0x1E, 0x1E, 0x1E);
            titleBg = Color.FromRgb(0x26, 0x26, 0x26);
            statusBg = Color.FromRgb(0x26, 0x26, 0x26);
            textColor = Color.FromRgb(0xCC, 0xCC, 0xCC);
            textMuted = Color.FromRgb(0x55, 0x55, 0x55);
        }

        Background = new SolidColorBrush(bg);
        titleBar.Background = new SolidColorBrush(titleBg);
        statusBar.Background = new SolidColorBrush(statusBg);
        scrollViewer.Background = new SolidColorBrush(bg);

        // Update foregrounds for all controls in title bar
        foreach (var child in titleBarGrid.Children)
        {
            SetElementForeground(child, textColor);
        }
        // Search specific elements (not direct children of titleBarGrid)
        var textMutedBrush = new SolidColorBrush(textMuted);
        searchHint.Foreground = textMutedBrush;
        searchBox.Foreground = new SolidColorBrush(textColor);
        searchBox.CaretBrush = new SolidColorBrush(textColor);
        searchBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, textColor.R, textColor.G, textColor.B));

        // Status bar
        statusText.Foreground = new SolidColorBrush(textMuted);
        statsText.Foreground = new SolidColorBrush(textMuted);
        undoLink.Foreground = new SolidColorBrush(Color.FromRgb(0x22, 0x88, 0x55));
        clearAllLink.Foreground = new SolidColorBrush(textMuted);
    }

    public void ApplyCompactMode(bool compact)
    {
        double marginVal = compact ? 4 : 10;
        double bodyFontVal = compact ? 11 : 13;
        double bodyMinH = compact ? 24 : 40;
        double titleFontVal = compact ? 12 : 14;

        ReplaceStyle("NoteCard", typeof(Border), s => {
            if (s.Property == Border.MarginProperty)
                s.Value = new Thickness(marginVal, compact ? 3 : 5, 0, compact ? 3 : 5);
        });
        ReplaceStyle("NoteTextBox", typeof(TextBox), s => {
            if (s.Property == TextBox.FontSizeProperty) s.Value = bodyFontVal;
            if (s.Property == TextBox.MinHeightProperty) s.Value = bodyMinH;
        });
        ReplaceStyle("TitleBox", typeof(TextBox), s => {
            if (s.Property == TextBox.FontSizeProperty) s.Value = titleFontVal;
        });

        // Force refresh so existing items re-apply styles
        var src = notesList.ItemsSource;
        notesList.ItemsSource = null;
        notesList.ItemsSource = src;
    }

    private void ReplaceStyle(string key, Type targetType, Action<Setter> modifier)
    {
        if (Resources[key] is not Style oldStyle) return;
        var newStyle = new Style(targetType, oldStyle.BasedOn);
        foreach (var s in oldStyle.Setters.OfType<Setter>())
        {
            object clonedValue = (s.Value is Thickness t) ? new Thickness(t.Left, t.Top, t.Right, t.Bottom) : s.Value;
            var clone = new Setter(s.Property, clonedValue);
            modifier(clone);
            newStyle.Setters.Add(clone);
        }
        foreach (var trigger in oldStyle.Triggers)
            newStyle.Triggers.Add(trigger);
        Resources[key] = newStyle;
    }

    private static void SetElementForeground(object element, Color color)
    {
        var brush = new SolidColorBrush(color);
        switch (element)
        {
            case Button b:
                b.Foreground = brush;
                break;
            case ToggleButton tb:
                tb.Foreground = brush;
                break;
            case TextBlock tb:
                tb.Foreground = brush;
                break;
            case TextBox tb:
                tb.Foreground = brush;
                break;
            case Panel p:
                foreach (var child in p.Children)
                    SetElementForeground(child, color);
                break;
            case ContentControl cc:
                SetElementForeground(cc.Content, color);
                break;
        }
    }

    public void SetStartWithWindows(bool enable)
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Run", true);
            if (key == null)
            {
                ErrorLog.Write("SetStartWithWindows: no se pudo abrir la clave del registro");
                return;
            }

            if (enable)
            {
                var exePath = Environment.ProcessPath;
                if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
                {
                    ErrorLog.Write("SetStartWithWindows: no se pudo determinar la ruta del ejecutable");
                    return;
                }

                var existing = key.GetValue("QuickNotes") as string;
                if (existing != null && !string.Equals(existing, exePath, StringComparison.OrdinalIgnoreCase))
                {
                    key.DeleteValue("QuickNotes", false);
                }

                key.SetValue("QuickNotes", exePath);
            }
            else
            {
                key.DeleteValue("QuickNotes", false);
            }
        }
        catch (Exception ex)
        {
            ErrorLog.Write(ex, "SetStartWithWindows");
        }
    }

    public void SetAutoSaveInterval(int seconds)
    {
        _saveTimer.Stop();
        _saveTimer.Interval = TimeSpan.FromSeconds(seconds);
    }

    private static System.Collections.Generic.IEnumerable<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T t) yield return t;
            foreach (var sub in FindVisualChildren<T>(child))
                yield return sub;
        }
    }

    private void MenuExit_Click(object sender, RoutedEventArgs e)
    {
        menuPopup.IsOpen = false;
        Close();
    }

    private void About_Click(object sender, RoutedEventArgs e)
    {
        menuPopup.IsOpen = false;
        var about = new Window
        {
            Title = "Acerca de QuickNotes",
            Width = 380,
            Height = 260,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = new SolidColorBrush(Color.FromRgb(0x26, 0x26, 0x26)),
            Owner = this,
            ShowInTaskbar = false,
            Topmost = true,
        };

        var grid = new Grid { Margin = new Thickness(24) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var titleBlock = new TextBlock
        {
            Text = "QuickNotes",
            FontSize = 20,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(0xDD, 0xDD, 0xDD)),
        };
        Grid.SetRow(titleBlock, 0);
        grid.Children.Add(titleBlock);

        var body = new TextBlock
        {
            Text = $"QuickNotes v1.0{Environment.NewLine}Desarrollado por Felix Bryan Batista{Environment.NewLine}República Dominicana{Environment.NewLine}{Environment.NewLine}App de notas con formato enriquecido,{Environment.NewLine}pestañas laterales y temas oscuros.",
            FontSize = 13,
            Foreground = new SolidColorBrush(Color.FromRgb(0x99, 0x99, 0x99)),
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
            LineHeight = 20,
        };
        Grid.SetRow(body, 1);
        grid.Children.Add(body);

        var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        Grid.SetRow(btnPanel, 2);
        var okBtn = new Button
        {
            Content = "Cerrar",
            Width = 90,
            Height = 30,
            Cursor = Cursors.Hand,
            FontSize = 13,
        };
        okBtn.Style = MakeBtnStyle(Color.FromRgb(0xFF, 0xFF, 0xFF), Color.FromRgb(0x5A, 0x5A, 0x5A),
            Color.FromRgb(0xFF, 0xFF, 0xFF), Color.FromRgb(0x77, 0x77, 0x77));
        okBtn.Click += (_, _) => about.Close();
        btnPanel.Children.Add(okBtn);
        Grid.SetRow(btnPanel, 2);
        grid.Children.Add(btnPanel);

        about.Content = grid;
        about.ShowDialog();
    }

    private void Search_TextChanged(object sender, TextChangedEventArgs e)
    {
        var filter = searchBox.Text.Trim();

        if (string.IsNullOrEmpty(filter))
        {
            foreach (var n in store.Notes)
                n.IsSearchMatch = true;
            _view.Filter = null;
        }
        else
        {
            foreach (var n in store.Notes)
                n.IsSearchMatch =
                    n.Title.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                    n.PlainText.Contains(filter, StringComparison.OrdinalIgnoreCase);

            // Filter to show only matching + highlight bar on matches
            _view.Filter = obj => obj is Note note && note.IsSearchMatch;
        }

        // Pass search filter to NoteCards for inline text highlighting
        Views.NoteCard.SearchFilter = filter ?? "";

        UpdateSearchHint();
    }

    private void Search_GotFocus(object sender, RoutedEventArgs e) => UpdateSearchHint();

    private void Search_LostFocus(object sender, RoutedEventArgs e) => UpdateSearchHint();

    private void UpdateSearchHint()
    {
        searchHint.Visibility = string.IsNullOrEmpty(searchBox.Text) && !searchBox.IsFocused
            ? Visibility.Visible : Visibility.Collapsed;
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (store.ConfirmOnExit && !ShowConfirm("¿Salir de QuickNotes?", "Se cerrarán todas las notas y pestañas abiertas."))
        {
            e.Cancel = true;
            return;
        }

        store.MainLeft = Left;
        store.MainTop = Top;
        store.MainWidth = Width;
        store.MainHeight = Height;

        // Save open note windows
        var openIds = new List<string>();
        foreach (Window w in Application.Current.Windows)
            if (w is NoteWindow nw && nw.Visibility == Visibility.Visible && nw.DataContext is Note n)
                openIds.Add(n.Id.ToString());
        store.OpenNoteIds = string.Join(",", openIds);

        store.SaveSettings();

        if (_deletedNote != null) FinalizeDelete();
        _undoTimer.Stop();
        store.Save();
        // Close DockWindow if open
        _dockWindow?.Close();

        // Don't reset note positions — app is shutting down
        NoteWindow.IsAppShuttingDown = true;

        // Close all NoteWindows except MainWindow
        for (int i = Application.Current.Windows.Count - 1; i >= 0; i--)
        {
            if (Application.Current.Windows[i] is NoteWindow nw)
                nw.Close();
        }

        for (int i = Application.Current.Windows.Count - 1; i >= 0; i--)
            if (Application.Current.Windows[i] is not MainWindow)
                Application.Current.Windows[i].Close();

        base.OnClosing(e);
    }

    private static bool ShowConfirm(string title, string message)
    {
        var win = new Window
        {
            Title = title,
            Width = 360,
            Height = 170,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = new SolidColorBrush(Color.FromRgb(0x26, 0x26, 0x26)),
            Owner = Application.Current.MainWindow,
            ShowInTaskbar = false,
            Topmost = true,
        };

        var grid = new Grid { Margin = new Thickness(20) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var titleBlock = new TextBlock
        {
            Text = title,
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(0xDD, 0xDD, 0xDD)),
        };
        Grid.SetRow(titleBlock, 0);
        grid.Children.Add(titleBlock);

        var msgBlock = new TextBlock
        {
            Text = message,
            FontSize = 13,
            Foreground = new SolidColorBrush(Color.FromRgb(0x99, 0x99, 0x99)),
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetRow(msgBlock, 1);
        grid.Children.Add(msgBlock);

        var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        Grid.SetRow(btnPanel, 2);
        grid.Children.Add(btnPanel);

        var noBtn = new Button
        {
            Content = "Cancelar",
            Width = 90,
            Height = 30,
            Margin = new Thickness(0, 0, 10, 0),
            Cursor = Cursors.Hand,
            FontSize = 13,
        };
        noBtn.Style = MakeBtnStyle(Color.FromRgb(0xBB, 0xBB, 0xBB), Color.FromRgb(0x3A, 0x3A, 0x3A),
            Color.FromRgb(0xFF, 0xFF, 0xFF), Color.FromRgb(0x55, 0x55, 0x55));
        noBtn.Click += (_, _) => { win.DialogResult = false; win.Close(); };
        btnPanel.Children.Add(noBtn);

        var yesBtn = new Button
        {
            Content = "Salir",
            Width = 90,
            Height = 30,
            Cursor = Cursors.Hand,
            FontSize = 13,
        };
        yesBtn.Style = MakeBtnStyle(Color.FromRgb(0xFF, 0xFF, 0xFF), Color.FromRgb(0x5A, 0x5A, 0x5A),
            Color.FromRgb(0xFF, 0xFF, 0xFF), Color.FromRgb(0x77, 0x77, 0x77));
        yesBtn.Click += (_, _) => { win.DialogResult = true; win.Close(); };
        btnPanel.Children.Add(yesBtn);

        win.Content = grid;
        return win.ShowDialog() == true;
    }

    internal static Style MakeBtnStyle(Color fg, Color bg, Color fgHover, Color bgHover)
    {
        var style = new Style(typeof(Button));
        style.Setters.Add(new Setter(Control.ForegroundProperty, new SolidColorBrush(fg)));
        style.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(bg)));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));

        var template = new ControlTemplate(typeof(Button));
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        border.SetValue(Border.BorderThicknessProperty, new Thickness(0));
        border.SetValue(Border.SnapsToDevicePixelsProperty, true);
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));

        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        presenter.SetValue(ContentPresenter.SnapsToDevicePixelsProperty, true);
        presenter.SetValue(ContentPresenter.ContentProperty, new TemplateBindingExtension(ContentControl.ContentProperty));
        presenter.SetValue(ContentPresenter.ContentTemplateProperty, new TemplateBindingExtension(ContentControl.ContentTemplateProperty));
        border.AppendChild(presenter);

        template.VisualTree = border;

        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(bgHover)));
        hover.Setters.Add(new Setter(Control.ForegroundProperty, new SolidColorBrush(fgHover)));
        template.Triggers.Add(hover);

        style.Setters.Add(new Setter(Control.TemplateProperty, template));
        return style;
    }

    internal static Style MakeComboStyle()
    {
        return (Style)System.Windows.Application.Current.FindResource("SettingsComboBoxStyle");
    }

    private static T? FindChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T t) return t;
            var found = FindChild<T>(child);
            if (found != null) return found;
        }
        return null;
    }

    private static T? FindParent<T>(DependencyObject? child) where T : DependencyObject
    {
        while (child != null)
        {
            if (child is T t) return t;
            child = child is Visual ? VisualTreeHelper.GetParent(child) : null;
        }
        return null;
    }
}