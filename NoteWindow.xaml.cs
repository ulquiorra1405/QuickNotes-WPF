using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using QuickNotes.Models;

namespace QuickNotes;

public partial class NoteWindow : Window
{
    public const double DefaultWidth = 340;
    public const double DefaultHeight = 260;

    /// <summary>
    /// Set to true during app shutdown to preserve positions.
    /// </summary>
    internal static bool IsAppShuttingDown;

    private static readonly string _imageFolder = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "QuickNotes", "images");

    private readonly Note _note;
    private readonly NotesStore _store;

    private readonly DispatcherTimer _selectionTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(350),
    };

    private string _searchQuery = "";

    private Color _currentHighlightColor = Color.FromArgb(0x55, 0xFF, 0xD7, 0x00);

    private static readonly Color[] HighlightColors =
    [
        Color.FromArgb(0x55, 0xFF, 0xD7, 0x00), // Yellow
        Color.FromArgb(0x55, 0x55, 0xFF, 0x55), // Green
        Color.FromArgb(0x55, 0x55, 0xAA, 0xFF), // Blue
        Color.FromArgb(0x55, 0xFF, 0x55, 0xFF), // Pink
        Color.FromArgb(0x55, 0xFF, 0x88, 0x00), // Orange
        Color.FromArgb(0x55, 0x88, 0x55, 0xFF), // Purple
        Color.FromArgb(0x55, 0x00, 0xDD, 0xFF), // Cyan
        Color.FromArgb(0x55, 0xFF, 0x55, 0x55), // Red
    ];

    private static readonly Color _noHighlightColor = Color.FromArgb(0x00, 0x00, 0x00, 0x00);



    public NoteWindow(Note note, NotesStore? store = null)
    {
        InitializeComponent();
        _note = note;
        _store = store ?? new NotesStore();

        DataContext = note;

        // Init emoji button
        emojiBtn.Content = note.Icon;
        BuildEmojiPicker();
        if (_store.NoteFontSize != 13)
            noteText.FontSize = _store.NoteFontSize;
        if (!string.IsNullOrEmpty(_store.NoteFontFamily) && _store.NoteFontFamily != "Calibri")
            noteText.FontFamily = new FontFamily(_store.NoteFontFamily);
        AnimationHelper.Enabled = _store.AnimationsEnabled;

        if (!double.IsNaN(note.WinLeft) && note.WinLeft > 0) Left = note.WinLeft;
        if (!double.IsNaN(note.WinTop) && note.WinTop > 0) Top = note.WinTop;
        if (!double.IsNaN(note.WinWidth) && note.WinWidth > 0) Width = note.WinWidth;
        if (!double.IsNaN(note.WinHeight) && note.WinHeight > 0) Height = note.WinHeight;

        // Ensure window is visible on some monitor
        var r = MonitorHelper.ClampToScreen(Left, Top, Width, Height);
        Left = r.Left;
        Top = r.Top;

        Opacity = 0;
        Loaded += (_, _) =>
        {
            BeginAnimation(OpacityProperty, AnimationHelper.MakeAnimation(0, 1, 200));
            LoadRichText();
            UpdateButtonForegrounds();
            FormatUrlsInDocument();
        };

        Activated += (_, _) => ToggleBars(show: true);
        Deactivated += (_, _) => ToggleBars(show: false);
        SourceInitialized += (_, _) =>
        {
            var source = (HwndSource?)PresentationSource.FromVisual(this);
            source?.AddHook(WndProc);
        };
        PreviewKeyDown += NoteWindow_PreviewKeyDown;
        PreviewMouseDown += (_, e) =>
        {
            bool anyOpen = colorPopup.IsOpen || emojiPopup.IsOpen || formatPopup.IsOpen;
            if (!anyOpen) return;

            bool clickedTrigger = false;
            bool insideEditor = false;

            if (e.OriginalSource is DependencyObject src)
            {
                clickedTrigger =
                    (colorPopup.IsOpen && FindParent<Border>(src) == currentColorDot) ||
                    (emojiPopup.IsOpen && FindParent<Button>(src) == emojiBtn);
                insideEditor = FindParent<RichTextBox>(src) == noteText;

            }

            if (clickedTrigger) return;

            colorPopup.IsOpen = false;
            emojiPopup.IsOpen = false;

            // Close formatPopup only if click is outside it AND outside editor
            if (formatPopup.IsOpen && !insideEditor)
            {
                bool clickedOnToolbar = false;
                if (e.OriginalSource is DependencyObject s)
                {
                    var sp = FindParent<StackPanel>(s);
                    clickedOnToolbar = sp == formatToolbar || sp == floatHighlightPicker || sp == floatHeadingPicker;
                }
                if (!clickedOnToolbar)
                    HideFormatPopup();
            }
        };

        // Image paste via NoteWindow_PreviewKeyDown
        noteText.AllowDrop = true;
        noteText.Drop += NoteText_Drop;
        noteText.PreviewDragOver += NoteText_PreviewDragOver;

        // Auto-pairing
        PreviewTextInput += NoteWindow_PreviewTextInput;

        // Floating toolbar timer
        _selectionTimer.Tick += SelectionTimer_Tick;

        // Right-click context menu
        noteText.ContextMenuOpening += NoteText_ContextMenuOpening;
    }

    private void NoteText_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        var files = (string[]?)e.Data.GetData(DataFormats.FileDrop);
        if (files == null || files.Length == 0) return;
        var validExts = new HashSet<string> { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp" };
        foreach (var file in files)
        {
            var ext = System.IO.Path.GetExtension(file).ToLowerInvariant();
            if (validExts.Contains(ext))
            {
                InsertImageFromFile(file);
                e.Handled = true;
            }
        }
    }

    private void NoteText_PreviewDragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop) ||
            e.Data.GetDataPresent(DataFormats.Bitmap))
        {
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_NCLBUTTONDBLCLK = 0x00A3;
        const int HTCAPTION = 2;

        if (msg == WM_NCLBUTTONDBLCLK && (int)wParam == HTCAPTION)
        {
            handled = true;
            Dispatcher.BeginInvoke(() => EnterEditMode());
            return IntPtr.Zero;
        }

        return IntPtr.Zero;
    }

    private void LoadRichText()
    {
        var text = _note.Text ?? "";
        if (string.IsNullOrEmpty(text)) return;

        try
        {
            var doc = (System.Windows.Documents.FlowDocument)System.Windows.Markup.XamlReader.Parse(text);
            noteText.Document = doc;
        }
        catch
        {
            // Fallback: plain text (notes from before XAML migration)
            var para = new Paragraph(new Run(text));
            noteText.Document.Blocks.Clear();
            noteText.Document.Blocks.Add(para);
        }
    }

    private void SaveRichText()
    {
        using var ms = new MemoryStream();
        var xaml = System.Windows.Markup.XamlWriter.Save(noteText.Document);
        _note.Text = xaml;
        _note.LastModified = DateTime.Now;
    }

    private void ToggleBars(bool show)
    {
        titleBar.BeginAnimation(HeightProperty, AnimationHelper.MakeAnimation(show ? 30 : 18, 200));
        titleRightPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (!show) noteSearchBorder.Visibility = Visibility.Collapsed;

        colorBar.BeginAnimation(OpacityProperty, AnimationHelper.MakeAnimation(show ? 1 : 0, 200));
        colorBar.BeginAnimation(HeightProperty, AnimationHelper.MakeAnimation(show ? 32 : 0, 200));

        noteText.VerticalScrollBarVisibility = show ? ScrollBarVisibility.Auto : ScrollBarVisibility.Hidden;
        if (show) ApplyScrollbarReversal(noteText, _note.Color);
    }

    private void NoteText_TextChanged(object sender, TextChangedEventArgs e)
    {
        _note.IsDirty = true;
        _note.LastModified = DateTime.Now;
        var mainWin = Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w is MainWindow) as MainWindow;
        mainWin?.DebounceSave();
    }

    private void Title_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is TextBox { IsLoaded: false }) return;
        _note.IsDirty = true;
        _note.LastModified = DateTime.Now;
        _store.Save();
        _note.IsDirty = false;
    }

    private void TitleInput_LostFocus(object sender, RoutedEventArgs e)
    {
        CommitTitleEdit();
    }

    private void TitleInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            CommitTitleEdit();
            e.Handled = true;
        }
    }

    private void CommitTitleEdit()
    {
        titleInput.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        titleInput.Visibility = Visibility.Collapsed;
        titleDisplay.Visibility = Visibility.Visible;
    }

    private void Text_LostFocus(object sender, RoutedEventArgs e)
    {
        SaveRichText();
        _note.IsDirty = false;
        _store.Save();
    }

    private void FormatUrlsInDocument()
    {
        if (_isUpdatingUrlFormats) return;
        _isUpdatingUrlFormats = true;

        var linkBrush = new SolidColorBrush(Color.FromArgb(0xFF, 0x60, 0xA0, 0xFF));

        try
        {
            var blocks = new List<Block>(noteText.Document.Blocks);
            foreach (var block in blocks)
            {
                if (block is not Paragraph para) continue;

                var inlines = new List<Inline>(para.Inlines);
                foreach (var inline in inlines)
                {
                    if (inline is not Run run || string.IsNullOrEmpty(run.Text)) continue;

                    var matches = _urlRegex.Matches(run.Text);
                    if (matches.Count == 0) continue;

                    int offset = 0;
                    foreach (System.Text.RegularExpressions.Match m in matches)
                    {
                        if (m.Index > offset)
                        {
                            var beforeRun = new Run(run.Text.Substring(offset, m.Index - offset));
                            para.Inlines.InsertBefore(run, beforeRun);
                        }

                        var urlRun = new Run(m.Value)
                        {
                            Foreground = linkBrush,
                            TextDecorations = TextDecorations.Underline
                        };
                        para.Inlines.InsertBefore(run, urlRun);

                        offset = m.Index + m.Length;
                    }

                    if (offset < run.Text.Length)
                    {
                        var afterRun = new Run(run.Text.Substring(offset));
                        para.Inlines.InsertBefore(run, afterRun);
                    }

                    para.Inlines.Remove(run);
                }
            }
        }
        finally
        {
            _isUpdatingUrlFormats = false;
        }
    }

    private void NoteText_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not RichTextBox tb) return;
        ApplyScrollbarReversal(tb, _note.Color);
    }

    private static void ApplyScrollbarReversal(DependencyObject? parent, string? noteColor)
    {
        if (parent == null) return;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is ScrollBar sb)
            {
                sb.Width = 5;
                if (sb.Template?.FindName("PART_Track", sb) is Track track)
                {
                    track.IsDirectionReversed = true;
                    if (track.Thumb?.Template?.FindName("bg", track.Thumb) is Border bg)
                    {
                        var dark = IsDarkColor(noteColor);
                        var thumbColor = dark
                            ? Color.FromArgb(0x88, 0xFF, 0xFF, 0xFF)
                            : Color.FromArgb(0x66, 0x00, 0x00, 0x00);
                        bg.Background = new SolidColorBrush(thumbColor);
                        bg.Margin = new Thickness(0, 1, 0, 1);
                    }
                }
                return;
            }
            ApplyScrollbarReversal(child, noteColor);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        SaveRichText();
        _note.LastModified = DateTime.Now;
        _note.IsDirty = false;
        var fade = AnimationHelper.MakeAnimation(0, 150);
        fade.Completed += (_, _) =>
        {
            _store.Save();
            Close();
        };
        BeginAnimation(OpacityProperty, fade);
    }

    private void EnterEditMode()
    {
        if (titleInput.Visibility == Visibility.Visible) return;
        titleDisplay.Visibility = Visibility.Collapsed;
        titleInput.Visibility = Visibility.Visible;
        titleInput.CaretBrush = IsDarkColor(_note.Color) ? Brushes.White : Brushes.Black;
        titleInput.Focus();
        titleInput.SelectAll();
    }

    private static bool IsDarkColor(string? hex)
    {
        if (string.IsNullOrEmpty(hex) || hex.Length < 7) return false;
        var r = Convert.ToInt32(hex.Substring(1, 2), 16);
        var g = Convert.ToInt32(hex.Substring(3, 2), 16);
        var b = Convert.ToInt32(hex.Substring(5, 2), 16);
        return (0.299 * r + 0.587 * g + 0.114 * b) < 140;
    }

    private static Color ParseColor(string? hex)
    {
        if (!string.IsNullOrEmpty(hex) && hex.Length >= 7)
        {
            try
            {
                var r = Convert.ToByte(hex.Substring(1, 2), 16);
                var g = Convert.ToByte(hex.Substring(3, 2), 16);
                var b = Convert.ToByte(hex.Substring(5, 2), 16);
                return Color.FromArgb(0xFF, r, g, b);
            }
            catch { }
        }
        return Color.FromArgb(0xFF, 0x3A, 0x3A, 0x3A);
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        SaveRichText();
        _note.IsDirty = false;
        // Reset position on individual close (not during app shutdown)
        if (!IsAppShuttingDown)
        {
            _note.WinLeft = double.NaN;
            _note.WinTop = double.NaN;
        }
        _store.Save();
        base.OnClosing(e);

        // Refresh dock indicators (safe after close)
        try
        {
            foreach (Window w in Application.Current.Windows)
                if (w is Views.DockWindow dw)
                    dw.Dispatcher.BeginInvoke(() => dw.RefreshNotes());
        }
        catch { }
    }

    private static T? FindParent<T>(DependencyObject child) where T : DependencyObject
    {
        while (child != null)
        {
            if (child is T t) return t;
            if (child is Visual)
                child = VisualTreeHelper.GetParent(child);
            else
                break;
        }
        return null;
    }

    private void PinNote_Click(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton btn)
            Topmost = btn.IsChecked == true;
    }

    private void MinimizeNote_Click(object sender, RoutedEventArgs e)
    {
        SaveRichText();
        _note.WinLeft = Left;
        _note.WinTop = Top;
        _note.WinWidth = Width;
        _note.WinHeight = Height;
        _note.LastModified = DateTime.Now;
        _note.IsDirty = false;
        _store.Save();
        Hide();

        // Notify dock to refresh indicators
        foreach (Window w in Application.Current.Windows)
            if (w is Views.DockWindow dw)
                dw.RefreshNotes();
    }

    public void OnTabOpened()
    {
        if (!double.IsNaN(_note.WinLeft)) Left = _note.WinLeft;
        if (!double.IsNaN(_note.WinTop)) Top = _note.WinTop;
        if (!double.IsNaN(_note.WinWidth)) Width = _note.WinWidth;
        if (!double.IsNaN(_note.WinHeight)) Height = _note.WinHeight;
    }

    private void ColorDot_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border border && border.Tag is string color)
        {
            _note.Color = color;
            _note.LastModified = DateTime.Now;
            _note.IsDirty = false;
            _store.Save();
            FlashElement(border);
            colorPopup.IsOpen = false;
            UpdateButtonForegrounds();
        }
    }

    private void CurrentColorDot_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
            colorPopup.IsOpen = !colorPopup.IsOpen;
    }

    private void BuildEmojiPicker()
    {
        emojiPanel.Children.Clear();
        foreach (var emoji in Note.EmojiPalette)
        {
            var border = new Border
            {
                Height = 28,
                CornerRadius = new CornerRadius(6),
                Margin = new Thickness(2),
                Cursor = Cursors.Hand,
                Background = Brushes.Transparent,
            };
            var text = new TextBlock
            {
                Text = emoji,
                FontSize = 13,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            border.Child = text;
            border.MouseDown += (s, e) =>
            {
                if (e.LeftButton == MouseButtonState.Pressed)
                    SelectEmoji(emoji, border);
            };
            border.MouseEnter += (_, _) =>
                border.Background = new SolidColorBrush(Color.FromArgb(0x3F, 0xFF, 0xFF, 0xFF));
            border.MouseLeave += (_, _) =>
                border.Background = Brushes.Transparent;
            emojiPanel.Children.Add(border);
        }
    }

    private void EmojiBtn_Click(object sender, RoutedEventArgs e)
    {
        emojiPopup.IsOpen = !emojiPopup.IsOpen;
    }

    private void SelectEmoji(string emoji, Border selectedBorder)
    {
        _note.Icon = emoji;
        emojiBtn.Content = emoji;
        _note.LastModified = DateTime.Now;
        _note.IsDirty = false;
        _store.Save();
        emojiPopup.IsOpen = false;

        // Flash feedback
        var down = AnimationHelper.MakeAnimation(0.7, 80);
        var up = AnimationHelper.MakeAnimation(1, 120);
        down.Completed += (_, _) => emojiBtn.BeginAnimation(OpacityProperty, up);
        emojiBtn.BeginAnimation(OpacityProperty, down);
    }

    private void UpdateButtonForegrounds()
    {
        var dark = IsDarkColor(_note.Color);
        var noteColor = ParseColor(_note.Color);
        var bgColor = Color.FromArgb(0xF2, noteColor.R, noteColor.G, noteColor.B);

        var fg = new SolidColorBrush(dark ? Colors.White : Color.FromArgb(0x88, 0x3A, 0x3A, 0x3A));
        SetPanelForeground(titleRightPanel, fg);
        SetPanelForeground(bottomRightPanel, fg);

        // Update floating toolbar too
        var floatFg = new SolidColorBrush(dark ? Colors.White : Color.FromArgb(0xCC, 0x3A, 0x3A, 0x3A));
        foreach (var child in formatToolbar.Children)
        {
            if (child is Control ctrl)
                ctrl.Foreground = floatFg;
        }
        if (formatPopup.Child is Border floatBorder)
            floatBorder.Background = new SolidColorBrush(bgColor);

        // Style heading picker buttons if visible
        foreach (var child in floatHeadingPicker.Children)
        {
            if (child is Control ctrl)
                ctrl.Foreground = floatFg;
        }

        // Style search bar dynamically
        var searchFg = new SolidColorBrush(dark ? Colors.White : Color.FromArgb(0xBB, 0x3A, 0x3A, 0x3A));
        noteSearchBox.Foreground = searchFg;
        noteSearchHint.Foreground = new SolidColorBrush(Color.FromArgb(0x66,
            dark ? (byte)0xFF : (byte)0x3A,
            dark ? (byte)0xFF : (byte)0x3A,
            dark ? (byte)0xFF : (byte)0x3A));
        var searchBg = dark ? Color.FromArgb(0xE0, 0x2D, 0x2D, 0x2D) : Color.FromArgb(0xE0, 0xF0, 0xF0, 0xF0);
        noteSearchBorder.Background = new SolidColorBrush(searchBg);
        noteSearchBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(0x40,
            dark ? (byte)0xFF : (byte)0x00,
            dark ? (byte)0xFF : (byte)0x00,
            dark ? (byte)0xFF : (byte)0x00));

        ApplyScrollbarReversal(noteText, _note.Color);
    }

    private static void SetPanelForeground(Panel panel, Brush brush)
    {
        foreach (var child in panel.Children)
        {
            if (child is Control ctrl)
                ctrl.Foreground = brush;
        }
    }

    public void UpdateSave()
    {
        _store.Save();
    }

    private void SavePosition()
    {
        _note.WinLeft = Left;
        _note.WinTop = Top;
        _note.WinWidth = Width;
        _note.WinHeight = Height;
    }

    private static void FlashElement(UIElement el)
    {
        var down = AnimationHelper.MakeAnimation(0.7, 80);
        var up = AnimationHelper.MakeAnimation(1, 120);
        down.Completed += (_, _) => el.BeginAnimation(OpacityProperty, up);
        el.BeginAnimation(OpacityProperty, down);
    }

    /// <summary>
    /// Resets a note window to default size and positions it to the left of the dock.
    /// </summary>
    public static void ResetToDefaultPosition(NoteWindow win, double dockLeft)
    {
        win.Left = dockLeft - DefaultWidth - 10;
        win.Width = DefaultWidth;
        win.Height = DefaultHeight;
    }

    // ── Auto-pairing ──

    private void NoteWindow_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (e.Text.Length != 1) return;
        var ch = e.Text[0];

        // Skip auto-pair if text is selected (wrap in markers instead)
        if (!noteText.Selection.IsEmpty)
        {
            switch (ch)
            {
                case '(': WrapSelection("(", ")"); e.Handled = true; break;
                case '[': WrapSelection("[", "]"); e.Handled = true; break;
                case '{': WrapSelection("{", "}"); e.Handled = true; break;
                case '"': WrapSelection("\"", "\""); e.Handled = true; break;
                case '\'': WrapSelection("'", "'"); e.Handled = true; break;
            }
            if (e.Handled) return;
        }

        switch (ch)
        {
            case '(': InsertPair("()"); e.Handled = true; break;
            case '[': InsertPair("[]"); e.Handled = true; break;
            case '{': InsertPair("{}"); e.Handled = true; break;
            case '"': InsertPair("\"\""); e.Handled = true; break;
            case '\'': InsertPair("''"); e.Handled = true; break;
            case ')': SkipClosing(')'); break;
            case ']': SkipClosing(']'); break;
            case '}': SkipClosing('}'); break;
        }
    }

    private void InsertPair(string pair)
    {
        var pos = noteText.CaretPosition;
        pos.InsertTextInRun(pair);
        noteText.CaretPosition = pos.GetPositionAtOffset(1);
    }

    private void WrapSelection(string open, string close)
    {
        var sel = noteText.Selection;
        var text = sel.Text;
        sel.Text = $"{open}{text}{close}";
        // Selection covers the wrapped text
    }

    private void SkipClosing(char ch)
    {
        var text = noteText.CaretPosition.GetTextInRun(LogicalDirection.Forward);
        if (text.Length > 0 && text[0] == ch)
            noteText.CaretPosition = noteText.CaretPosition.GetPositionAtOffset(1);
    }

    // ── List continuation ──

    private bool HandleListContinuation()
    {
        var para = noteText.CaretPosition.Paragraph;
        if (para == null) return false;

        var text = new TextRange(para.ContentStart, para.ContentEnd).Text.TrimEnd('\r', '\n');

        // Prefixes for bullet / checklist (including QuickNotes checkboxes)
        string[] prefixes = ["- ", "* ", "\u2022 ", "\u25a1 ", "\u2610 ", "[] ", "[x] "];
        // QuickNotes-specific checkbox prefixes
        string[] cbPrefixes = ["\u25fb ", "\u2713 "];  // ◻ and ✓

        // Helper: check if text is only the prefix (empty list item)
        bool IsEmptyListItem(string prefix)
            => text.TrimEnd() == prefix.TrimEnd();

        // Check QuickNotes checkboxes first
        foreach (var prefix in cbPrefixes)
        {
            if (!text.StartsWith(prefix)) continue;

            if (IsEmptyListItem(prefix))
            {
                // Empty checkbox → remove it and exit list
                var doc = noteText.Document;

                // Move caret to after this paragraph before removing
                var afterPara = para.NextBlock;
                var insertPos = afterPara != null
                    ? afterPara.ContentStart
                    : doc.ContentEnd;

                // Insert an empty paragraph where this one was
                var emptyPara = new Paragraph(new Run(""));
                if (afterPara != null)
                    doc.Blocks.InsertBefore(afterPara, emptyPara);
                else
                    doc.Blocks.Add(emptyPara);

                doc.Blocks.Remove(para);
                noteText.CaretPosition = emptyPara.ContentEnd;
                return true;
            }

            // Has content → continue with same prefix
            var doc2 = noteText.Document;
            var newPara2 = new Paragraph(new Run(prefix));

            var nextBlock2 = para.NextBlock;
            if (nextBlock2 != null)
                doc2.Blocks.InsertBefore(nextBlock2, newPara2);
            else
                doc2.Blocks.Add(newPara2);

            noteText.CaretPosition = newPara2.ContentEnd;
            return true;
        }

        // Standard bullet / prefix lists
        foreach (var prefix in prefixes)
        {
            if (!text.StartsWith(prefix)) continue;

            if (IsEmptyListItem(prefix))
            {
                // Only prefix → exit list (let default Enter create empty para)
                return false;
            }

            // Has content → continue with same prefix
            var doc = noteText.Document;
            var newPara = new Paragraph(new Run(prefix));

            var nextBlock = para.NextBlock;
            if (nextBlock != null)
                doc.Blocks.InsertBefore(nextBlock, newPara);
            else
                doc.Blocks.Add(newPara);

            noteText.CaretPosition = newPara.ContentEnd;
            return true;
        }

        // Numbered list continuation: 1. → 2.
        var match = System.Text.RegularExpressions.Regex.Match(text, @"^(\d+)\.\s");
        if (match.Success)
        {
            var num = int.Parse(match.Groups[1].Value);
            var doc = noteText.Document;
            var newPara = new Paragraph(new Run($"{num + 1}. "));

            var nextBlock = para.NextBlock;
            if (nextBlock != null)
                doc.Blocks.InsertBefore(nextBlock, newPara);
            else
                doc.Blocks.Add(newPara);

            noteText.CaretPosition = newPara.ContentEnd;
            return true;
        }

        return false;
    }

    // ── Markdown shortcuts (inline) ──

    private bool HandleMarkdownShortcuts()
    {
        var caret = noteText.CaretPosition;
        var text = caret.GetTextInRun(LogicalDirection.Backward);
        if (string.IsNullOrEmpty(text)) return false;

        // **text** → Bold
        if (text.EndsWith("**") && text.Length > 4)
        {
            var innerEnd = text.Length - 2;
            var openIdx = text.LastIndexOf("**", innerEnd - 1);
            if (openIdx >= 0 && openIdx + 2 < innerEnd)
                return ApplyInlineFormat(caret, text, openIdx, "**", "**",
                    FontWeightProperty, FontWeights.Bold);
        }

        // *text* → Italic (not **)
        if (text.EndsWith("*") && !text.EndsWith("**") && text.Length > 2)
        {
            var innerEnd = text.Length - 1;
            var openIdx = text.LastIndexOf("*", innerEnd - 1);
            if (openIdx >= 0 && openIdx + 1 < innerEnd &&
                (openIdx == 0 || text[openIdx - 1] != '*'))
                return ApplyInlineFormat(caret, text, openIdx, "*", "*",
                    FontStyleProperty, FontStyles.Italic);
        }

        // ~~text~~ → Strikethrough
        if (text.EndsWith("~~") && text.Length > 4)
        {
            var innerEnd = text.Length - 2;
            var openIdx = text.LastIndexOf("~~", innerEnd - 1);
            if (openIdx >= 0 && openIdx + 2 < innerEnd)
                return ApplyInlineFormat(caret, text, openIdx, "~~", "~~",
                    Inline.TextDecorationsProperty, TextDecorations.Strikethrough);
        }

        return false;
    }

    private bool ApplyInlineFormat(TextPointer caret, string text, int openIdx,
        string openMarker, string closeMarker, DependencyProperty prop, object value)
    {
        var start = caret.GetPositionAtOffset(-text.Length + openIdx);
        if (start == null) return false;

        var sel = new TextRange(start, caret);
        var totalLen = sel.Text.Length;
        var innerLen = totalLen - openMarker.Length - closeMarker.Length;
        var inner = sel.Text.Substring(openMarker.Length, innerLen);

        // Replace markers+inner with just inner
        sel.Text = inner;

        // Apply format (sel now covers the inner text)
        sel.ApplyPropertyValue(prop, value);

        // Insert space after the formatted text
        var newCaret = noteText.CaretPosition;
        newCaret.InsertTextInRun(" ");
        return true;
    }

    // ── Highlight ──

    private void ApplyHighlight(Color color)
    {
        var sel = noteText.Selection;
        if (sel.IsEmpty) return;

        var range = new TextRange(sel.Start, sel.End);

        // "Sin color" — remove all highlighting
        if (color == _noHighlightColor)
        {
            range.ApplyPropertyValue(TextElement.BackgroundProperty, Brushes.Transparent);
            MarkDirtyAndDebounce();
            return;
        }

        var existing = range.GetPropertyValue(TextElement.BackgroundProperty);

        if (existing is SolidColorBrush scb && scb.Color == color)
        {
            range.ApplyPropertyValue(TextElement.BackgroundProperty, Brushes.Transparent);
        }
        else
        {
            range.ApplyPropertyValue(TextElement.BackgroundProperty,
                new SolidColorBrush(color));
        }

        MarkDirtyAndDebounce();
    }

    private void ToggleHighlight()
    {
        ApplyHighlight(_currentHighlightColor);
    }

    private void NoteWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Intercept Ctrl+V for images BEFORE RichTextBox handles it
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.V)
        {
            // Use Win32 P/Invoke to read clipboard
            var img = Helpers.ClipboardImageReader.GetImageFromClipboard();
            if (img != null)
            {
                e.Handled = true;
                InsertImageFromClipboard(img);
                return;
            }

            // Check for file drop (image files from Explorer)
            if (Clipboard.ContainsFileDropList())
            {
                var files = Clipboard.GetFileDropList();
                if (files.Count > 0 && files[0] != null)
                {
                    var ext = System.IO.Path.GetExtension(files[0]).ToLowerInvariant();
                    var validExts = new System.Collections.Generic.HashSet<string>
                        { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp" };
                    if (validExts.Contains(ext))
                    {
                        e.Handled = true;
                        InsertImageFromFile(files[0]);
                        return;
                    }
                }
            }

            // No image — let default paste handle text
            // Don't set e.Handled, let it flow naturally
            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            switch (e.Key)
            {
                case Key.F: ShowNoteSearch(); e.Handled = true; return;
                case Key.B: ToggleBold(); e.Handled = true; return;
                case Key.I: ToggleItalic(); e.Handled = true; return;
                case Key.U: ToggleUnderline(); e.Handled = true; return;
                case Key.K: ToggleStrikethrough(); e.Handled = true; return;
                case Key.Z: Undo(); e.Handled = true; return;
                case Key.Y: Redo(); e.Handled = true; return;
                case Key.L: ToggleHighlight(); e.Handled = true; return;
                case Key.H: ToggleHighlight(); e.Handled = true; return;
                case Key.D1: SetHeading(1); e.Handled = true; return;
                case Key.D2: SetHeading(2); e.Handled = true; return;
                case Key.D3: SetHeading(3); e.Handled = true; return;
                case Key.D0: SetHeading(0); e.Handled = true; return;
            }
        }
        if (Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key == Key.C)
        {
            InsertCheckbox();
            e.Handled = true;
            return;
        }

        // Tab / Shift+Tab for indentation (only when RichTextBox has focus)
        if (Keyboard.Modifiers == ModifierKeys.None && e.Key == Key.Tab && noteText.IsFocused)
        {
            EditingCommands.IncreaseIndentation.Execute(null, noteText);
            e.Handled = true;
            return;
        }
        if (Keyboard.Modifiers == ModifierKeys.Shift && e.Key == Key.Tab && noteText.IsFocused)
        {
            EditingCommands.DecreaseIndentation.Execute(null, noteText);
            e.Handled = true;
            return;
        }

        // Markdown shortcuts (Space) & list continuation (Enter)
        if (Keyboard.Modifiers == ModifierKeys.None)
        {
            if (e.Key == Key.Space)
            {
                if (HandleMarkdownShortcuts())
                {
                    e.Handled = true;
                    return;
                }
            }
            else if (e.Key == Key.Enter)
            {
                if (HandleListContinuation())
                {
                    e.Handled = true;
                    return;
                }
                // After RTF processes Enter, reset new paragraph after heading
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    var curPara = noteText.CaretPosition.Paragraph;
                    var prevPara = curPara?.PreviousBlock as Paragraph;
                    if (prevPara != null &&
                        (prevPara is { FontSize: >= 15 }))
                    {
                        curPara.FontSize = 13;
                        curPara.FontWeight = FontWeights.Normal;
                    }
                }));
            }

            // Always format URLs in current paragraph on Space or Enter
            // The Space/Enter has already been processed by the RichTextBox at this point.
            // We dispatch this async so the document is stable before we modify it.
            if (e.Key == Key.Space || e.Key == Key.Enter)
            {
                Dispatcher.BeginInvoke(new Action(FormatUrlsInDocument));
            }
        }

        // Search key handling is done by noteSearchBox.PreviewKeyDown
        if (e.Key == Key.Escape && noteSearchBorder.Visibility == Visibility.Visible)
        {
            HideNoteSearch();
            e.Handled = true;
            return;
        }
    }

    private void MarkDirtyAndDebounce()
    {
        _note.IsDirty = true;
        _note.LastModified = DateTime.Now;
        var mainWin = Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w is MainWindow) as MainWindow;
        mainWin?.DebounceSave();
    }

    private void ToggleBold()
    {
        var sel = noteText.Selection;
        if (sel.IsEmpty) return;
        var isBold = sel.GetPropertyValue(FontWeightProperty) is FontWeight fw && fw == FontWeights.Bold;
        sel.ApplyPropertyValue(FontWeightProperty, isBold ? FontWeights.Normal : FontWeights.Bold);
        MarkDirtyAndDebounce();
    }

    private void ToggleItalic()
    {
        var sel = noteText.Selection;
        if (sel.IsEmpty) return;
        var isItalic = sel.GetPropertyValue(FontStyleProperty) is FontStyle fs && fs == FontStyles.Italic;
        sel.ApplyPropertyValue(FontStyleProperty, isItalic ? FontStyles.Normal : FontStyles.Italic);
        MarkDirtyAndDebounce();
    }

    private void ToggleUnderline()
    {
        var sel = noteText.Selection;
        if (sel.IsEmpty) return;
        var hasUL = false;
        if (!sel.IsEmpty)
        {
            var range = new TextRange(sel.Start, sel.End);
            if (range.GetPropertyValue(Inline.TextDecorationsProperty) is TextDecorationCollection tdc)
                hasUL = tdc == TextDecorations.Underline;
        }
        sel.ApplyPropertyValue(Inline.TextDecorationsProperty, hasUL ? null : TextDecorations.Underline);
        MarkDirtyAndDebounce();
    }

    private void ToggleStrikethrough()
    {
        var sel = noteText.Selection;
        if (sel.IsEmpty) return;
        var hasST = false;
        if (!sel.IsEmpty)
        {
            var range = new TextRange(sel.Start, sel.End);
            if (range.GetPropertyValue(Inline.TextDecorationsProperty) is TextDecorationCollection tdc)
                hasST = tdc == TextDecorations.Strikethrough;
        }
        sel.ApplyPropertyValue(Inline.TextDecorationsProperty, hasST ? null : TextDecorations.Strikethrough);
        MarkDirtyAndDebounce();
    }

    private void ToggleBulletList()
    {
        var sel = noteText.Selection;
        if (sel.IsEmpty) return;
        ToggleList(sel, TextMarkerStyle.Disc);
    }

    private void ToggleNumberList()
    {
        var sel = noteText.Selection;
        if (sel.IsEmpty) return;
        ToggleList(sel, TextMarkerStyle.Decimal);
    }

    private void ToggleList(TextSelection sel, TextMarkerStyle style)
    {
        var startPara = sel.Start.Paragraph;
        var endPara = sel.End.Paragraph;
        if (startPara == null || endPara == null) return;

        var doc = noteText.Document;
        var existingList = startPara.Parent as List;
        bool alreadyList = existingList != null && existingList.MarkerStyle == style;

        if (alreadyList)
        {
            var items = existingList!.ListItems.ToArray();
            var refBlock = existingList.NextBlock;

            doc.Blocks.Remove(existingList);

            for (int i = items.Length - 1; i >= 0; i--)
            {
                var paraBlocks = items[i].Blocks.ToArray();
                foreach (Block b in paraBlocks)
                {
                    if (b is Paragraph p)
                    {
                        p.Margin = new Thickness(0);
                        if (refBlock != null)
                            doc.Blocks.InsertBefore(refBlock, p);
                        else
                            doc.Blocks.Add(p);
                    }
                }
            }
        }
        else
        {
            var paras = new List<Paragraph>();
            var block = (Block)startPara;
            bool reached = false;
            while (block != null)
            {
                if (block == startPara) reached = true;
                if (reached && block is Paragraph p)
                    paras.Add(p);
                if (block == endPara) break;
                block = block.NextBlock;
            }

            var refBlock = endPara.NextBlock;

            foreach (var p in paras)
                doc.Blocks.Remove(p);

            var newList = new List { MarkerStyle = style };
            foreach (var p in paras)
                newList.ListItems.Add(new ListItem { Blocks = { p } });

            if (refBlock != null)
                doc.Blocks.InsertBefore(refBlock, newList);
            else
                doc.Blocks.Add(newList);
        }
        MarkDirtyAndDebounce();
    }

    // ── Floating format toolbar ──

    private void NoteText_SelectionChanged(object sender, RoutedEventArgs e)
    {
        var sel = noteText.Selection;
        if (sel != null && !sel.IsEmpty && sel.Text.TrimEnd().Length > 0)
        {
            if (floatHighlightPicker.Visibility == Visibility.Visible) return; // don't restart timer while picker is open
            _selectionTimer.Stop();
            _selectionTimer.Start();
        }
        else
        {
            _selectionTimer.Stop();
            if (floatHighlightPicker.Visibility != Visibility.Visible)
                HideFormatPopup();
        }
    }

    private void SelectionTimer_Tick(object? sender, EventArgs e)
    {
        _selectionTimer.Stop();
        if (noteText.Selection is { IsEmpty: false } sel && sel.Text.TrimEnd().Length > 0)
        {
            // Don't reposition if highlight picker is open
            if (floatHighlightPicker.Visibility != Visibility.Visible)
                ShowFormatPopup();
        }
    }

    private void ShowFormatPopup()
    {
        if (formatPopup.IsOpen)
        {
            // Just update styles, don't flicker
            UpdateFloatingToolbarStyle();
        }
        else
        {
            formatPopup.IsOpen = false;
            UpdateFloatingToolbarStyle();
            PositionFormatPopup();
            formatPopup.IsOpen = true;
        }
    }

    private void HideFormatPopup()
    {
        // Reset inline picker state so it works when popup reopens
        floatHighlightPicker.BeginAnimation(OpacityProperty, null);
        floatHighlightPicker.Visibility = Visibility.Collapsed;
        floatHighlightPicker.Opacity = 0;
        floatHeadingPicker.BeginAnimation(OpacityProperty, null);
        floatHeadingPicker.Visibility = Visibility.Collapsed;
        floatHeadingPicker.Opacity = 0;
        formatPopup.IsOpen = false;
    }

    private void PositionFormatPopup()
    {
        var sel = noteText.Selection;
        if (sel == null || sel.IsEmpty) return;

        try
        {
            var startPt = sel.Start.GetCharacterRect(LogicalDirection.Forward);
            var endPt = sel.End.GetCharacterRect(LogicalDirection.Backward);

            // Get selection dimensions relative to noteText
            double selLeft = Math.Min(startPt.X, endPt.X);
            double selRight = Math.Max(startPt.X + startPt.Width, endPt.X + endPt.Width);
            double selTop = Math.Min(startPt.Y, endPt.Y);
            double selBot = Math.Max(startPt.Y + startPt.Height, endPt.Y + endPt.Height);
            double selCenterX = (selLeft + selRight) / 2;

            // Force layout to get actual toolbar size
            formatToolbar.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var tw = formatToolbar.DesiredSize.Width;
            var th = formatToolbar.DesiredSize.Height;

            // Close first to reset placement, then reopen with new offsets
            formatPopup.IsOpen = false;
            formatPopup.Placement = PlacementMode.Relative;
            formatPopup.PlacementTarget = noteText;

            // Center above selection
            double offsetX = Math.Max(2, selCenterX - tw / 2);
            double offsetY = selTop - th - 8;

            // If off-screen top (relative to noteText), show below instead
            if (offsetY < 0)
                offsetY = selBot + 6;

            // Clamp horizontal within noteText bounds
            offsetX = Math.Max(2, Math.Min(offsetX, noteText.ActualWidth - tw - 2));

            formatPopup.HorizontalOffset = offsetX;
            formatPopup.VerticalOffset = offsetY;
        }
        catch
        {
            // Fallback
            formatPopup.IsOpen = false;
            formatPopup.Placement = PlacementMode.Relative;
            formatPopup.PlacementTarget = noteText;
            formatPopup.HorizontalOffset = noteText.ActualWidth / 2 - 100;
            formatPopup.VerticalOffset = 10;
        }
    }

    private void UpdateFloatingToolbarStyle()
    {
        var dark = IsDarkColor(_note.Color);
        var noteColor = ParseColor(_note.Color);
        var bgColor = Color.FromArgb(0xF2, noteColor.R, noteColor.G, noteColor.B);

        // Set popup background to note color
        if (formatPopup.Child is Border popupBorder)
            popupBorder.Background = new SolidColorBrush(bgColor);

        // Set button foregrounds
        var fg = new SolidColorBrush(dark ? Colors.White : Color.FromArgb(0xCC, 0x3A, 0x3A, 0x3A));
        foreach (var child in formatToolbar.Children)
        {
            if (child is Control ctrl)
                ctrl.Foreground = fg;
        }

        // Sync heading picker buttons
        foreach (var child in floatHeadingPicker.Children)
        {
            if (child is Control ctrl)
                ctrl.Foreground = fg;
        }

        // Sync highlight button with current color
        floatHighlightBtn.Background = new SolidColorBrush(_currentHighlightColor);
    }

    // ── Floating toolbar button handlers ──

    private void FloatBold_Click(object sender, RoutedEventArgs e)
    {
        ToggleBold();
        try { noteText.Focus(); } catch { }
    }
    private void FloatItalic_Click(object sender, RoutedEventArgs e)
    {
        ToggleItalic();
        try { noteText.Focus(); } catch { }
    }
    private void FloatUnderline_Click(object sender, RoutedEventArgs e)
    {
        ToggleUnderline();
        try { noteText.Focus(); } catch { }
    }
    private void FloatStrike_Click(object sender, RoutedEventArgs e)
    {
        ToggleStrikethrough();
        try { noteText.Focus(); } catch { }
    }
    private void FloatCheckbox_Click(object sender, RoutedEventArgs e)
    {
        InsertCheckbox();
        try { noteText.Focus(); } catch { }
    }

    private void FloatHighlight_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            if (floatHighlightPicker.Visibility == Visibility.Visible)
            {
                CloseHighlightPicker();
            }
            else
            {
                CloseHeadingPicker(); // close heading if open
                // Reset animation state in case previous fade was interrupted
                floatHighlightPicker.BeginAnimation(OpacityProperty, null);
                floatHighlightPicker.Opacity = 0;
                OpenHighlightPicker();
            }
        }
    }

    private void OpenHighlightPicker()
    {
        if (floatHighlightPicker.Visibility == Visibility.Visible) return;

        floatHighlightPicker.Children.Clear();

        // Add "no color" button — ✕ removes highlight
        var noColor = new Border
        {
            Width = 14, Height = 14,
            CornerRadius = new CornerRadius(2),
            Margin = new Thickness(2, 0, 2, 0),
            Cursor = Cursors.Hand,
            Background = Brushes.Transparent,
            Child = new TextBlock
            {
                Text = "✕",
                FontSize = 10,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        noColor.MouseDown += (_, args) =>
        {
            if (args.LeftButton == MouseButtonState.Pressed)
            {
                _currentHighlightColor = _noHighlightColor;
                ApplyHighlight(_noHighlightColor);
                floatHighlightBtn.Background = Brushes.Transparent;
                CloseHighlightPicker();
            }
        };
        floatHighlightPicker.Children.Add(noColor);

        // Add color dots
        foreach (var color in HighlightColors)
        {
            var dot = new Border
            {
                Width = 14, Height = 14,
                CornerRadius = new CornerRadius(2),
                Margin = new Thickness(1, 0, 1, 0),
                Cursor = Cursors.Hand,
                Background = new SolidColorBrush(color),
            };
            var c = color;
            dot.MouseDown += (_, args) =>
            {
                if (args.LeftButton == MouseButtonState.Pressed)
                {
                    _currentHighlightColor = c;
                    ApplyHighlight(c);
                    floatHighlightBtn.Background = new SolidColorBrush(c);
                    CloseHighlightPicker();
                }
            };
            floatHighlightPicker.Children.Add(dot);
        }

        // Style the picker to match toolbar
        var dark = IsDarkColor(_note.Color);
        var floatFg = new SolidColorBrush(dark ? Colors.White : Color.FromArgb(0xCC, 0x3A, 0x3A, 0x3A));

        // Show with fade animation
        floatHighlightPicker.Visibility = Visibility.Visible;
        var fadeIn = AnimationHelper.MakeAnimation(0, 1, 150);
        floatHighlightPicker.BeginAnimation(OpacityProperty, fadeIn);
    }

    private void CloseHighlightPicker()
    {
        if (floatHighlightPicker.Visibility != Visibility.Visible) return;

        var fadeOut = AnimationHelper.MakeAnimation(1, 0, 100);
        fadeOut.Completed += OnCloseFadeCompleted;
        floatHighlightPicker.BeginAnimation(OpacityProperty, fadeOut);
    }

    private void OnCloseFadeCompleted(object? sender, EventArgs e)
    {
        floatHighlightPicker.Visibility = Visibility.Collapsed;
        floatHighlightPicker.BeginAnimation(OpacityProperty, null);
        // Ensure focus returns to editor
        try { noteText.Focus(); } catch { }
    }

    // ── Headings ──

    private void SetHeading(int level)
    {
        // Use caret position paragraph if nothing selected, else selection start
        TextPointer pos;
        if (noteText.Selection.IsEmpty)
            pos = noteText.CaretPosition;
        else
            pos = noteText.Selection.Start;

        var para = pos.Paragraph;
        if (para == null) return;

        switch (level)
        {
            case 1:
                para.FontSize = 22;
                para.FontWeight = FontWeights.Bold;
                break;
            case 2:
                para.FontSize = 18;
                para.FontWeight = FontWeights.Bold;
                break;
            case 3:
                para.FontSize = 15;
                para.FontWeight = FontWeights.SemiBold;
                break;
            default:
                para.FontSize = 13;
                para.FontWeight = FontWeights.Normal;
                break;
        }

        MarkDirtyAndDebounce();
    }

    private void FloatHeading_Click(object sender, RoutedEventArgs e)
    {
        if (floatHeadingPicker.Visibility == Visibility.Visible)
        {
            CloseHeadingPicker();
        }
        else
        {
            CloseHighlightPicker(); // close highlight if open
            OpenHeadingPicker();
        }
    }

    private void OpenHeadingPicker()
    {
        if (floatHeadingPicker.Visibility == Visibility.Visible) return;

        floatHeadingPicker.Children.Clear();

        var dark = IsDarkColor(_note.Color);
        var fg = new SolidColorBrush(dark ? Colors.White : Color.FromArgb(0xCC, 0x3A, 0x3A, 0x3A));

        var items = new[] { ("✕", 0, "Normal"), ("H1", 1, "Encabezado 1"),
                            ("H2", 2, "Encabezado 2"), ("H3", 3, "Encabezado 3") };

        foreach (var (label, level, tooltip) in items)
        {
            var btn = new Button
            {
                Content = label,
                FontSize = level switch { 1 => 15, 2 => 13, 3 => 11, _ => 10 },
                FontWeight = level > 0 ? FontWeights.Bold : FontWeights.Normal,
                Width = level > 0 ? 32 : 36,
                Height = 22,
                Cursor = Cursors.Hand,
                ToolTip = tooltip,
                Foreground = fg,
                Style = (Style)FindResource("TitleBtn"),
            };
            int lvl = level;
            btn.Click += (_, _) =>
            {
                SetHeading(lvl);
                CloseHeadingPicker();
            };
            floatHeadingPicker.Children.Add(btn);

            if (level == 0)
            {
                floatHeadingPicker.Children.Add(new Rectangle
                {
                    Width = 1,
                    Fill = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)),
                    Margin = new Thickness(3, 2, 3, 2),
                });
            }
        }

        // Toggle heading state for color sync
        floatHeadingPicker.Visibility = Visibility.Visible;
        var fadeIn = AnimationHelper.MakeAnimation(0, 1, 150);
        floatHeadingPicker.BeginAnimation(OpacityProperty, fadeIn);
    }

    private void CloseHeadingPicker()
    {
        if (floatHeadingPicker.Visibility != Visibility.Visible) return;
        var fadeOut = AnimationHelper.MakeAnimation(1, 0, 100);
        fadeOut.Completed += (_, _) =>
        {
            floatHeadingPicker.Visibility = Visibility.Collapsed;
            floatHeadingPicker.BeginAnimation(OpacityProperty, null);
        };
        floatHeadingPicker.BeginAnimation(OpacityProperty, fadeOut);
    }

    // ── Right-click context menu ──

    private void NoteText_ContextMenuOpening(object? sender, ContextMenuEventArgs e)
    {
        var menu = new ContextMenu();

        // Standard edit items
        menu.Items.Add(new MenuItem
        {
            Header = "Cortar",
            Command = ApplicationCommands.Cut,
            CommandTarget = noteText,
        });
        menu.Items.Add(new MenuItem
        {
            Header = "Copiar",
            Command = ApplicationCommands.Copy,
            CommandTarget = noteText,
        });
        menu.Items.Add(new MenuItem
        {
            Header = "Pegar",
            Command = ApplicationCommands.Paste,
            CommandTarget = noteText,
        });
        menu.Items.Add(new Separator());

        // Move to notebook submenu
        var notebookMenu = new MenuItem { Header = "Mover a libreta" };
        foreach (var nb in _store.Notebooks)
        {
            var item = new MenuItem
            {
                Header = $"{nb.Icon} {nb.Name}",
                Tag = nb.Id,
            };
            item.Click += ContextMenu_MoveToNotebook;
            notebookMenu.Items.Add(item);
        }
        notebookMenu.Items.Add(new Separator());
        var noNotebook = new MenuItem { Header = "Sin libreta" };
        noNotebook.Click += ContextMenu_ClearNotebook;
        notebookMenu.Items.Add(noNotebook);
        menu.Items.Add(notebookMenu);

        // Tag submenu
        var tagMenu = new MenuItem { Header = "Asignar tag" };
        foreach (var t in _store.Tags)
        {
            var item = new MenuItem
            {
                Header = $"#{t.Name}",
                IsChecked = _note.TagIds?.Contains(t.Id) == true,
                Tag = t.Id,
            };
            item.Click += ContextMenu_ToggleTag;
            tagMenu.Items.Add(item);
        }
        menu.Items.Add(tagMenu);
        menu.Items.Add(new Separator());

        // Note actions
        var duplicateItem = new MenuItem { Header = "Duplicar nota" };
        duplicateItem.Click += ContextMenu_DuplicateNote;
        menu.Items.Add(duplicateItem);

        var archiveItem = new MenuItem { Header = "Archivar nota" };
        archiveItem.Click += ContextMenu_ArchiveNote;
        menu.Items.Add(archiveItem);

        var deleteItem = new MenuItem { Header = "Eliminar nota" };
        deleteItem.Click += ContextMenu_DeleteNote;
        menu.Items.Add(deleteItem);

        noteText.ContextMenu = menu;
    }

    private void ContextMenu_MoveToNotebook(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem item && item.Tag is Guid nbId)
        {
            _note.NotebookId = nbId;
            _note.LastModified = DateTime.Now;
            _store.Save();
        }
    }

    private void ContextMenu_ClearNotebook(object sender, RoutedEventArgs e)
    {
        _note.NotebookId = null;
        _note.LastModified = DateTime.Now;
        _store.Save();
    }

    private void ContextMenu_ToggleTag(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem item && item.Tag is Guid tagId)
        {
            if (_note.TagIds.Contains(tagId))
                _note.TagIds.Remove(tagId);
            else
                _note.TagIds.Add(tagId);
            _note.LastModified = DateTime.Now;
            _store.Save();
        }
    }

    private void ContextMenu_DuplicateNote(object sender, RoutedEventArgs e)
    {
        // Save current note first
        SaveRichText();

        var copy = new Note
        {
            Title = _note.Title + " (copia)",
            Text = _note.Text,
            Color = _note.Color,
            Icon = _note.Icon,
            LastModified = DateTime.Now,
        };
        _store.Notes.Add(copy);
        _store.Save();

        // Open the duplicated note
        var win = new NoteWindow(copy, _store);
        win.Left = Left + 25;
        win.Top = Top + 25;
        win.Show();
    }

    private void ContextMenu_ArchiveNote(object sender, RoutedEventArgs e)
    {
        _note.IsArchived = true;
        _note.LastModified = DateTime.Now;
        _store.Save();
        Close();
    }

    private void ContextMenu_DeleteNote(object sender, RoutedEventArgs e)
    {
        _note.IsDeleted = true;
        _note.DeletedAt = DateTime.Now;
        _note.LastModified = DateTime.Now;
        _store.Save();
        Close();
    }

    // ── Image support ──

    private void InsertImageFromFile(string sourcePath)
    {
        try
        {
            Directory.CreateDirectory(_imageFolder);
            var ext = System.IO.Path.GetExtension(sourcePath).ToLowerInvariant();
            var fileName = $"{Guid.NewGuid()}{ext}";
            var destPath = System.IO.Path.Combine(_imageFolder, fileName);
            File.Copy(sourcePath, destPath, overwrite: false);
            InsertImageBlock(destPath);
        }
        catch (Exception ex) { ErrorLog.Write(ex, "InsertImageFromFile"); }
    }

    private void InsertImageFromClipboard(BitmapSource img)
    {
        try
        {
            if (img == null) return;

            Directory.CreateDirectory(_imageFolder);
            var fileName = $"{Guid.NewGuid()}.png";
            var filePath = System.IO.Path.Combine(_imageFolder, fileName);

            {
                using var fs = new FileStream(filePath, FileMode.Create);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(img));
                encoder.Save(fs);
            }

            InsertImageBlock(filePath);
        }
        catch (Exception ex) { ErrorLog.Write(ex, "InsertImageFromClipboard"); }
    }

    private void InsertImageBlock(string fullPath)
    {
        var img = new System.Windows.Controls.Image();
        BitmapImage bi;
        try
        {
            bi = new BitmapImage();
            bi.BeginInit();
            bi.UriSource = new Uri(fullPath);
            bi.CacheOption = BitmapCacheOption.OnLoad;
            bi.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            bi.EndInit();
            img.Source = bi;
        }
        catch { return; }

        // Natural size
        double naturalW = bi.PixelWidth;
        double noteContentW = Math.Max(Width - 60, 200);

        // Fit to note width, but never exceed natural size (no pixelation)
        img.MaxWidth = Math.Min(naturalW, noteContentW);
        img.HorizontalAlignment = HorizontalAlignment.Center;
        img.Margin = new Thickness(0, 4, 0, 4);
        img.Stretch = Stretch.Uniform;

        var container = new BlockUIContainer(img);

        // Insert at caret position
        var caretPos = noteText.CaretPosition;
        var caretPara = caretPos.Paragraph;

        if (caretPara != null && caretPara.Parent is FlowDocument doc)
            doc.Blocks.InsertAfter(caretPara, container);
        else if (caretPara != null && caretPara.Parent is Section sec)
            sec.Blocks.InsertAfter(caretPara, container);
        else
            noteText.Document.Blocks.Add(container);

        noteText.CaretPosition = container.ElementEnd;
        noteText.Focus();
        MarkDirtyAndDebounce();
    }

    private void InsertCheckbox()
    {
        try
        {
            var sel = noteText.Selection;
            var para = sel.Start.Paragraph;
            if (para == null) return;

            var paraText = new TextRange(para.ContentStart, para.ContentEnd).Text;

            int idx;
            TextPointer? cbPos = null;
            if ((idx = paraText.IndexOf("◻ ", StringComparison.Ordinal)) >= 0)
                cbPos = para.ContentStart.GetPositionAtOffset(idx);
            else if ((idx = paraText.IndexOf("✓ ", StringComparison.Ordinal)) >= 0)
                cbPos = para.ContentStart.GetPositionAtOffset(idx);

            if (cbPos != null)
                ToggleCheckboxAt(cbPos, paraText[idx] == '◻' ? "✓ " : "◻ ");
            else if (sel.IsEmpty)
                para.ContentStart.InsertTextInRun("◻ ");
            else
                sel.Text = "◻ ";

            noteText.Focus();
            MarkDirtyAndDebounce();
        }
        catch (Exception ex)
        {
            ErrorLog.Write(ex, "InsertCheckbox");
        }
    }

    private static void ToggleCheckboxAt(TextPointer charPos, string replaceWith)
    {
        var run = charPos.Parent as Run;
        if (run == null) return;
        int runOffset = charPos.GetOffsetToPosition(run.ContentStart);
        if (runOffset < 0) return;

        var start = run.ContentStart.GetPositionAtOffset(runOffset);
        var end = run.ContentStart.GetPositionAtOffset(runOffset + Math.Max(replaceWith.Length, 2));
        if (start == null || end == null) return;

        var r = new TextRange(start, end);
        r.Text = replaceWith;
    }

    private static readonly Regex _urlRegex = new(@"https?://[\w./?=&%#@!~$'()*+,;:–—\[\]_-]+", RegexOptions.Compiled);
    private bool _isUpdatingUrlFormats;

    private void NoteText_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        try
        {
            var pt = e.GetPosition(noteText);
            var pos = noteText.GetPositionFromPoint(pt, false);
            if (pos == null) return;

            // Ctrl+Click → open URL under cursor
            if (Keyboard.Modifiers == ModifierKeys.Control)
            {
                var textBefore = pos.GetTextInRun(LogicalDirection.Backward) ?? "";
                var textAfter = pos.GetTextInRun(LogicalDirection.Forward) ?? "";
                var combined = textBefore + textAfter;
                if (combined.Length > 0)
                {
                    var match = _urlRegex.Match(combined);
                    if (match.Success)
                    {
                        var url = match.Value;
                        // Find the URL at cursor: ensure cursor is within the match
                        if (textBefore.Length >= match.Index &&
                            textBefore.Length <= match.Index + match.Length)
                        {
                            e.Handled = true;
                            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                            {
                                FileName = url,
                                UseShellExecute = true
                            });
                            return;
                        }
                    }
                }
                return;
            }

            for (int offset = -1; offset <= 1; offset++)
            {
                var checkPos = pos.GetPositionAtOffset(offset);
                if (checkPos?.Parent is not Run run) continue;

                int runOffset = checkPos.GetOffsetToPosition(run.ContentStart);
                if (runOffset < 0 || runOffset >= run.Text.Length) continue;

                char c = run.Text[runOffset];
                if (c == '◻')
                {
                    ToggleCheckboxAt(checkPos, "✓ ");
                    e.Handled = true;
                    MarkDirtyAndDebounce();
                    return;
                }
                if (c == '✓')
                {
                    ToggleCheckboxAt(checkPos, "◻ ");
                    e.Handled = true;
                    MarkDirtyAndDebounce();
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            ErrorLog.Write(ex, "NoteText_PreviewMouseDown");
        }
    }

    private static string? ExtractUrlAt(string text, int offset)
    {
        var matches = _urlRegex.Matches(text);
        foreach (System.Text.RegularExpressions.Match m in matches)
        {
            if (offset >= m.Index && offset < m.Index + m.Length)
                return m.Value;
        }
        return null;
    }

    private void NoteText_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        try
        {
            var pt = e.GetPosition(noteText);
            var pos = noteText.GetPositionFromPoint(pt, false);
            if (pos == null) { noteText.Cursor = Cursors.IBeam; return; }

            for (int offset = -1; offset <= 1; offset++)
            {
                var checkPos = pos.GetPositionAtOffset(offset);
                if (checkPos?.Parent is not Run run) continue;
                int runOffset = checkPos.GetOffsetToPosition(run.ContentStart);
                if (runOffset < 0 || runOffset >= run.Text.Length) continue;
                char c = run.Text[runOffset];
                if (c == '◻' || c == '✓')
                {
                    noteText.Cursor = Cursors.Arrow;
                    return;
                }
            }
            noteText.Cursor = Cursors.IBeam;
        }
        catch
        {
            noteText.Cursor = Cursors.IBeam;
        }
    }

    private void ToolbarClick(System.Action action)
    {
        action();
        try { noteText.Focus(); } catch { }
    }

    private void BoldBtn_Click(object sender, RoutedEventArgs e) => ToolbarClick(ToggleBold);
    private void ItalicBtn_Click(object sender, RoutedEventArgs e) => ToolbarClick(ToggleItalic);
    private void UnderlineBtn_Click(object sender, RoutedEventArgs e) => ToolbarClick(ToggleUnderline);
    private void StrikeBtn_Click(object sender, RoutedEventArgs e) => ToolbarClick(ToggleStrikethrough);
    private void BulletBtn_Click(object sender, RoutedEventArgs e) => ToolbarClick(ToggleBulletList);
    private void NumberBtn_Click(object sender, RoutedEventArgs e) => ToolbarClick(ToggleNumberList);
    private void CheckboxBtn_Click(object sender, RoutedEventArgs e) => ToolbarClick(InsertCheckbox);

    private void UndoBtn_Click(object sender, RoutedEventArgs e) => ToolbarClick(Undo);
    private void RedoBtn_Click(object sender, RoutedEventArgs e) => ToolbarClick(Redo);

    private void Undo()
    {
        if (noteText.CanUndo)
            noteText.Undo();
    }

    private void Redo()
    {
        if (noteText.CanRedo)
            noteText.Redo();
    }

    private void ShowNoteSearch()
    {
        _searchQuery = "";
        noteSearchBox.Clear();
        searchCounter.Text = "";

        // Replace title with search bar
        titleDisplay.Visibility = Visibility.Collapsed;
        titleInput.Visibility = Visibility.Collapsed;
        titleRightPanel.Visibility = Visibility.Collapsed;
        noteSearchBorder.Visibility = Visibility.Visible;

        noteSearchBox.Focus();
    }

    private void HideNoteSearch()
    {
        noteSearchBorder.Visibility = Visibility.Collapsed;
        noteSearchBox.Clear();
        searchCounter.Text = "";

        // Restore title
        titleDisplay.Visibility = Visibility.Visible;
        titleRightPanel.Visibility = Visibility.Visible;

        _searchQuery = "";
        try { noteText.Focus(); } catch { }
    }

    private void NoteSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        var query = noteSearchBox.Text;
        noteSearchHint.Visibility = string.IsNullOrEmpty(query)
            ? Visibility.Visible : Visibility.Collapsed;

        if (string.IsNullOrEmpty(query))
        {
            _searchQuery = "";
            searchCounter.Text = "";
            return;
        }

        _searchQuery = query;
        var found = FindAndSelect(query, noteText.Document.ContentStart);
        UpdateSearchCounter();
    }

    private void NoteSearch_LostFocus(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(noteSearchBox.Text) && noteSearchBorder.Visibility == Visibility.Visible)
            HideNoteSearch();
    }

    private void NoteSearch_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            if ((Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
                FindPrevious();
            else
                FindNext();
            e.Handled = true;
        }
        else if (e.Key == Key.Down)
        {
            FindNext();
            e.Handled = true;
        }
        else if (e.Key == Key.Up)
        {
            FindPrevious();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            HideNoteSearch();
            e.Handled = true;
        }
    }

    private void SearchClose_Click(object sender, RoutedEventArgs e)
    {
        HideNoteSearch();
    }

    private void FindNext()
    {
        if (string.IsNullOrEmpty(_searchQuery)) return;
        var from = noteText.Selection.IsEmpty
            ? noteText.Document.ContentStart
            : noteText.Selection.End;
        var found = FindAndSelect(_searchQuery, from);
        if (!found)
            FindAndSelect(_searchQuery, noteText.Document.ContentStart);
        UpdateSearchCounter();
    }

    private void FindPrevious()
    {
        if (string.IsNullOrEmpty(_searchQuery)) return;

        // Walk from start to find all matches, pick the one just before selection end
        var text = new TextRange(noteText.Document.ContentStart, noteText.Document.ContentEnd).Text;
        var selEnd = noteText.Selection.Start;
        string query = _searchQuery;

        int lastIdx = -1;
        int at = 0;
        while ((at = text.IndexOf(query, at, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            var pos = FindTextPointer(noteText.Document.ContentStart, at);
            if (pos != null && pos.CompareTo(selEnd) < 0)
                lastIdx = at;
            else if (pos != null && pos.CompareTo(selEnd) >= 0)
                break;
            at += query.Length;
        }

        if (lastIdx >= 0)
        {
            var start = FindTextPointer(noteText.Document.ContentStart, lastIdx);
            if (start != null)
            {
                var end = FindTextPointer(start, query.Length);
                if (end != null)
                {
                    noteText.Selection.Select(start, end);
                    UpdateSearchCounter();
                    return;
                }
            }
        }

        // Wrap: go to last occurrence
        FindAndSelect(query, noteText.Document.ContentEnd);
        UpdateSearchCounter();
    }

    private void UpdateSearchCounter()
    {
        if (string.IsNullOrEmpty(_searchQuery))
        {
            searchCounter.Text = "";
            return;
        }

        // Count total matches
        var fullText = new TextRange(noteText.Document.ContentStart, noteText.Document.ContentEnd).Text;
        int total = 0, at = 0;
        while ((at = fullText.IndexOf(_searchQuery, at, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            total++;
            at += _searchQuery.Length;
        }

        if (total == 0)
        {
            searchCounter.Text = "Sin resultados";
            searchCounter.Foreground = new SolidColorBrush(Color.FromArgb(0x88, 0xFF, 0x66, 0x66));
            return;
        }

        // Find current match position
        var sel = noteText.Selection;
        int current = 0;
        if (!sel.IsEmpty)
        {
            int idx2 = 0;
            int matchNum = 0;
            while ((idx2 = fullText.IndexOf(_searchQuery, idx2, StringComparison.OrdinalIgnoreCase)) >= 0)
            {
                matchNum++;
                var pos = FindTextPointer(noteText.Document.ContentStart, idx2);
                if (pos != null && pos.CompareTo(sel.Start) == 0)
                {
                    current = matchNum;
                    break;
                }
                idx2 += _searchQuery.Length;
            }
        }

        searchCounter.Foreground = new SolidColorBrush(Color.FromArgb(0x88, 0xFF, 0xFF, 0xFF));
        searchCounter.Text = current > 0 ? $"{current}/{total}" : $"0/{total}";
    }

    private bool FindAndSelect(string query, TextPointer from)
    {
        var text = new TextRange(from, noteText.Document.ContentEnd).Text;
        int idx = text.IndexOf(query, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return false;

        var start = FindTextPointer(from, idx);
        if (start == null) return false;
        var end = FindTextPointer(start, query.Length);
        if (end == null) return false;

        noteText.Selection.Select(start, end);
        // Do NOT steal focus from search box
        return true;
    }

    private static TextPointer? FindTextPointer(TextPointer from, int offset)
    {
        var current = from;
        int remaining = offset;
        while (current != null)
        {
            if (current.GetPointerContext(LogicalDirection.Forward) == TextPointerContext.Text)
            {
                var text = current.GetTextInRun(LogicalDirection.Forward);
                if (remaining <= text.Length)
                    return current.GetPositionAtOffset(remaining);
                remaining -= text.Length;
            }
            current = current.GetNextContextPosition(LogicalDirection.Forward);
        }
        return null;
    }

    public void SetFontFamily(string fontFamily)
    {
        if (!string.IsNullOrEmpty(fontFamily))
            noteText.FontFamily = new FontFamily(fontFamily);
    }

}