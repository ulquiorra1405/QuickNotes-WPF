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
    private Note? _dragNote;
    private FrameworkElement? _dragCard;
    private Point _dragStart;
    private bool _isDragging;
    private DockWindow? _dockWindow;
    private string _activeSection = "all";
    private string _searchFilter = "";
    private bool _sidebarExpanded;
    private Brush _sidebarActiveBg = new SolidColorBrush(Color.FromArgb(0xFF, 0x3A, 0x3A, 0x3A));

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

        // Restore active section after Load
        Loaded += (_, _) =>
        {
            _activeSection = store.SidebarSection switch
            {
                "all" or "archived" or "trash" or "timeline" => store.SidebarSection,
                string s when s.StartsWith("notebook:") => s,
                string s when s.StartsWith("tag:") => s,
                _ => "all"
            };
            UpdateTagNotebookLookups();
            SetActiveSection(_activeSection);
            UpdateCounters();
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
        // Build template popup on demand
        templatePanel.Children.Clear();
        var templates = store.GetTemplates();

        // Separator between built-in section label and items
        bool first = true;
        foreach (var t in templates)
        {
            if (t.IsBuiltIn && first)
            {
                first = false;
            }

            var grid = new Grid
            {
                Cursor = System.Windows.Input.Cursors.Hand,
                Margin = new Thickness(0, 1, 0, 1),
                Background = System.Windows.Media.Brushes.Transparent,
            };
            var text = new TextBlock
            {
                Text = $"{t.Icon}  {t.Name}",
                Padding = new Thickness(6, 5, 6, 5),
                Foreground = new SolidColorBrush(Color.FromRgb(0xEE, 0xEE, 0xEE)),
                FontSize = 13,
                FontFamily = new FontFamily("Segoe UI Emoji, Segoe UI"),
            };
            grid.Children.Add(text);
            var template = t; // capture
            grid.MouseEnter += (_, _) => grid.Background = new SolidColorBrush(Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF));
            grid.MouseLeave += (_, _) => grid.Background = System.Windows.Media.Brushes.Transparent;
            grid.MouseDown += (_, args) =>
            {
                if (args.LeftButton == System.Windows.Input.MouseButtonState.Pressed)
                    CreateNoteFromTemplate(template);
            };
            templatePanel.Children.Add(grid);
        }

        if (templatePanel.Children.Count > 0)
        {
            templatePopup.PlacementTarget = addBtn;
            templatePopup.IsOpen = true;
        }
        else
        {
            // Fallback: direct create blank note
            CreateNoteFromTemplate(null);
        }
    }

    private void CreateNoteFromTemplate(NoteTemplate? template)
    {
        templatePopup.IsOpen = false;

        var color = !string.IsNullOrEmpty(store.DefaultColor)
            ? store.DefaultColor
            : Note.RandomColor();
        var maxOrder = store.Notes.Count > 0 ? store.Notes.Max(n => n.Order) : -1;

        Note note;
        if (template == null || template.Name == "Nota en blanco")
        {
            note = new Note { Color = color, Order = maxOrder + 1 };
        }
        else
        {
            var content = template.Content;
            // Replace date placeholder in Diario template
            if (template.Name == "Diario")
            {
                var today = DateTime.Now.ToString("dddd, d MMMM yyyy", new System.Globalization.CultureInfo("es-DO"));
                content = content.Replace("[fecha de hoy]", today);
            }
            note = new Note
            {
                Title = template.Name,
                Text = content,
                Color = color,
                Order = maxOrder + 1,
            };
        }

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
        // No-op in new trash system; use Restore from context menu or sidebar
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
        MoveToTrash(note);
    }

    private void MoveToTrash(Note note)
    {
        // Close any NoteWindows for this note
        for (int i = Application.Current.Windows.Count - 1; i >= 0; i--)
        {
            if (Application.Current.Windows[i] is NoteWindow nw && nw.DataContext == note)
                nw.Close();
        }

        // Fade animation, then mark as deleted in-place
        if (notesList.ItemContainerGenerator.ContainerFromItem(note) is ContentPresenter cp)
        {
            var noteCard = FindChild<NoteCard>(cp);
            if (noteCard != null)
            {
                noteCard.MaxHeight = noteCard.ActualHeight;
                var fade = AnimationHelper.MakeAnimation(0, 180);
                fade.Completed += (_, _) =>
                {
                    note.IsDeleted = true;
                    note.DeletedAt = DateTime.Now;
                    note.IsDirty = true;
                    MaybeAddDefaultNote();
                    store.Save();
                    ApplyFilters();
                    UpdateCounters();
                    ShowStatus("Nota movida a papelera", false);
                };
                noteCard.BeginAnimation(OpacityProperty, fade);
                noteCard.BeginAnimation(MaxHeightProperty, AnimationHelper.MakeAnimation(0, 180));
                return;
            }
        }

        // Fallback: no animation
        note.IsDeleted = true;
        note.DeletedAt = DateTime.Now;
        note.IsDirty = true;
        MaybeAddDefaultNote();
        store.Save();
        ApplyFilters();
        UpdateCounters();
    }

    private void RestoreFromTrash(Note note)
    {
        note.IsDeleted = false;
        note.DeletedAt = null;
        note.IsDirty = true;
        store.Save();
        ApplyFilters();
        UpdateCounters();

        // Check if we need to remove the extra default note
        var active = store.Notes.Where(n => !n.IsArchived && !n.IsDeleted).ToList();
        if (active.Count > 1 && active.Any(n => string.IsNullOrEmpty(n.Title) && string.IsNullOrEmpty(n.Text)))
        {
            var empty = active.FirstOrDefault(n => string.IsNullOrEmpty(n.Title) && string.IsNullOrEmpty(n.Text) && n.Id != note.Id);
            if (empty != null) store.Notes.Remove(empty);
            ApplyFilters();
        }

        // Animate the restored card if visible
        if (notesList.ItemContainerGenerator.ContainerFromItem(note) is ContentPresenter cp2)
        {
            var card = FindChild<NoteCard>(cp2);
            if (card != null)
            {
                card.Opacity = 0;
                var fadeIn = AnimationHelper.MakeAnimation(0, 1, 220);
                fadeIn.EasingFunction = new QuadraticEase();
                card.BeginAnimation(OpacityProperty, fadeIn);
            }
        }

        ShowStatus("Nota restaurada", false);
    }

    private void PermanentDeleteNote(Note note)
    {
        store.Notes.Remove(note);
        store.Save();
        UpdateCounters();
        ApplyFilters();
        MaybeAddDefaultNote();
        ShowStatus("Nota eliminada permanentemente", false);
    }

    private void ArchiveNote(Note note)
    {
        note.IsArchived = true;
        note.IsDirty = true;
        MaybeAddDefaultNote();
        store.Save();
        ApplyFilters();
        UpdateCounters();
        ShowStatus("Nota archivada", false);
    }

    private void RestoreFromArchive(Note note)
    {
        note.IsArchived = false;
        note.IsDirty = true;
        store.Save();
        ApplyFilters();
        UpdateCounters();

        var active = store.Notes.Where(n => !n.IsArchived && !n.IsDeleted).ToList();
        if (active.Count > 1 && active.Any(n => string.IsNullOrEmpty(n.Title) && string.IsNullOrEmpty(n.Text)))
        {
            var empty = active.FirstOrDefault(n => string.IsNullOrEmpty(n.Title) && string.IsNullOrEmpty(n.Text) && n.Id != note.Id);
            if (empty != null) store.Notes.Remove(empty);
            ApplyFilters();
        }

        ShowStatus("Nota restaurada", false);
    }

    private void MaybeAddDefaultNote()
    {
        var active = store.Notes.Where(n => !n.IsArchived && !n.IsDeleted).ToList();
        if (active.Count == 0)
        {
            store.Notes.Add(new Note());
        }
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
        if (_activeSection == "timeline") return; // no drag reorder in timeline
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

        NoteCard? card = null;
        if (mi.Parent is ContextMenu cm)
            card = FindParent<NoteCard>(cm.PlacementTarget);
        else
            card = NoteCard.CurrentContextCard;

        if (card?.DataContext is not Note note) return;

        switch (mi.Tag?.ToString())
        {
            case "Duplicate":
                CopyNote(note);
                break;
            case "Delete":
                var delBtn = FindCardButton(card, "Delete");
                DeleteNote(note, delBtn ?? new Button { Tag = "Delete" });
                break;
            case "PermanentDelete":
                PermanentDeleteNote(note);
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
                if (note.IsArchived)
                    RestoreFromArchive(note);
                else
                    ArchiveNote(note);
                break;
            case "Restore":
                if (note.IsDeleted)
                    RestoreFromTrash(note);
                else if (note.IsArchived)
                    RestoreFromArchive(note);
                break;
            case "Export":
                statusText.Text = "⏳ Exportar disponible en Fase 4";
                break;

            case string s when s.StartsWith("notebook:"):
                {
                    var idStr = s.AsSpan(9);
                    if (idStr.Length == 0)
                        note.NotebookId = null;
                    else if (Guid.TryParse(idStr, out var nbId))
                        note.NotebookId = nbId;
                    note.IsDirty = true;
                    store.Save();
                    ApplyFilters();
                    ShowStatus("Nota movida", false);
                    break;
                }

            case string s when s.StartsWith("tagtoggle:"):
                {
                    if (Guid.TryParse(s.AsSpan(10), out var tId))
                    {
                        if (note.TagIds.Contains(tId))
                            note.TagIds.Remove(tId);
                        else
                            note.TagIds.Add(tId);
                        note.IsDirty = true;
                        store.Save();
                        ApplyFilters();
                        ShowStatus("Tags actualizados", false);
                    }
                    break;
                }
        }
    }

    internal void HandleNoteAction(Note note, string actionTag)
    {
        note.IsDirty = true;
        store.Save();
        ApplyFilters();
        if (actionTag.StartsWith("notebook:"))
            ShowStatus("Nota movida", false);
        else if (actionTag.StartsWith("tagtoggle:"))
            ShowStatus("Tags actualizados", false);
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

        // Sidebar
        var sidebarBg = effective == "light"
            ? Color.FromArgb(0xFF, 0xEA, 0xEA, 0xEA)
            : Color.FromArgb(0xFF, 0x23, 0x23, 0x23);
        sidebarBorder.Background = new SolidColorBrush(sidebarBg);

        var sidebarMuted = new SolidColorBrush(Color.FromArgb(effective == "light" ? (byte)0x66 : (byte)0x88, textColor.R, textColor.G, textColor.B));
        var activeBg = effective == "light"
            ? new SolidColorBrush(Color.FromArgb(0xFF, 0xD0, 0xD0, 0xD0))
            : new SolidColorBrush(Color.FromArgb(0xFF, 0x3A, 0x3A, 0x3A));
        UpdateSidebarHighlightColors(activeBg, sidebarMuted);

        // Status bar
        statusText.Foreground = new SolidColorBrush(textMuted);
        statsText.Foreground = new SolidColorBrush(textMuted);
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
        _searchFilter = searchBox.Text.Trim();
        ApplyFilters();
        Views.NoteCard.SearchFilter = _searchFilter ?? "";
        UpdateSearchHint();
    }

    private void Search_GotFocus(object sender, RoutedEventArgs e) => UpdateSearchHint();

    private void Search_LostFocus(object sender, RoutedEventArgs e) => UpdateSearchHint();

    private void UpdateSearchHint()
    {
        searchHint.Visibility = string.IsNullOrEmpty(searchBox.Text) && !searchBox.IsFocused
            ? Visibility.Visible : Visibility.Collapsed;
    }

    // ── Sidebar ──

    private void SidebarToggle_Click(object sender, MouseButtonEventArgs e)
    {
        ToggleSidebar();
    }

    private void ToggleSidebar()
    {
        _sidebarExpanded = !_sidebarExpanded;
        double targetWidth = _sidebarExpanded ? 160 : 48;

        // Animate the sidebar border width directly
        var anim = AnimationHelper.MakeAnimation(sidebarBorder.Width, targetWidth, 180);
        anim.EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut };
        sidebarBorder.BeginAnimation(Border.WidthProperty, anim);

        // Fade labels in/out
        double labelTarget = _sidebarExpanded ? 1.0 : 0.0;
        var fade = AnimationHelper.MakeAnimation(1 - labelTarget, labelTarget, 150);
        fade.BeginTime = _sidebarExpanded ? TimeSpan.FromMilliseconds(60) : TimeSpan.Zero;
        sectAllLabel.BeginAnimation(OpacityProperty, fade);
        sectAllCount.BeginAnimation(OpacityProperty, fade);
        sectArchivedLabel.BeginAnimation(OpacityProperty, fade);
        sectArchivedCount.BeginAnimation(OpacityProperty, fade);
        sectTrashLabel.BeginAnimation(OpacityProperty, fade);
        sectTrashCount.BeginAnimation(OpacityProperty, fade);
        sectTimelineLabel.BeginAnimation(OpacityProperty, fade);
        sectNotebooksLabel.BeginAnimation(OpacityProperty, fade);
        sectNotebooksBtnAdd.BeginAnimation(OpacityProperty, fade);
        sectTagsLabel.BeginAnimation(OpacityProperty, fade);
        sectTagsBtnAdd.BeginAnimation(OpacityProperty, fade);
    }

    private void UpdateSidebarLabels()
    {
        double o = _sidebarExpanded ? 1.0 : 0.0;
        sectAllLabel.Opacity = o;
        sectAllCount.Opacity = o;
        sectArchivedLabel.Opacity = o;
        sectArchivedCount.Opacity = o;
        sectTrashLabel.Opacity = o;
        sectTrashCount.Opacity = o;
        sectTimelineLabel.Opacity = o;
        sectNotebooksLabel.Opacity = o;
        sectNotebooksBtnAdd.Opacity = o;
        sectTagsLabel.Opacity = o;
        sectTagsBtnAdd.Opacity = o;
    }

    private void SidebarSection_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border b && b.Tag is string section)
        {
            SetActiveSection(section);
        }
    }

    private void SetActiveSection(string section)
    {
        _activeSection = section;
        store.SidebarSection = section;
        UpdateSidebarHighlight();
        ApplyFilters();
    }

    private void UpdateSidebarHighlight()
    {
        var sections = new[] { sectAll, sectArchived, sectTrash, sectTimeline };
        var transparent = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));
        foreach (var s in sections)
            s.Background = transparent;

        Border? target = _activeSection switch
        {
            "all" => sectAll,
            "archived" => sectArchived,
            "trash" => sectTrash,
            "timeline" => sectTimeline,
            _ => sectAll
        };
        if (target != null)
            target.Background = _sidebarActiveBg;
    }

    private void UpdateSidebarHighlightColors(Brush activeBg, Brush mutedFg)
    {
        _sidebarActiveBg = activeBg;
        UpdateSidebarHighlight();

        sidebarToggleIcon.Foreground = mutedFg;
        sectAllIcon.Foreground = mutedFg;
        sectAllLabel.Foreground = mutedFg;
        sectAllCount.Foreground = mutedFg;
        sectArchivedIcon.Foreground = mutedFg;
        sectArchivedLabel.Foreground = mutedFg;
        sectArchivedCount.Foreground = mutedFg;
        sectTrashIcon.Foreground = mutedFg;
        sectTrashLabel.Foreground = mutedFg;
        sectTrashCount.Foreground = mutedFg;
        sectTimelineIcon.Foreground = mutedFg;
        sectTimelineLabel.Foreground = mutedFg;
        sectNotebooksIcon.Foreground = mutedFg;
        sectNotebooksLabel.Foreground = mutedFg;
        sectNotebooksBtnAdd.Foreground = mutedFg;
        sectTagsIcon.Foreground = mutedFg;
        sectTagsLabel.Foreground = mutedFg;
    }

    private void UpdateTagNotebookLookups()
    {
        // Count notes per tag/notebook
        var allNotes = store.Notes.ToList();
        foreach (var tag in store.Tags)
            tag.Count = allNotes.Count(n => n.TagIds.Contains(tag.Id) && !n.IsDeleted);
        foreach (var nb in store.Notebooks)
            nb.Count = allNotes.Count(n => n.NotebookId == nb.Id && !n.IsDeleted);

        // Build lookups for NoteCard
        NoteCard.TagNameLookup.Clear();
        foreach (var tag in store.Tags)
            NoteCard.TagNameLookup[tag.Id] = tag.Name;
        NoteCard.NotebookNameLookup.Clear();
        foreach (var nb in store.Notebooks)
            NoteCard.NotebookNameLookup[nb.Id] = nb.Name;
    }

    private void UpdateCounters()
    {
        var all = store.Notes.ToList();
        sectAllCount.Text = $"({all.Count(n => !n.IsArchived && !n.IsDeleted)})";
        sectArchivedCount.Text = $"({all.Count(n => n.IsArchived && !n.IsDeleted)})";
        sectTrashCount.Text = $"({all.Count(n => n.IsDeleted)})";
        UpdateTagNotebookLookups();
    }

    // ── Sidebar flyout (for notebooks / tags) ──

    private void SidebarAccordion_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border b && b.Tag is string section)
            ShowSidebarFlyout(b, section);
    }

    private void Window_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        // Close sidebar flyout when clicking outside it
        if (sidebarFlyout.IsOpen && sidebarFlyout.Child is FrameworkElement child)
        {
            var dep = e.OriginalSource as DependencyObject;
            bool inside = false;
            while (dep != null)
            {
                if (dep == child)
                { inside = true; break; }
                dep = VisualTreeHelper.GetParent(dep);
            }
            if (!inside)
                sidebarFlyout.IsOpen = false;
        }
    }

    private void ShowSidebarFlyout(Border header, string section)
    {
        var popup = sidebarFlyout;
        popup.PlacementTarget = header;
        popup.Placement = PlacementMode.Right;
        popup.HorizontalOffset = 4;
        popup.VerticalOffset = -4;

        // Build content
        var border = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x26, 0x26, 0x26)),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(6),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            MinWidth = 140,
            MaxHeight = 300,
        };

        var stack = new StackPanel();

        if (section == "notebooks")
        {
            if (store.Notebooks.Count == 0)
            {
                stack.Children.Add(new TextBlock
                {
                    Text = "Sin libretas",
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF)),
                    Margin = new Thickness(10, 6, 10, 6),
                });
            }

            foreach (var nb in store.Notebooks)
            {
                var captured = nb;
                var item = CreateFlyoutItem($"📓 {nb.Name}", nb.Count, () =>
                {
                    SetActiveSection($"notebook:{captured.Id}");
                    popup.IsOpen = false;
                });
                stack.Children.Add(item);
            }
        }
        else if (section == "tags")
        {
            if (store.Tags.Count == 0)
            {
                stack.Children.Add(new TextBlock
                {
                    Text = "Sin tags",
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF)),
                    Margin = new Thickness(10, 6, 10, 6),
                });
            }

            foreach (var tag in store.Tags)
            {
                var captured = tag;
                var item = CreateFlyoutItem($"#{tag.Name}", tag.Count, () =>
                {
                    SetActiveSection($"tag:{captured.Id}");
                    popup.IsOpen = false;
                });
                stack.Children.Add(item);
            }
        }

        border.Child = new ScrollViewer { Content = stack, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        popup.Child = border;
        popup.IsOpen = true;
    }

    private Border CreateFlyoutItem(string label, int? count, Action onClick)
    {
        var b = new Border
        {
            Padding = new Thickness(10, 6, 10, 6),
            Margin = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Cursor = Cursors.Hand,
            Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0)),
        };
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var txt = new TextBlock
        {
            Text = label,
            FontSize = 13,
            Foreground = new SolidColorBrush(Color.FromRgb(0xDD, 0xDD, 0xDD)),
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(txt, 0);
        grid.Children.Add(txt);

        if (count.HasValue)
        {
            var ct = new TextBlock
            {
                Text = $"({count.Value})",
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromArgb(0x88, 0xFF, 0xFF, 0xFF)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0),
            };
            Grid.SetColumn(ct, 1);
            grid.Children.Add(ct);
        }

        b.Child = grid;

        b.MouseEnter += (_, _) =>
            b.Background = new SolidColorBrush(Color.FromArgb(0x3F, 0xFF, 0xFF, 0xFF));
        b.MouseLeave += (_, _) =>
            b.Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));
        b.PreviewMouseDown += (_, _) => onClick();

        return b;
    }

    private void AddNotebook_Click(object sender, MouseButtonEventArgs e)
    {
        var dialog = new Window
        {
            Title = "Nueva libreta",
            Width = 340,
            Height = 180,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0)),
            Owner = this,
            ShowInTaskbar = false,
        };

        var border = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x26, 0x26, 0x26)),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(20),
        };

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var titleBlock = new TextBlock
        {
            Text = "Nueva libreta",
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(0xDD, 0xDD, 0xDD)),
        };
        Grid.SetRow(titleBlock, 0);
        grid.Children.Add(titleBlock);

        var input = new System.Windows.Controls.TextBox
        {
            FontSize = 14,
            Margin = new Thickness(0, 12, 0, 12),
            Padding = new Thickness(8, 6, 8, 6),
            Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E)),
            Foreground = new SolidColorBrush(Color.FromRgb(0xDD, 0xDD, 0xDD)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            CaretBrush = new SolidColorBrush(Color.FromRgb(0xDD, 0xDD, 0xDD)),
        };
        Grid.SetRow(input, 1);
        grid.Children.Add(input);

        var btnPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        Grid.SetRow(btnPanel, 2);
        grid.Children.Add(btnPanel);

        var cancelBtn = new Button { Content = "Cancelar", Width = 80, Height = 30, Cursor = Cursors.Hand, FontSize = 13, Margin = new Thickness(0, 0, 8, 0) };
        cancelBtn.Style = MakeBtnStyle(Color.FromRgb(0xBB, 0xBB, 0xBB), Color.FromRgb(0x3A, 0x3A, 0x3A),
            Color.FromRgb(0xFF, 0xFF, 0xFF), Color.FromRgb(0x55, 0x55, 0x55));
        cancelBtn.Click += (_, _) => dialog.Close();
        btnPanel.Children.Add(cancelBtn);

        var okBtn = new Button { Content = "Crear", Width = 80, Height = 30, Cursor = Cursors.Hand, FontSize = 13 };
        okBtn.Style = MakeBtnStyle(Color.FromRgb(0xFF, 0xFF, 0xFF), Color.FromRgb(0x5A, 0x5A, 0x5A),
            Color.FromRgb(0xFF, 0xFF, 0xFF), Color.FromRgb(0x77, 0x77, 0x77));
        okBtn.Click += (_, _) =>
        {
            var name = input.Text.Trim();
            if (!string.IsNullOrEmpty(name))
            {
                store.Notebooks.Add(new Notebook { Name = name });
                store.Save();
                UpdateCounters();
                ShowStatus($"Libreta '{name}' creada", false);
            }
            dialog.Close();
        };
        btnPanel.Children.Add(okBtn);

        input.KeyDown += (_, e2) => { if (e2.Key == Key.Enter) okBtn.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); };

        border.Child = grid;
        dialog.Content = border;
        dialog.ShowDialog();
    }

    private void AddTag_Click(object sender, MouseButtonEventArgs e)
    {
        var dialog = new Window
        {
            Title = "Nuevo tag",
            Width = 340,
            Height = 180,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0)),
            Owner = this,
            ShowInTaskbar = false,
        };

        var border = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x26, 0x26, 0x26)),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(20),
        };

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var titleBlock = new TextBlock
        {
            Text = "Nuevo tag",
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(0xDD, 0xDD, 0xDD)),
        };
        Grid.SetRow(titleBlock, 0);
        grid.Children.Add(titleBlock);

        var input = new System.Windows.Controls.TextBox
        {
            FontSize = 14,
            Margin = new Thickness(0, 12, 0, 12),
            Padding = new Thickness(8, 6, 8, 6),
            Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E)),
            Foreground = new SolidColorBrush(Color.FromRgb(0xDD, 0xDD, 0xDD)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            CaretBrush = new SolidColorBrush(Color.FromRgb(0xDD, 0xDD, 0xDD)),
        };
        Grid.SetRow(input, 1);
        grid.Children.Add(input);

        var btnPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        Grid.SetRow(btnPanel, 2);
        grid.Children.Add(btnPanel);

        var cancelBtn = new Button { Content = "Cancelar", Width = 80, Height = 30, Cursor = Cursors.Hand, FontSize = 13, Margin = new Thickness(0, 0, 8, 0) };
        cancelBtn.Style = MakeBtnStyle(Color.FromRgb(0xBB, 0xBB, 0xBB), Color.FromRgb(0x3A, 0x3A, 0x3A),
            Color.FromRgb(0xFF, 0xFF, 0xFF), Color.FromRgb(0x55, 0x55, 0x55));
        cancelBtn.Click += (_, _) => dialog.Close();
        btnPanel.Children.Add(cancelBtn);

        var okBtn = new Button { Content = "Crear", Width = 80, Height = 30, Cursor = Cursors.Hand, FontSize = 13 };
        okBtn.Style = MakeBtnStyle(Color.FromRgb(0xFF, 0xFF, 0xFF), Color.FromRgb(0x5A, 0x5A, 0x5A),
            Color.FromRgb(0xFF, 0xFF, 0xFF), Color.FromRgb(0x77, 0x77, 0x77));
        okBtn.Click += (_, _) =>
        {
            var name = input.Text.Trim();
            if (!string.IsNullOrEmpty(name))
            {
                store.Tags.Add(new Tag { Name = name });
                store.Save();
                UpdateCounters();
                ShowStatus($"Tag '{name}' creado", false);
            }
            dialog.Close();
        };
        btnPanel.Children.Add(okBtn);

        input.KeyDown += (_, e2) => { if (e2.Key == Key.Enter) okBtn.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); };

        border.Child = grid;
        dialog.Content = border;
        dialog.ShowDialog();
    }

    private void ApplyFilters()
    {
        _view.Filter = obj =>
        {
            if (obj is not Note note) return false;

            // Parse section filter: base section + optional notebook/tag
            string baseSection = _activeSection;
            Guid? filterNotebook = null;
            Guid? filterTag = null;

            if (_activeSection.StartsWith("notebook:"))
            {
                baseSection = "notebook";
                if (Guid.TryParse(_activeSection.AsSpan(9), out var nbId))
                    filterNotebook = nbId;
            }
            else if (_activeSection.StartsWith("tag:"))
            {
                baseSection = "tag";
                if (Guid.TryParse(_activeSection.AsSpan(4), out var tId))
                    filterTag = tId;
            }

            // Base visibility filter
            bool baseMatch = baseSection switch
            {
                "all" => !note.IsArchived && !note.IsDeleted,
                "archived" => note.IsArchived && !note.IsDeleted,
                "trash" => note.IsDeleted,
                "timeline" => !note.IsArchived && !note.IsDeleted,
                "notebook" => !note.IsArchived && !note.IsDeleted,
                "tag" => !note.IsArchived && !note.IsDeleted,
                _ => !note.IsArchived && !note.IsDeleted
            };
            if (!baseMatch) return false;

            // Notebook filter
            if (filterNotebook.HasValue && note.NotebookId != filterNotebook.Value)
                return false;

            // Tag filter
            if (filterTag.HasValue && !note.TagIds.Contains(filterTag.Value))
                return false;

            // Search filter (if active)
            if (!string.IsNullOrEmpty(_searchFilter))
            {
                bool match = note.Title.Contains(_searchFilter, StringComparison.OrdinalIgnoreCase)
                    || note.PlainText.Contains(_searchFilter, StringComparison.OrdinalIgnoreCase);
                note.IsSearchMatch = match;
                return match;
            }

            note.IsSearchMatch = true;
            return true;
        };

        // Timeline: group by date, sort by date desc then time desc
        bool isTimeline = _activeSection == "timeline";

        _view.GroupDescriptions.Clear();
        _view.SortDescriptions.Clear();

        if (isTimeline)
        {
            _view.GroupDescriptions.Add(new PropertyGroupDescription("DateGroup"));
            _view.SortDescriptions.Add(new SortDescription("DateGroup", ListSortDirection.Descending));
            _view.SortDescriptions.Add(new SortDescription("LastModified", ListSortDirection.Descending));
        }
        else
        {
            // Normal sort: pinned first, then by last modified
            _view.SortDescriptions.Add(new SortDescription("IsPinned", ListSortDirection.Descending));
            _view.SortDescriptions.Add(new SortDescription("LastModified", ListSortDirection.Descending));
        }

        _view.Refresh();
        UpdateCounters();
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
        store.SidebarSection = _activeSection;
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

    internal static Style MakeEditableComboStyle()
    {
        // Use the editable style which has PART_EditableTextBox
        return (Style)System.Windows.Application.Current.FindResource("SettingsComboBoxEditableStyle");
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

public class DateGroupConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        if (value is not DateTime dt) return "";

        var today = DateTime.Today;
        if (dt == today) return "─── Hoy ───";
        if (dt == today.AddDays(-1)) return "─── Ayer ───";

        var daysDiff = (today - dt).Days;
        if (daysDiff > 0 && daysDiff <= 6)
            return $"─── {dt:dddd} ───";

        return $"─── {dt:dd MMM} ───";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotImplementedException();
}