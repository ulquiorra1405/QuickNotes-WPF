using System.IO;
using System.Linq;
using System.Text;
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
            if (!colorPopup.IsOpen && !emojiPopup.IsOpen && !highlightPopup.IsOpen) return;
            if (e.OriginalSource is DependencyObject src)
            {
                if (colorPopup.IsOpen && FindParent<Border>(src) == currentColorDot) return;
                if (emojiPopup.IsOpen && FindParent<Button>(src) == emojiBtn) return;
                if (highlightPopup.IsOpen && FindParent<Border>(src) == highlightBtn) return;
            }
            colorPopup.IsOpen = false;
            emojiPopup.IsOpen = false;
            highlightPopup.IsOpen = false;
        };

        // Image paste & drag-drop
        noteText.AllowDrop = true;
        noteText.Drop += NoteText_Drop;
        noteText.PreviewDragOver += NoteText_PreviewDragOver;

        // Auto-pairing
        PreviewTextInput += NoteWindow_PreviewTextInput;

        // Highlight popup
        BuildHighlightPicker();
        highlightBtn.Background = new SolidColorBrush(_currentHighlightColor);
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
        var fg = new SolidColorBrush(dark ? Colors.White : Color.FromArgb(0x88, 0x3A, 0x3A, 0x3A));
        SetPanelForeground(titleRightPanel, fg);
        SetPanelForeground(bottomRightPanel, fg);
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

        // Prefixes for bullet / checklist
        string[] prefixes = ["- ", "* ", "\u2022 ", "\u25a1 ", "\u2610 ", "[] ", "[x] "];

        foreach (var prefix in prefixes)
        {
            if (!text.StartsWith(prefix)) continue;

            if (text.Length == prefix.TrimEnd().Length)
            {
                // Only prefix → exit list (let default Enter create empty para)
                return false;
            }

            // Has content → continue with same prefix
            var doc = noteText.Document;
            var newPara = new Paragraph(new Run(prefix.TrimEnd()));

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

    private void BuildHighlightPicker()
    {
        highlightPanel.Children.Clear();
        foreach (var color in HighlightColors)
        {
            var border = new Border
            {
                Width = 14,
                Height = 14,
                CornerRadius = new CornerRadius(2),
                Margin = new Thickness(2),
                Cursor = Cursors.Hand,
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 0x00, 0x00, 0x00)),
                BorderThickness = new Thickness(1),
                Background = new SolidColorBrush(color),
            };
            var c = color; // capture
            border.MouseDown += (_, e) =>
            {
                if (e.LeftButton == MouseButtonState.Pressed)
                    SelectHighlightColor(c, border);
            };
            border.MouseEnter += (_, _) =>
                border.BorderBrush = new SolidColorBrush(Color.FromArgb(0x80, 0x00, 0x00, 0x00));
            border.MouseLeave += (_, _) =>
                border.BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 0x00, 0x00, 0x00));
            highlightPanel.Children.Add(border);
        }
    }

    private void HighlightBtn_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            highlightPopup.IsOpen = !highlightPopup.IsOpen;
    }

    private void SelectHighlightColor(Color color, Border selected)
    {
        _currentHighlightColor = color;
        highlightBtn.Background = new SolidColorBrush(color);
        highlightPopup.IsOpen = false;
        ApplyHighlight(color);
    }

    private void ApplyHighlight(Color color)
    {
        var sel = noteText.Selection;
        if (sel.IsEmpty) return;

        var range = new TextRange(sel.Start, sel.End);
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
        if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            // Ctrl+V: intercept images before RichTextBox handles it
            if (e.Key == Key.V)
            {
                if (Clipboard.ContainsImage())
                {
                    e.Handled = true;
                    InsertImageFromClipboard();
                    return;
                }
                // Text paste — let it fall through to RichTextBox
                return;
            }

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
            }
        }
        if (Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key == Key.C)
        {
            InsertCheckbox();
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
            }
        }

        if (e.Key == Key.Escape && noteSearchBorder.Visibility == Visibility.Visible)
        {
            HideNoteSearch();
            e.Handled = true;
            return;
        }
        if (noteSearchBorder.Visibility == Visibility.Visible)
        {
            if (e.Key == Key.Enter || e.Key == Key.F3)
            {
                FindNext();
                e.Handled = true;
            }
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

    private void InsertImageFromClipboard()
    {
        try
        {
            var img = Clipboard.GetImage();
            if (img == null) return;

            Directory.CreateDirectory(_imageFolder);
            var fileName = $"{Guid.NewGuid()}.png";
            var filePath = System.IO.Path.Combine(_imageFolder, fileName);

            using var fs = new FileStream(filePath, FileMode.Create);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(img));
            encoder.Save(fs);

            InsertImageBlock(filePath);
        }
        catch (Exception ex) { ErrorLog.Write(ex, "InsertImageFromClipboard"); }
    }

    private void InsertImageBlock(string fullPath)
    {
        var img = new System.Windows.Controls.Image();
        img.Source = new BitmapImage(new Uri(fullPath));
        img.MaxWidth = Math.Max(Width - 40, 200);
        img.Margin = new Thickness(0, 4, 0, 4);
        img.Stretch = Stretch.Uniform;

        var container = new BlockUIContainer(img);

        // Insert at caret position
        var caretPos = noteText.CaretPosition;
        var caretPara = caretPos.Paragraph;

        if (caretPara != null && caretPara.Parent is FlowDocument doc)
        {
            doc.Blocks.InsertAfter(caretPara, container);
        }
        else if (caretPara != null && caretPara.Parent is Section sec)
        {
            sec.Blocks.InsertAfter(caretPara, container);
        }
        else
        {
            // Fallback: add to the end of document
            noteText.Document.Blocks.Add(container);
        }

        // Place caret after the image block
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

    private void NoteText_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        try
        {
            var pt = e.GetPosition(noteText);
            var pos = noteText.GetPositionFromPoint(pt, false);
            if (pos == null) return;

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

    private void BoldBtn_Click(object sender, RoutedEventArgs e) => ToggleBold();
    private void ItalicBtn_Click(object sender, RoutedEventArgs e) => ToggleItalic();
    private void UnderlineBtn_Click(object sender, RoutedEventArgs e) => ToggleUnderline();
    private void StrikeBtn_Click(object sender, RoutedEventArgs e) => ToggleStrikethrough();
    private void BulletBtn_Click(object sender, RoutedEventArgs e) => ToggleBulletList();
    private void NumberBtn_Click(object sender, RoutedEventArgs e) => ToggleNumberList();
    private void CheckboxBtn_Click(object sender, RoutedEventArgs e) => InsertCheckbox();

    private void UndoBtn_Click(object sender, RoutedEventArgs e) => Undo();
    private void RedoBtn_Click(object sender, RoutedEventArgs e) => Redo();

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
        noteSearchBorder.Visibility = Visibility.Visible;
        noteSearchBox.Focus();
        noteSearchBox.SelectAll();
    }

    private void HideNoteSearch()
    {
        noteSearchBorder.Visibility = Visibility.Collapsed;
        noteSearchBox.Clear();
        noteText.Focus();
    }

    private void NoteSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        var query = noteSearchBox.Text;
        noteSearchHint.Visibility = string.IsNullOrEmpty(query)
            ? Visibility.Visible : Visibility.Collapsed;

        if (string.IsNullOrEmpty(query))
        {
            noteSearchBorder.Visibility = Visibility.Collapsed;
            noteText.Focus();
            return;
        }

        _searchQuery = query;
        FindAndSelect(query, noteText.Document.ContentStart);
    }

    private void NoteSearch_LostFocus(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(noteSearchBox.Text))
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
        noteText.Focus();
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