using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using QuickNotes.Models;
using QuickNotes;
using System.Windows.Shapes;

namespace QuickNotes.Views;

public partial class NoteCard : UserControl
{
    private static string _searchFilter = "";
    private static event EventHandler? SearchFilterChanged;

    public static string SearchFilter
    {
        get => _searchFilter;
        set
        {
            if (_searchFilter == value) return;
            _searchFilter = value;
            SearchFilterChanged?.Invoke(null, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Global lookup: TagId -> Tag name. Populated by MainWindow on load.
    /// </summary>
    public static Dictionary<Guid, string> TagNameLookup { get; } = new();

    /// <summary>
    /// Global lookup: NotebookId -> Notebook name. Populated by MainWindow on load.
    /// </summary>
    public static Dictionary<Guid, string> NotebookNameLookup { get; } = new();

    /// <summary>
    /// Used by MainWindow's context menu handler to resolve the NoteCard.
    /// Set when context menu opens.
    /// </summary>
    internal static NoteCard? CurrentContextCard { get; set; }

    public NoteCard()
    {
        InitializeComponent();
        SearchFilterChanged += OnSearchFilterChanged;
        DataContextChanged += (_, _) =>
        {
            if (DataContext is Note oldNote)
                oldNote.PropertyChanged -= Note_PropertyChanged;
            if (DataContext is Note newNote)
                newNote.PropertyChanged += Note_PropertyChanged;
            RenderBodyWithHighlight();
        };
        Unloaded += (_, _) => SearchFilterChanged -= OnSearchFilterChanged;
    }

    private void OnSearchFilterChanged(object? sender, EventArgs e)
    {
        RenderBodyWithHighlight();
    }

    // === Routed events for MainWindow to handle ===

    public static readonly RoutedEvent PinToggleEvent =
        EventManager.RegisterRoutedEvent("PinToggle", RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(NoteCard));
    public event RoutedEventHandler PinToggle
    { add => AddHandler(PinToggleEvent, value); remove => RemoveHandler(PinToggleEvent, value); }

    public static readonly RoutedEvent ColorChangedEvent =
        EventManager.RegisterRoutedEvent("ColorChanged", RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(NoteCard));
    public event RoutedEventHandler ColorChanged
    { add => AddHandler(ColorChangedEvent, value); remove => RemoveHandler(ColorChangedEvent, value); }

    public static readonly RoutedEvent TitleChangedEvent =
        EventManager.RegisterRoutedEvent("TitleChanged", RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(NoteCard));
    public event RoutedEventHandler TitleChanged
    { add => AddHandler(TitleChangedEvent, value); remove => RemoveHandler(TitleChangedEvent, value); }

    public static readonly RoutedEvent ContextMenuActionEvent =
        EventManager.RegisterRoutedEvent("ContextMenuAction", RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(NoteCard));
    public event RoutedEventHandler ContextMenuAction
    { add => AddHandler(ContextMenuActionEvent, value); remove => RemoveHandler(ContextMenuActionEvent, value); }

    private void Note_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Note.Color))
            RebuildTagPills();
    }

    // === Initialization ===

    private void Card_Loaded(object sender, RoutedEventArgs e)
    {
        var fade = AnimationHelper.MakeAnimation(0, 1, 220);
        fade.EasingFunction = new QuadraticEase();
        BeginAnimation(OpacityProperty, fade);

        if (DataContext is Note note)
        {
            var pinBtn = FindVisualChild<Button>(this);
            if (pinBtn != null)
                UpdatePinVisual(pinBtn, note, false);
        }

        UpdateReminderBadge();
        RebuildTagPills();
        RenderBodyWithHighlight();
    }

