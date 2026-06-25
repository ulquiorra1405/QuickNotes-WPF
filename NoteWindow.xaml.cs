using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using QuickNotes.Models;

namespace QuickNotes;

public partial class NoteWindow : Window
{
    private readonly Note _note;
    private readonly NotesStore _store;

    private string _searchQuery = "";



    public NoteWindow(Note note, NotesStore? store = null)
    {
        InitializeComponent();
        _note = note;
        _store = store ?? new NotesStore();

        DataContext = note;
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
            if (!colorPopup.IsOpen) return;
            if (e.OriginalSource is DependencyObject src && FindParent<Border>(src) == currentColorDot) return;
            colorPopup.IsOpen = false;
        };
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
            var doc = (FlowDocument)XamlReader.Parse(text);
            noteText.Document = doc;
        }
        catch
        {
            var para = new Paragraph(new Run(text));
            noteText.Document.Blocks.Clear();
            noteText.Document.Blocks.Add(para);
        }
    }

    private void SaveRichText()
    {
        var range = new TextRange(noteText.Document.ContentStart, noteText.Document.ContentEnd);
        _note.Text = range.Text.TrimEnd('\r', '\n');
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
            SavePosition();
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
        _store.Save();
        base.OnClosing(e);
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
        // ⏳ Función próximamente
        System.Windows.MessageBox.Show("Esta función estará disponible próximamente.", "Próximamente",
            MessageBoxButton.OK, MessageBoxImage.Information);
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

    private void NoteWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
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
            }
        }
        if (Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key == Key.C)
        {
            InsertCheckbox();
            e.Handled = true;
            return;
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