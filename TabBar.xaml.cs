using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using QuickNotes.Models;

namespace QuickNotes;

public partial class TabBar : Window
{
    private static TabBar? _instance;
    private readonly Dictionary<NoteWindow, (TabItem tab, Note note)> _tabs = [];
    private TabItem? _dragItem;
    private Point _dragStart;
    private bool _isDragging;
    private int _dragOrigIdx;
    private double _layoutOffset;
    private const double Gap = 4;
    private const double NoteGap = 8;
    private string _position = "right";
    private bool IsVertical => _position is "left" or "right";
    private bool IsHorizontal => _position is "top" or "bottom";

    public static TabBar Instance => _instance ??= new TabBar();

    public static void CloseIfOpen()
    {
        if (_instance is not null)
        {
            _instance.Close();
            _instance = null;
        }
    }

    private TabBar()
    {
        InitializeComponent();
        AllowDrop = true;
        Loaded += (_, _) => Reposition();
        SizeChanged += (_, _) => Reposition();
        DragOver += TabBar_DragOver;
        DragLeave += TabBar_DragLeave;
        Drop += TabBar_Drop;
    }

    private void TabBar_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetData("QuickNotesNote") is Note)
        {
            e.Effects = DragDropEffects.Move;
            Background = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF));
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
        e.Handled = true;
    }

    private void TabBar_DragLeave(object sender, DragEventArgs e)
    {
        Background = Brushes.Transparent;
    }

    private void TabBar_Drop(object sender, DragEventArgs e)
    {
        Background = Brushes.Transparent;
        if (e.Data.GetData("QuickNotesNote") is Note note)
        {
            e.Handled = true;
            MinimizeNote(note);
        }
    }

    public void MinimizeNote(Note note)
    {
        var existing = _tabs.FirstOrDefault(kvp => kvp.Value.note == note);
        NoteWindow? win = existing.Key;

        if (win == null)
        {
            foreach (Window w in Application.Current.Windows)
                if (w is NoteWindow nw && nw.DataContext == note)
                {
                    win = nw;
                    break;
                }
        }

        if (win != null)
        {
            if (!HasTab(win))
            {
                AddTab(win, note);
                ShowTabBar();
            }
            else
                RestoreTab(win);
            win.Hide();
        }
        else
        {
            var mainStore = (Application.Current.MainWindow as MainWindow)?.GetStore();
            win = new NoteWindow(note, mainStore);
            win.Owner = null;
            AddTab(win, note);
            ShowTabBar();
            win.Hide();
        }
        note.IsMinimized = true;
        note.LastModified = DateTime.Now;
        (Application.Current.MainWindow as MainWindow)?.DebounceSave();
    }

    private void Reposition()
    {
        var screen = SystemParameters.WorkArea;
        switch (_position)
        {
            case "left":
                Left = screen.Left + Gap;
                Top = screen.Top + (screen.Height - ActualHeight) / 2;
                break;
            case "right":
                Left = screen.Right - Width - Gap;
                Top = screen.Top + (screen.Height - ActualHeight) / 2;
                break;
            case "top":
                Left = screen.Left + (screen.Width - ActualWidth) / 2;
                Top = screen.Top + Gap;
                break;
            case "bottom":
                Left = screen.Left + (screen.Width - ActualWidth) / 2;
                Top = screen.Bottom - Height;
                break;
        }
    }

    public void SetPosition(string position)
    {
        _position = position;
        bool vert = IsVertical;

        SizeToContent = SizeToContent.Manual;
        scrollViewer.VerticalScrollBarVisibility = vert ? ScrollBarVisibility.Hidden : ScrollBarVisibility.Disabled;
        scrollViewer.HorizontalScrollBarVisibility = vert ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Hidden;
        tabPanel.Orientation = vert ? Orientation.Vertical : Orientation.Horizontal;

        foreach (var (_, (tab, _)) in _tabs)
            tab.SetPosition(position);

        if (vert)
        {
            Width = 32;
            Height = double.NaN;
            SizeToContent = SizeToContent.Height;
        }
        else
        {
            Height = 32;
            Width = double.NaN;
            SizeToContent = SizeToContent.Width;
        }
        InvalidateVisual();
    }

    public void AddTab(NoteWindow win, Note note)
    {
        if (_tabs.ContainsKey(win)) return;

        var tab = new TabItem(note);
        tab.SetPosition(_position);
        tab.MouseDown += (_, e) => Tab_MouseDown(tab, e);
        tab.MouseMove += (_, e) => Tab_MouseMove(tab, e);
        tab.MouseUp += (_, e) => Tab_MouseUp(tab, win, note, e);
        tabPanel.Children.Add(tab);
        _tabs[win] = (tab, note);
        note.PropertyChanged += OnNotePropertyChanged;
    }

    public void ShowTabBar()
    {
        if (_tabs.Count == 0 || IsVisible) return;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            Show();
            Reposition();
            Activate();
        }), DispatcherPriority.Input);
    }

    public void RemoveTab(NoteWindow win)
    {
        if (!_tabs.TryGetValue(win, out var entry)) return;
        var (tab, note) = entry;

        note.PropertyChanged -= OnNotePropertyChanged;
        tabPanel.Children.Remove(tab);
        _tabs.Remove(win);
        if (_tabs.Count == 0) Hide();
    }

    public void RemoveTabsByNote(Note note)
    {
        List<NoteWindow> toRemove = [];
        foreach (var (win, (_, n)) in _tabs)
            if (n == note) toRemove.Add(win);
        foreach (var win in toRemove)
        {
            RemoveTab(win);
            win.Close();
        }
    }

    public void UpdateTab(NoteWindow win)
    {
        if (_tabs.TryGetValue(win, out var entry))
            entry.tab.Refresh();
    }

    public bool HasTab(NoteWindow win) => _tabs.ContainsKey(win);

    public int TabCount => _tabs.Count;

    public void FocusNextTab(ref int cycleIndex)
    {
        if (_tabs.Count == 0) return;
        var keys = _tabs.Keys.ToArray();
        cycleIndex %= keys.Length;
        var win = keys[cycleIndex];
        cycleIndex = (cycleIndex + 1) % keys.Length;

        var tabPanelChildren = tabPanel.Children;
        for (int i = 0; i < tabPanelChildren.Count; i++)
        {
            if (tabPanelChildren[i] is TabItem tab && _tabs.TryGetValue(win, out var entry) && entry.tab == tab)
            {
                tab.Ghost();
                win.OnTabOpened();
                positionNoteWindow(win, i);
                win.Show();
                win.Activate();
                win.WindowState = WindowState.Normal;
                break;
            }
        }
    }

    private void positionNoteWindow(NoteWindow win, int cascadeIndex)
    {
        double offset = cascadeIndex * 25;
        switch (_position)
        {
            case "left":
                win.Left = Left + Width + NoteGap;
                win.Top = Top + offset;
                break;
            case "right":
                win.Left = Left - win.Width - NoteGap;
                win.Top = Top + offset;
                break;
            case "top":
                win.Top = Top + Height + NoteGap;
                win.Left = Left + offset;
                break;
            case "bottom":
                win.Top = Top - win.Height - NoteGap;
                win.Left = Left + offset;
                break;
        }
    }

    public void RestoreTab(NoteWindow win)
    {
        if (_tabs.TryGetValue(win, out var entry))
            entry.tab.Restore();
    }

    private void OnNotePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not Note note || e.PropertyName is not ("Title" or "Color")) return;
        foreach (var (_, (tab, n)) in _tabs)
        {
            if (n == note) { tab.Refresh(); break; }
        }
    }

    private void Tab_MouseDown(TabItem tab, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            _dragItem = tab;
            _dragStart = e.GetPosition(tabPanel);
            _dragOrigIdx = tabPanel.Children.IndexOf(tab);
            _layoutOffset = 0;
            _isDragging = false;
        }
    }

    private void Tab_MouseMove(TabItem tab, MouseEventArgs e)
    {
        if (_dragItem != tab || e.LeftButton != MouseButtonState.Pressed) return;

        var pos = e.GetPosition(tabPanel);
        bool vert = IsVertical;
        var delta = vert ? pos.Y - _dragStart.Y : pos.X - _dragStart.X;

        if (!_isDragging && Math.Abs(delta) > 6)
        {
            _isDragging = true;
            tab.CaptureMouse();
            tab.Opacity = 0.8;
        }

        if (!_isDragging) return;

        var children = tabPanel.Children;
        var idx = children.IndexOf(tab);
        var count = children.Count;
        var step = (int)(vert ? TabItem.TabSize : TabItem.TabSizeH) + 2;
        var threshold = step / 2;

        var visualOff = delta - _layoutOffset;
        if (idx == 0) visualOff = Math.Max(visualOff, 0);
        if (idx == count - 1) visualOff = Math.Min(visualOff, 0);
        tab.RenderTransform = vert
            ? new TranslateTransform(0, visualOff)
            : new TranslateTransform(visualOff, 0);

        if (visualOff < -threshold && idx > 0)
        {
            children.RemoveAt(idx);
            children.Insert(idx - 1, tab);
            _layoutOffset -= step;
        }
        else if (visualOff > threshold && idx < count - 1)
        {
            children.RemoveAt(idx);
            children.Insert(idx + 1, tab);
            _layoutOffset += step;
        }
    }

    private void Tab_MouseUp(TabItem tab, NoteWindow win, Note note, MouseButtonEventArgs e)
    {
        if (_isDragging)
        {
            tab.RenderTransform = null;
            tab.Opacity = 1;
            _isDragging = false;
            _dragItem = null;
            tab.ReleaseMouseCapture();
            return;
        }

        _dragItem = null;

        tab.Ghost();
        win.OnTabOpened();
        var cascadeIdx = tabPanel.Children.IndexOf(tab);
        positionNoteWindow(win, cascadeIdx);
        win.Show();
        win.Activate();
        win.WindowState = WindowState.Normal;
    }

    private class TabItem : Border
    {
        public const double TabSize = 70;
        public const double TabSizeH = 70;

        private readonly Note _note;
        private readonly TextBlock _label;
        private readonly SolidColorBrush _bgBrush;
        private readonly SolidColorBrush _fgBrush;
        private readonly DropShadowEffect _shadow;
        private Color _fgBase;
        private double _originalSize;
        private bool _isGhosted;
        private DispatcherTimer? _leaveTimer;
        private string _position = "right";

        public TabItem(Note note)
        {
            _note = note;

            Cursor = Cursors.Hand;
            Focusable = true;
            Margin = new Thickness(0, 0, 0, 2);
            ClipToBounds = true;
            Opacity = 0.7;

            _shadow = new DropShadowEffect
            {
                Direction = 225,
                ShadowDepth = 3,
                BlurRadius = 5,
                Opacity = 0.3,
                Color = Colors.Black,
            };
            Effect = _shadow;

            var text = GetLabelText(note);
            var size = CalcSize(text);
            _fgBase = IsDarkBg(_note.Color)
                ? Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF)
                : Color.FromArgb(0xCC, 0x1A, 0x1A, 0x1A);
            _fgBrush = new SolidColorBrush(_fgBase);
            _bgBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(_note.Color));
            Background = _bgBrush;
            _label = new TextBlock
            {
                Text = text,
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                Foreground = _fgBrush,
                TextTrimming = TextTrimming.CharacterEllipsis,
                LayoutTransform = new RotateTransform(-90),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                MaxWidth = size - 8,
                Margin = new Thickness(0, 0, 0, 0),
            };

            Child = _label;
            _originalSize = size;
            applyVerticalLayout(size);

            MouseEnter += OnHoverEnter;
            MouseLeave += OnHoverLeave;
        }

        public void SetPosition(string position)
        {
            _position = position;
            bool vert = position is "left" or "right";
            if (vert)
            {
                var size = CalcSize(GetLabelText(_note));
                applyVerticalLayout(size);
            }
            else
            {
                var size = CalcSizeH(GetLabelText(_note));
                applyHorizontalLayout(size);
            }
        }

        private void applyVerticalLayout(double height)
        {
            Width = 32;
            Height = _isGhosted ? Height : height;
            MinWidth = 32;
            MaxWidth = 32;
            MinHeight = 0;
            MaxHeight = double.PositiveInfinity;
            Margin = new Thickness(0, 0, 0, 2);
            _label.LayoutTransform = new RotateTransform(-90);
            _label.MaxWidth = height - 8;
            CornerRadius = _position == "left"
                ? new CornerRadius(0, 6, 6, 0)
                : new CornerRadius(6, 0, 0, 6);
            HorizontalAlignment = HorizontalAlignment.Stretch;
        }

        private void applyHorizontalLayout(double width)
        {
            Width = _isGhosted ? Width : width;
            Height = 32;
            MinWidth = 0;
            MaxWidth = double.PositiveInfinity;
            MinHeight = 32;
            MaxHeight = 32;
            Margin = new Thickness(0, 0, 2, 0);
            _label.LayoutTransform = null;
            _label.MaxWidth = width - 8;
            CornerRadius = _position == "top"
                ? new CornerRadius(0, 0, 6, 6)
                : new CornerRadius(6, 6, 0, 0);
            VerticalAlignment = VerticalAlignment.Stretch;
        }

        private void OnHoverEnter(object sender, MouseEventArgs e)
        {
            if (_instance is { _isDragging: true }) return;

            _leaveTimer?.Stop();
            _leaveTimer = null;

            BeginAnimation(OpacityProperty, AnimationHelper.MakeAnimation(1, 200));

            _label.Effect = new DropShadowEffect
            {
                Direction = 270,
                ShadowDepth = 1,
                BlurRadius = 4,
                Opacity = 0,
                Color = Colors.White,
            };
            _label.Effect.BeginAnimation(DropShadowEffect.OpacityProperty, AnimationHelper.MakeAnimation(0.5, 150));

            _bgBrush.BeginAnimation(SolidColorBrush.ColorProperty,
                new ColorAnimation(Intensify(_bgBrush.Color), AnimationHelper.Dur(300)));
            var hoverFg = IsDarkBg(_note.Color) ? Colors.White : Colors.Black;
            _fgBrush.BeginAnimation(SolidColorBrush.ColorProperty,
                new ColorAnimation(Color.FromArgb(0xFF, hoverFg.R, hoverFg.G, hoverFg.B), AnimationHelper.Dur(300)));
        }

        private void OnHoverLeave(object sender, MouseEventArgs e)
        {
            _leaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
            _leaveTimer.Tick += (_, _) =>
            {
                _leaveTimer?.Stop();
                _leaveTimer = null;
                if (IsMouseOver) return;

                BeginAnimation(OpacityProperty, AnimationHelper.MakeAnimation(0.7, 300));
                _bgBrush.BeginAnimation(SolidColorBrush.ColorProperty,
                    new ColorAnimation((Color)ColorConverter.ConvertFromString(_note.Color), AnimationHelper.Dur(300)));
                _fgBrush.BeginAnimation(SolidColorBrush.ColorProperty,
                    new ColorAnimation(_fgBase, AnimationHelper.Dur(300)));

                if (_label.Effect is DropShadowEffect shadow)
                {
                    var fadeOut = AnimationHelper.MakeAnimation(0, 300);
                    fadeOut.Completed += (_, _) => _label.Effect = null;
                    shadow.BeginAnimation(DropShadowEffect.OpacityProperty, fadeOut);
                }
            };
            _leaveTimer.Start();
        }

        private static Color Intensify(Color c)
        {
            double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
            double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
            double h = 0, s, l = (max + min) / 2;

            if (max != min)
            {
                double d = max - min;
                s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
                if (max == r) h = (g - b) / d + (g < b ? 6 : 0);
                else if (max == g) h = (b - r) / d + 2;
                else h = (r - g) / d + 4;
                h /= 6;
            }
            else s = 0;

            s = Math.Min(1, s * 1.5);
            l = l + (0.5 - l) * 0.45;

            if (s == 0) { r = g = b = l; }
            else
            {
                static double H2R(double p, double q, double t)
                {
                    if (t < 0) t += 1; if (t > 1) t -= 1;
                    if (t < 1.0 / 6) return p + (q - p) * 6 * t;
                    if (t < 1.0 / 2) return q;
                    if (t < 2.0 / 3) return p + (q - p) * (2.0 / 3 - t) * 6;
                    return p;
                }
                double qq = l < 0.5 ? l * (1 + s) : l + s - l * s;
                double pp = 2 * l - qq;
                r = H2R(pp, qq, h + 1.0 / 3);
                g = H2R(pp, qq, h);
                b = H2R(pp, qq, h - 1.0 / 3);
            }

            return Color.FromArgb(c.A, (byte)(r * 255), (byte)(g * 255), (byte)(b * 255));
        }

        public void Refresh()
        {
            _bgBrush.BeginAnimation(SolidColorBrush.ColorProperty, null);
            _fgBrush.BeginAnimation(SolidColorBrush.ColorProperty, null);
            var text = GetLabelText(_note);
            _label.Text = text;
            if (_position is "left" or "right")
            {
                var h = CalcSize(text);
                _originalSize = h;
                _label.MaxWidth = h - 8;
                if (!_isGhosted) Height = h;
            }
            else
            {
                var w = CalcSizeH(text);
                _originalSize = w;
                _label.MaxWidth = w - 8;
                if (!_isGhosted) Width = w;
            }
            _bgBrush.Color = (Color)ColorConverter.ConvertFromString(_note.Color);
            _fgBase = IsDarkBg(_note.Color)
                ? Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF)
                : Color.FromArgb(0xCC, 0x1A, 0x1A, 0x1A);
            _fgBrush.Color = _fgBase;
        }

        public void Ghost()
        {
            _isGhosted = true;
            _label.Visibility = Visibility.Collapsed;
            bool vert = _position is "left" or "right";
            if (vert)
            {
                HorizontalAlignment = _position == "left" ? HorizontalAlignment.Left : HorizontalAlignment.Right;
                BeginAnimation(HeightProperty, AnimationHelper.MakeAnimation(10, 200));
                BeginAnimation(WidthProperty, AnimationHelper.MakeAnimation(24, 200));
            }
            else
            {
                VerticalAlignment = _position == "top" ? VerticalAlignment.Top : VerticalAlignment.Bottom;
                BeginAnimation(WidthProperty, AnimationHelper.MakeAnimation(10, 200));
                BeginAnimation(HeightProperty, AnimationHelper.MakeAnimation(24, 200));
            }
            BeginAnimation(OpacityProperty, AnimationHelper.MakeAnimation(0.6, 200));
        }

        public void Restore()
        {
            _isGhosted = false;
            _label.Visibility = Visibility.Visible;
            bool vert = _position is "left" or "right";
            if (vert)
            {
                var wa = AnimationHelper.MakeAnimation(32, 200);
                wa.Completed += (_, _) => HorizontalAlignment = HorizontalAlignment.Stretch;
                BeginAnimation(HeightProperty, AnimationHelper.MakeAnimation(_originalSize, 200));
                BeginAnimation(WidthProperty, wa);
            }
            else
            {
                var ha = AnimationHelper.MakeAnimation(32, 200);
                ha.Completed += (_, _) => VerticalAlignment = VerticalAlignment.Stretch;
                BeginAnimation(WidthProperty, AnimationHelper.MakeAnimation(_originalSize, 200));
                BeginAnimation(HeightProperty, ha);
            }
            BeginAnimation(OpacityProperty, AnimationHelper.MakeAnimation(1, 200));
        }

        private static double CalcSize(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return TabSize;
            return Math.Clamp(text.Length * 6.5 + 20, TabSize, TabSize * 2);
        }

        private static double CalcSizeH(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return TabSizeH;
            return Math.Clamp(text.Length * 7.5 + 24, TabSizeH, TabSizeH * 2);
        }

        private static string GetLabelText(Note note)
        {
            var title = (note.Title ?? "").Trim();
            if (string.IsNullOrWhiteSpace(title)) return "Note";
            var words = title.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return words.Length >= 2 ? $"{words[0]} {words[1]}" : words[0];
        }

        private static bool IsDarkBg(string? hex)
        {
            if (string.IsNullOrEmpty(hex) || hex.Length < 7) return false;
            var r = Convert.ToInt32(hex.Substring(1, 2), 16);
            var g = Convert.ToInt32(hex.Substring(3, 2), 16);
            var b = Convert.ToInt32(hex.Substring(5, 2), 16);
            return (0.299 * r + 0.587 * g + 0.114 * b) < 140;
        }
    }
}