    private void UpdateReminderBadge()
    {
        if (DataContext is not Note note) return;
        if (Application.Current.MainWindow is MainWindow mw)
        {
            var hasReminder = mw.GetStore().GetRemindersForNote(note.Id).Any(r => !r.IsCompleted);
            // Find or create the badge Path in the card
            var badge = FindVisualChild<System.Windows.Shapes.Path>(this, p => p.Name == "reminderBadge");
            if (badge == null)
            {
                // Create programmatically — attach to the header grid
                if (FindVisualChild<Ellipse>(this) is Ellipse ellipse && VisualTreeHelper.GetParent(ellipse) is Grid headerGrid)
                {
                    badge = new System.Windows.Shapes.Path
                    {
                        Name = "reminderBadge",
                        Data = (System.Windows.Media.StreamGeometry)FindResource("IconBell"),
                        Stretch = System.Windows.Media.Stretch.Uniform,
                        Width = 10,
                        Height = 10,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(0, 0, 4, 0),
                        IsHitTestVisible = false,
                        Fill = (System.Windows.Media.Brush)FindResource("SidebarIconBrush"),
                        Visibility = Visibility.Collapsed,
                    };
                    Grid.SetColumn(badge, 1);
                    headerGrid.Children.Add(badge);
                }
            }
            if (badge != null)
                badge.Visibility = hasReminder ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private static T? FindVisualChild<T>(DependencyObject? parent, Func<T, bool>? predicate = null) where T : DependencyObject
    {
        if (parent == null) return null;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T t && (predicate == null || predicate(t)))
                return t;
            var found = FindVisualChild(child, predicate);
            if (found != null) return found;
        }
        return null;
    }

