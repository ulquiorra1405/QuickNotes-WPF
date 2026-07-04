using System.Media;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using QuickNotes.Models;

namespace QuickNotes.Views;

public partial class ReminderToastWindow : Window
{
    public event Action<Guid>? ReminderSnoozed;

    private readonly Reminder _reminder;
    private readonly NotesStore _store;
    private readonly DispatcherTimer _soundTimer = new();
    private bool _isClosed;
    private static SoundPlayer? _soundPlayer;
    private static string? _soundFilePath;
    private Color _btnHoverColor;

    private const double FADE_DURATION_MS = 300;

    // Theme colors
    private static readonly Color DarkBg = Color.FromRgb(0x1E, 0x1E, 0x1E);
    private static readonly Color DarkBorder = Color.FromRgb(0x3A, 0x3A, 0x3A);
    private static readonly Color DarkText = Color.FromRgb(0xDD, 0xDD, 0xDD);
    private static readonly Color DarkMuted = Color.FromRgb(0x99, 0x99, 0x99);
    private static readonly Color DarkBtnBg = Color.FromRgb(0x3A, 0x3A, 0x3A);
    private static readonly Color DarkBtnHover = Color.FromRgb(0x55, 0x55, 0x55);

    private static readonly Color LightBg = Color.FromRgb(0xF5, 0xF5, 0xF5);
    private static readonly Color LightBorder = Color.FromRgb(0xCC, 0xCC, 0xCC);
    private static readonly Color LightText = Color.FromRgb(0x1A, 0x1A, 0x1A);
    private static readonly Color LightMuted = Color.FromRgb(0x66, 0x66, 0x66);
    private static readonly Color LightBtnBg = Color.FromRgb(0xE0, 0xE0, 0xE0);
    private static readonly Color LightBtnHover = Color.FromRgb(0xC8, 0xC8, 0xC8);

    public ReminderToastWindow(Reminder reminder, NotesStore store)
    {
        InitializeComponent();
        _reminder = reminder;
        _store = store;

        ApplyTheme();
        WireButtonHover();

        var note = store.Notes.FirstOrDefault(n => n.Id == reminder.NoteId);
        titleText.Text = reminder.Title;

        if (!string.IsNullOrEmpty(reminder.Description))
            previewText.Text = reminder.Description;
        else if (note != null)
            previewText.Text = note.PlainText;
        else
            previewText.Text = "";

        _soundTimer.Interval = TimeSpan.FromSeconds(15);
        _soundTimer.Tick += (_, _) => PlayReminderSound();
    }

    private string GetEffectiveTheme()
    {
        var theme = _store.Theme;
        if (theme != "system") return theme;
        try
        {
            var reg = Microsoft.Win32.Registry.GetValue(
                @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                "AppsUseLightTheme", "1");
            return (reg is int i && i == 1) ? "light" : "dark";
        }
        catch { return "dark"; }
    }

    private void ApplyTheme()
    {
        bool isDark = GetEffectiveTheme() == "dark";

        var bg = isDark ? DarkBg : LightBg;
        var border = isDark ? DarkBorder : LightBorder;
        var text = isDark ? DarkText : LightText;
        var muted = isDark ? DarkMuted : LightMuted;
        var btnBg = isDark ? DarkBtnBg : LightBtnBg;
        _btnHoverColor = isDark ? DarkBtnHover : LightBtnHover;

        toastBorder.Background = new SolidColorBrush(bg);
        toastBorder.BorderBrush = new SolidColorBrush(border);
        toastBorder.BorderThickness = new Thickness(1);

        bellIcon.Foreground = new SolidColorBrush(text);
        titleText.Foreground = new SolidColorBrush(text);
        previewText.Foreground = new SolidColorBrush(muted);

        var btnFg = new SolidColorBrush(text);
        var btnBgBrush = new SolidColorBrush(btnBg);

        snoozeBtn.Background = btnBgBrush;
        snoozeBtn.Foreground = btnFg;
        dismissBtn.Background = btnBgBrush;
        dismissBtn.Foreground = btnFg;
        openBtn.Background = btnBgBrush;
        openBtn.Foreground = btnFg;
    }

    private void WireButtonHover()
    {
        void OnEnter(object s, System.Windows.Input.MouseEventArgs e)
        {
            if (s is Button btn)
                btn.Background = new SolidColorBrush(_btnHoverColor);
        }
        void OnLeave(object s, System.Windows.Input.MouseEventArgs e)
        {
            if (s is Button btn)
            {
                bool isDark = GetEffectiveTheme() == "dark";
                btn.Background = new SolidColorBrush(isDark ? DarkBtnBg : LightBtnBg);
            }
        }

        snoozeBtn.MouseEnter += OnEnter;
        snoozeBtn.MouseLeave += OnLeave;
        dismissBtn.MouseEnter += OnEnter;
        dismissBtn.MouseLeave += OnLeave;
        openBtn.MouseEnter += OnEnter;
        openBtn.MouseLeave += OnLeave;
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        var area = Helpers.MonitorHelper.GetMonitorWorkingArea(this);
        Left = area.Right - Width - 16;
        Top = area.Bottom - Height - 16;

        var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(FADE_DURATION_MS))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        toastBorder.BeginAnimation(OpacityProperty, fadeIn);