    private void RebuildTagPills()
    {
        tagsPanel.Children.Clear();
        tagsPanel.Visibility = Visibility.Collapsed;

        if (DataContext is not Note note || note.TagIds.Count == 0) return;

        var isDark = IsDarkBg(note.Color);
        var pillBg = isDark
            ? new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF))
            : new SolidColorBrush(Color.FromArgb(0x30, 0x00, 0x00, 0x00));
        var pillFg = isDark
            ? new SolidColorBrush(Color.FromArgb(0xBB, 0xFF, 0xFF, 0xFF))
            : new SolidColorBrush(Color.FromArgb(0xBB, 0x3A, 0x3A, 0x3A));

        foreach (var tagId in note.TagIds)
        {
            if (TagNameLookup.TryGetValue(tagId, out var tagName))
            {
                var pill = new Border
                {
                    Background = pillBg,
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(5, 1, 5, 1),
                    Margin = new Thickness(0, 0, 3, 0),
                    Child = new TextBlock
                    {
                        Text = "#" + tagName,
                        FontSize = 10,
                        Foreground = pillFg,
                    }
                };
                tagsPanel.Children.Add(pill);
            }
        }
        tagsPanel.Visibility = Visibility.Visible;
    }

    private static bool IsDarkBg(string? hex)
    {
        if (string.IsNullOrEmpty(hex) || hex.Length < 7) return false;
        try
        {
            var r = Convert.ToByte(hex.Substring(1, 2), 16);
            var g = Convert.ToByte(hex.Substring(3, 2), 16);
            var b = Convert.ToByte(hex.Substring(5, 2), 16);
            return (r * 0.299 + g * 0.587 + b * 0.114) < 128;
        }
        catch { return false; }
    }

    private void RenderBodyWithHighlight()
    {
        cardBodyBlock.Inlines.Clear();

        if (DataContext is not Note note) return;
        var text = note.PlainText ?? "";
        var filter = _searchFilter;

        if (string.IsNullOrEmpty(filter) || text.Length == 0)
        {
            cardBodyBlock.Inlines.Add(new Run(text));
            return;
        }

        // Split text around the search filter and highlight matches
        var highlightBg = new SolidColorBrush(Color.FromArgb(0x44, 0xFF, 0xD7, 0x00)); // translucent yellow

        int idx = 0;
        while (idx < text.Length)
        {
            var matchIdx = text.IndexOf(filter, idx, StringComparison.OrdinalIgnoreCase);
            if (matchIdx < 0)
            {
                // No more matches - remaining text
                cardBodyBlock.Inlines.Add(new Run(text.Substring(idx)));
                break;
            }

            // Text before match
            if (matchIdx > idx)
                cardBodyBlock.Inlines.Add(new Run(text.Substring(idx, matchIdx - idx)));

            // Highlighted match
            cardBodyBlock.Inlines.Add(new Run(text.Substring(matchIdx, filter.Length))
            {
                Background = highlightBg,
                FontWeight = FontWeights.SemiBold
            });

            idx = matchIdx + filter.Length;
        }
    }

    // === Pin button ===

    private void PinIndicator_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is Note note)
        {
            note.IsPinned = !note.IsPinned;
            note.IsMimetized = false;
            UpdatePinVisual((Button)sender, note, true);
            RaiseEvent(new RoutedEventArgs(PinToggleEvent));
        }
    }

    private void UpdatePinVisual(Button btn, Note note, bool cardHovered)
    {
        var dash = btn.Template.FindName("dashText", btn) as TextBlock;
        var hover = btn.Template.FindName("pinHoverText", btn) as Path;
        var pin = btn.Template.FindName("pinText", btn) as Path;
        if (dash == null || hover == null || pin == null) return;

        double toDash = 0, toHover = 0, toPin = 0;
        if (note.IsPinned) { toPin = 1; }
        else if (cardHovered) { toHover = 1; }
        else { toDash = 1; }

        dash.BeginAnimation(OpacityProperty, AnimationHelper.MakeAnimation(toDash, 150));
        hover.BeginAnimation(OpacityProperty, AnimationHelper.MakeAnimation(toHover, 150));
        pin.BeginAnimation(OpacityProperty, AnimationHelper.MakeAnimation(toPin, 150));
    }

    // === Title ===

    private void Title_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is TextBox { IsLoaded: false }) return;
        if (DataContext is Note note)
        {
            note.IsDirty = true;
            RaiseEvent(new RoutedEventArgs(TitleChangedEvent));
        }
    }

    // === Color picker ===

    private void CardCurrentColor_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe)
        {
            var popup = fe.FindName("cardCurrentColor") is Border b
                ? (b.Parent as FrameworkElement)?.FindName("") as Popup
                : null;
            // Use the UserControl's popup directly
            var cardPopup = FindVisualChild<Popup>(this);            if (cardPopup != null)
                cardPopup.IsOpen = !cardPopup.IsOpen;
        }
    }

    private void ColorDot_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border dot && dot.Tag is string color && DataContext is Note note)
        {
            note.Color = color;
            note.IsDirty = true;
            RaiseEvent(new RoutedEventArgs(ColorChangedEvent));

            // Close popup
            var popup = FindVisualChild<Popup>(this);            if (popup != null)
                popup.IsOpen = false;
        }
    }

    // === Context menu ===

    private void MainBorder_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (DataContext is not Note note || mainBorder.ContextMenu == null) return;
        CurrentContextCard = this;

        // Determine current view state
        bool isDeleted = note.IsDeleted;
        bool isArchived = note.IsArchived;

        foreach (var item in mainBorder.ContextMenu.Items)
        {
            if (item is MenuItem mi)
            {
                switch (mi.Tag?.ToString())
                {
                    case "Duplicate":
                        mi.Visibility = isDeleted ? Visibility.Collapsed : Visibility.Visible;
                        break;

                    case "Delete":
                        if (isDeleted)
                        {
                            mi.Header = "Eliminar permanentemente";
                            mi.Tag = "PermanentDelete";
                        }
                        else
                        {
                            mi.Header = "Eliminar";
                            mi.Tag = "Delete";
                        }
                        break;

                    case "Archive":
                        mi.Visibility = isDeleted ? Visibility.Collapsed : Visibility.Visible;
                        mi.Header = isArchived ? "Desarchivar" : "Archivar";
                        break;

                    case "Restore":
                        // Only show in papelera — Desarchivar handles archivadas
                        mi.Visibility = isDeleted ? Visibility.Visible : Visibility.Collapsed;
                        break;

                    case "Pin":
                        mi.Visibility = isDeleted ? Visibility.Collapsed : Visibility.Visible;
                        mi.Header = note.IsPinned ? "Desanclar" : "Anclar";
                        break;

                    case "NotebookSubmenu":
                        mi.Visibility = isDeleted ? Visibility.Collapsed : Visibility.Visible;
                        break;
                    case "TagsSubmenu":
                        mi.Visibility = isDeleted ? Visibility.Collapsed : Visibility.Visible;
                        break;
                    case "Reminder":
                        mi.Visibility = isDeleted ? Visibility.Collapsed : Visibility.Visible;
                        if (note != null && Application.Current.MainWindow is MainWindow mw)
                        {
                            var hasReminder = mw.GetStore().GetRemindersForNote(note.Id).Any(r => !r.IsCompleted);
                            var headerPanel = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
                            headerPanel.Children.Add(new System.Windows.Shapes.Path
                            {
                                Data = (System.Windows.Media.StreamGeometry)Application.Current.FindResource("IconBell"),
                                Stretch = System.Windows.Media.Stretch.Uniform,
                                Width = 12,
                                Height = 12,
                                Fill = System.Windows.Media.Brushes.White,
                                VerticalAlignment = VerticalAlignment.Center,
                                Margin = new Thickness(0, 0, 6, 0)
                            });
                            headerPanel.Children.Add(new System.Windows.Controls.TextBlock
                            {
                                Text = hasReminder ? "Editar recordatorio" : "Recordatorio",
                                VerticalAlignment = VerticalAlignment.Center
                            });
                            mi.Header = headerPanel;
                        }
                        break;
                }
            }
        }

        // Hide separators if surrounded by hidden items
        bool midVisible = notebookSubmenu.Visibility == Visibility.Visible
            || tagsSubmenu.Visibility == Visibility.Visible;
        sep1.Visibility = midVisible ? Visibility.Visible : Visibility.Collapsed;
        sep2.Visibility = midVisible ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ContextMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi)
        {
            var tag = mi.Tag?.ToString() ?? "(null)";

            switch (tag)
            {
                case "NotebookSubmenu":
                    ShowNotebookSelector();
                    return;
                case "TagsSubmenu":
                    ShowTagSelector();
                    return;
            }

            // Delegate notebook/tag commands to MainWindow
            if (tag == "notebook:" || tag.StartsWith("notebook:") || tag.StartsWith("tagtoggle:"))
            {
                if (DataContext is Note note && Application.Current.MainWindow is MainWindow mw)
                {
                    if (tag.StartsWith("notebook:"))
                    {
                        var idStr = tag.AsSpan(9);
                        note.NotebookId = idStr.Length == 0 ? null : Guid.Parse(idStr);
                        note.IsDirty = true;
                    }
                    else if (tag.StartsWith("tagtoggle:"))
                    {
                        var tId = Guid.Parse(tag.AsSpan(10));
                        if (note.TagIds.Contains(tId))
                            note.TagIds.Remove(tId);
                        else
                            note.TagIds.Add(tId);
                        note.IsDirty = true;
                    }
                    mw.HandleNoteAction(note, tag);
                }
                return;
            }

            // For standard items, bubble the event as before
            var args = new RoutedEventArgs(ContextMenuActionEvent, mi);
            RaiseEvent(args);
        }
    }

    private void ShowNotebookSelector()
    {
        if (DataContext is not Note note) return;
        if (Application.Current.MainWindow is not MainWindow mw) return;

        var dialog = new Window
        {
            Title = "Mover a libreta",
            Width = 280,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0)),
            Owner = mw,
            ShowInTaskbar = false,
        };

        var border = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x26, 0x26, 0x26)),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14),
        };

        var stack = new StackPanel();

        var label = new TextBlock
        {
            Text = "Mover a libreta",
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(0xBB, 0xBB, 0xBB)),
            Margin = new Thickness(0, 0, 0, 6),
        };
        stack.Children.Add(label);

        var items = new List<NotebookItem> { new() { Id = null, Display = "Sin libreta" } };
        foreach (var kvp in NotebookNameLookup)
            items.Add(new NotebookItem { Id = kvp.Key, Display = kvp.Value });

        var combo = new ComboBox
        {
            IsEditable = true,
            ItemsSource = items,
            DisplayMemberPath = "Display",
            SelectedIndex = 0,
            Style = MainWindow.MakeEditableComboStyle(),
        };

        combo.PreviewMouseDown += (_, _) =>
        {
            if (!combo.IsDropDownOpen)
                combo.IsDropDownOpen = true;
        };

        combo.GotKeyboardFocus += (_, _) =>
        {
            combo.IsDropDownOpen = true;
        };

        // Filter while typing
        combo.Loaded += (_, _) =>
        {
            if (combo.Template.FindName("PART_EditableTextBox", combo) is TextBox tb)
            {
                tb.TextChanged += (_, _) =>
                {
                    var filter = tb.Text.Trim();
                    combo.ItemsSource = string.IsNullOrEmpty(filter)
                        ? items
                        : items.Where(i => i.Display != null && i.Display.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();
                };
            }
        };

        // Commit from text match on close or Enter
        void CommitNotebookFromText()
        {
            var text = combo.Text?.Trim();
            if (string.IsNullOrEmpty(text)) return;
            var match = items.FirstOrDefault(i => i.Display != null && i.Display.Equals(text, StringComparison.OrdinalIgnoreCase));
            if (match != null)
            {
                note.NotebookId = match.Id;
                note.IsDirty = true;
                mw.HandleNoteAction(note, "notebook:");
            }
        }

        combo.DropDownClosed += (_, _) => CommitNotebookFromText();

        combo.KeyDown += (_, e2) =>
        {
            if (e2.Key == Key.Enter)
            {
                CommitNotebookFromText();
                dialog.Close();
                e2.Handled = true;
            }
            else if (e2.Key == Key.Escape)
            {
                dialog.Close();
                e2.Handled = true;
            }
        };

        stack.Children.Add(combo);
        border.Child = stack;
        dialog.Content = border;
        dialog.ShowDialog();
    }

    private void ShowTagSelector()
    {
        if (DataContext is not Note note) return;
        if (Application.Current.MainWindow is not MainWindow mw) return;

        var dialog = new Window
        {
            Title = "Asignar tags",
            Width = 280,
            SizeToContent = SizeToContent.Height,
            MinHeight = 80,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0)),
            Owner = mw,
            ShowInTaskbar = false,
        };

        var border = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x26, 0x26, 0x26)),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14),
        };

        var rootGrid = new Grid();
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // pills
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // combo
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // listo btn

        // Pills wrap panel
        var pillsWrap = new WrapPanel { Margin = new Thickness(0, 0, 0, 6), MaxWidth = 252 };

        void RebuildPills()
        {
            pillsWrap.Children.Clear();
            foreach (var tagId in note.TagIds.ToList())
            {
                if (!TagNameLookup.TryGetValue(tagId, out var tagName)) continue;
                var pill = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(0x40, 0x88, 0xFF, 0xFF)),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(5, 1, 5, 1),
                    Margin = new Thickness(0, 0, 3, 3),
                    Cursor = Cursors.Hand,
                    Tag = tagId,
                };
                var pillGrid = new Grid();
                pillGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                pillGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var txt = new TextBlock
                {
                    Text = $"#{tagName}",
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromRgb(0xDD, 0xDD, 0xDD)),
                    VerticalAlignment = VerticalAlignment.Center,
                };
                Grid.SetColumn(txt, 0);
                pillGrid.Children.Add(txt);

                var xTxt = new TextBlock
                {
                    Text = " ✕",
                    FontSize = 10,
                    Foreground = new SolidColorBrush(Color.FromArgb(0x88, 0xFF, 0xFF, 0xFF)),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(3, 0, 0, 0),
                };
                Grid.SetColumn(xTxt, 1);
                pillGrid.Children.Add(xTxt);

                pill.Child = pillGrid;

                var captured = tagId;
                pill.MouseDown += (_, _) =>
                {
                    note.TagIds.Remove(captured);
                    note.IsDirty = true;
                    RebuildPills();
                };
                pillsWrap.Children.Add(pill);
            }
        }
        RebuildPills();
        Grid.SetRow(pillsWrap, 0);
        rootGrid.Children.Add(pillsWrap);

        // Tag combo with autocomplete
        List<TagSuggestion> GetSuggestions(string filter)
        {
            return TagNameLookup
                .Where(kvp => !note.TagIds.Contains(kvp.Key) &&
                    (string.IsNullOrEmpty(filter) || kvp.Value.Contains(filter, StringComparison.OrdinalIgnoreCase)))
                .Select(kvp => new TagSuggestion { Id = kvp.Key, Name = kvp.Value })
                .ToList();
        }

        var combo = new ComboBox
        {
            IsEditable = true,
            IsTextSearchEnabled = false,
            StaysOpenOnEdit = true,
            Style = MainWindow.MakeEditableComboStyle(),
            ItemsSource = GetSuggestions(""),
            DisplayMemberPath = "Name",
        };

        // Refresh items when dropdown opens (pills may have changed)
        combo.DropDownOpened += (_, _) =>
        {
            combo.ItemsSource = TagNameLookup
                .Where(kvp => !note.TagIds.Contains(kvp.Key))
                .Select(kvp => new TagSuggestion { Id = kvp.Key, Name = kvp.Value })
                .ToList();
        };

        combo.PreviewMouseDown += (_, _) =>
        {
            if (!combo.IsDropDownOpen)
                combo.IsDropDownOpen = true;
        };

        combo.GotKeyboardFocus += (_, _) =>
        {
            combo.IsDropDownOpen = true;
        };

        // Commit tag from current text (matches by name against lookup)
        void CommitTagFromText()
        {
            var text = combo.Text?.Trim();
            if (string.IsNullOrEmpty(text)) return;
            var match = TagNameLookup.FirstOrDefault(kvp =>
                kvp.Value.Equals(text, StringComparison.OrdinalIgnoreCase));
            if (match.Key != Guid.Empty && !note.TagIds.Contains(match.Key))
            {
                note.TagIds.Add(match.Key);
                note.IsDirty = true;
                RebuildPills();
                // Clear text asynchronously to avoid re-entrant crash
                System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() =>
                {
                    if (combo.Template.FindName("PART_EditableTextBox", combo) is TextBox tb)
                        tb.Text = "";
                    combo.ItemsSource = GetSuggestions("");
                }), System.Windows.Threading.DispatcherPriority.Background);
            }
        }

        // When dropdown closes (user clicked an item), commit the text
        combo.DropDownClosed += (_, _) => CommitTagFromText();

        // Wire text filtering and Enter
        combo.Loaded += (_, _) =>
        {
            if (combo.Template.FindName("PART_EditableTextBox", combo) is TextBox tb)
            {
                tb.TextChanged += (_, _) =>
                {
                    var filter = tb.Text.Trim();
                    combo.ItemsSource = GetSuggestions(filter);
                };

                tb.KeyDown += (_, e2) =>
                {
                    if (e2.Key == Key.Enter)
                    {
                        CommitTagFromText();
                        e2.Handled = true;
                    }
                };
            }
        };

        Grid.SetRow(combo, 1);
        rootGrid.Children.Add(combo);

        // Listo button
        var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
        Grid.SetRow(btnPanel, 2);
        rootGrid.Children.Add(btnPanel);

        var okBtn = new Button { Content = "Listo", Width = 70, Height = 28, Cursor = Cursors.Hand, FontSize = 13 };
        okBtn.Style = MainWindow.MakeBtnStyle(Color.FromRgb(0xFF, 0xFF, 0xFF), Color.FromRgb(0x5A, 0x5A, 0x5A),
            Color.FromRgb(0xFF, 0xFF, 0xFF), Color.FromRgb(0x77, 0x77, 0x77));
        okBtn.Click += (_, _) =>
        {
            mw.HandleNoteAction(note, "tagtoggle:");
            dialog.Close();
        };
        btnPanel.Children.Add(okBtn);

        border.Child = rootGrid;
        dialog.Content = border;
        dialog.ShowDialog();
    }

    private class NotebookItem
    {
        public Guid? Id { get; set; }
        public string? Display { get; set; }
    }

    private class TagSuggestion
    {
        public Guid Id { get; set; }
        public string? Name { get; set; }
    }

    // === Helpers ===

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T t) return t;
            var found = FindVisualChild<T>(child);
            if (found != null) return found;
        }
        return null;
    }
}