        StartPulsingBorder();
        PlayReminderSound();
        _soundTimer.Start();
    }

    private void StartPulsingBorder()
    {
        bool isDark = GetEffectiveTheme() == "dark";
        var baseColor = isDark ? Color.FromRgb(0x3A, 0x3A, 0x3A) : Color.FromRgb(0xCC, 0xCC, 0xCC);
        var pulseColor = isDark ? Color.FromRgb(0x5B, 0x8E, 0xC9) : Color.FromRgb(0x4A, 0x7D, 0xB5);

        var borderBrush = new SolidColorBrush(baseColor);
        toastBorder.BorderBrush = borderBrush;

        var pulse = new ColorAnimation(baseColor, pulseColor, new Duration(TimeSpan.FromSeconds(1.5)))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        };
        borderBrush.BeginAnimation(SolidColorBrush.ColorProperty, pulse);
    }

    private static void PlayReminderSound()
    {
        try
        {
            if (_soundPlayer == null)
            {
                var asm = typeof(ReminderToastWindow).Assembly;
                using var stream = asm.GetManifestResourceStream("QuickNotes.Sound.alarm.wav");
                if (stream == null) return;

                _soundFilePath = System.IO.Path.Combine(
                    System.IO.Path.GetTempPath(), "QuickNotes_alarm.wav");
                using var fileStream = System.IO.File.Create(_soundFilePath);
                stream.CopyTo(fileStream);

                _soundPlayer = new SoundPlayer(_soundFilePath);
                _soundPlayer.Load();
            }
            _soundPlayer.Play();
        }
        catch { }
    }

    private void OpenBtn_Click(object sender, RoutedEventArgs e)
    {
        BeginClose();
        _store.DeleteReminder(_reminder.Id);
        var note = _store.Notes.FirstOrDefault(n => n.Id == _reminder.NoteId);
        if (note == null) return;

        Dispatcher.BeginInvoke(() =>
        {
            var existing = Application.Current.Windows.OfType<NoteWindow>()
                .FirstOrDefault(w => w.DataContext is Note n && n.Id == note.Id);
            if (existing == null)
            {
                var win = new NoteWindow(note, _store);
                win.Show();
            }
            else
            {
                existing.Show();
                existing.Activate();
                if (existing.WindowState == WindowState.Minimized)
                    existing.WindowState = WindowState.Normal;
            }
        });
    }

    private void SnoozeBtn_Click(object sender, RoutedEventArgs e)
    {
        var contextMenu = new System.Windows.Controls.ContextMenu();
        bool isDark = GetEffectiveTheme() == "dark";

        foreach (var (label, minutes) in new[] { ("5 minutos", 5), ("10 minutos", 10), ("30 minutos", 30) })
        {
            var item = new System.Windows.Controls.MenuItem
            {
                Header = label,
                FontSize = 12,
                Foreground = new SolidColorBrush(isDark ? Color.FromRgb(0xDD, 0xDD, 0xDD) : Color.FromRgb(0x1A, 0x1A, 0x1A)),
                Background = new SolidColorBrush(isDark ? Color.FromRgb(0x2A, 0x2A, 0x2A) : Color.FromRgb(0xF0, 0xF0, 0xF0)),
            };
            var mins = minutes;
            item.Click += (_, _) => ApplySnooze(mins);
            contextMenu.Items.Add(item);
        }

        contextMenu.PlacementTarget = snoozeBtn;
        contextMenu.IsOpen = true;
    }

    private void ApplySnooze(int minutes)
    {
        _reminder.DueAt = DateTime.Now.AddMinutes(minutes);
        _reminder.IsCompleted = false;
        _store.SaveReminder(_reminder);
        ReminderSnoozed?.Invoke(_reminder.Id);
        BeginClose();
    }

    private void DismissBtn_Click(object sender, RoutedEventArgs e)
    {
        _store.DeleteReminder(_reminder.Id);
        BeginClose();
    }

    private void BeginClose()
    {
        if (_isClosed) return;
        _isClosed = true;
        _soundTimer.Stop();

        bool isDark = GetEffectiveTheme() == "dark";
        toastBorder.BorderBrush = new SolidColorBrush(isDark ? Color.FromRgb(0x3A, 0x3A, 0x3A) : Color.FromRgb(0xCC, 0xCC, 0xCC));

        var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(FADE_DURATION_MS))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
        };
        fadeOut.Completed += (_, _) => Close();
        toastBorder.BeginAnimation(OpacityProperty, fadeOut);
    }
}
