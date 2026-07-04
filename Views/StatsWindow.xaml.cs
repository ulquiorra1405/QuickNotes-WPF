using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using QuickNotes.Helpers;
using System.Windows.Shapes;
using QuickNotes.Models;

namespace QuickNotes.Views;

public partial class StatsWindow : Window
{
    private readonly NotesStore _store;

    private static readonly Color DarkBg = Color.FromRgb(0x1E, 0x1E, 0x1E);
    private static readonly Color DarkCard = Color.FromRgb(0x2A, 0x2A, 0x2A);
    private static readonly Color DarkText = Color.FromRgb(0xDD, 0xDD, 0xDD);
    private static readonly Color DarkMuted = Color.FromRgb(0x99, 0x99, 0x99);
    private static readonly Color DarkTitleBg = Color.FromRgb(0x26, 0x26, 0x26);

    private static readonly Color LightBg = Color.FromRgb(0xF0, 0xF0, 0xF0);
    private static readonly Color LightCard = Color.FromRgb(0xFF, 0xFF, 0xFF);
    private static readonly Color LightText = Color.FromRgb(0x1A, 0x1A, 0x1A);
    private static readonly Color LightMuted = Color.FromRgb(0x88, 0x88, 0x88);
    private static readonly Color LightTitleBg = Color.FromRgb(0xE0, 0xE0, 0xE0);

    // Calendar intensity shades (dark bg)
    private static readonly Color[] DarkCalColors =
    [
        Color.FromRgb(0x2A, 0x2A, 0x2A), // none
        Color.FromRgb(0x0E, 0x44, 0x2E), // low
        Color.FromRgb(0x19, 0x6E, 0x41), // med
        Color.FromRgb(0x27, 0x9E, 0x50), // high
        Color.FromRgb(0x3E, 0xC4, 0x6B), // max
    ];

    // Calendar intensity shades (light bg)
    private static readonly Color[] LightCalColors =
    [
        Color.FromRgb(0xEB, 0xED, 0xF0), // none
        Color.FromRgb(0x9E, 0xE0, 0xAF), // low
        Color.FromRgb(0x66, 0xBB, 0x6A), // med
        Color.FromRgb(0x2E, 0x96, 0x46), // high
        Color.FromRgb(0x1B, 0x7A, 0x34), // max
    ];

    public StatsWindow(NotesStore store)
    {
        InitializeComponent();
        _store = store;
        ApplyTheme();
        ComputeAndRender();
    }

    private string GetTheme() => _store.Theme;
    private bool IsDark => GetTheme() == "dark" || (GetTheme() == "system" && IsSystemDark());
    private static bool IsSystemDark()
    {
        try
        {
            var reg = Microsoft.Win32.Registry.GetValue(
                @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                "AppsUseLightTheme", "1");
            return !(reg is int i && i == 1);
        }
        catch { return true; }
    }

    private void ApplyTheme()
    {
        bool dark = IsDark;
        var bg = dark ? DarkBg : LightBg;
        var card = dark ? DarkCard : LightCard;
        var text = dark ? DarkText : LightText;
        var muted = dark ? DarkMuted : LightMuted;
        var titleBg = dark ? DarkTitleBg : LightTitleBg;

        rootBorder.Background = new SolidColorBrush(bg);
        titleBar.Background = new SolidColorBrush(titleBg);
        closeBtn.Foreground = new SolidColorBrush(text);

        foreach (var box in new[] { summaryBox, streakBox, calendarBox, weekBox, topWordsBox, emptyBox })
            box.Background = new SolidColorBrush(card);

        // Stat labels
        foreach (var tb in new[] { totalNotesValue, totalWordsValue, avgWordsValue, longestValue })
            tb.Foreground = new SolidColorBrush(text);
        foreach (var tb in new[] { currentStreakValue, bestStreakValue })
            tb.Foreground = new SolidColorBrush(text);
    }

    private void ComputeAndRender()
    {
        var stats = StatsCalculator.Compute(_store.Notes);

        if (stats.TotalNotes == 0)
        {
            emptyBox.Visibility = Visibility.Visible;
            return;
        }

        // Summary
        totalNotesValue.Text = stats.TotalNotes.ToString("N0");
        totalWordsValue.Text = stats.TotalWords.ToString("N0");
        avgWordsValue.Text = stats.AvgWordsPerNote.ToString("F1");
        longestValue.Text = stats.LongestNoteWords.ToString("N0");

        // Streak
        currentStreakValue.Text = stats.CurrentStreak.ToString();
        bestStreakValue.Text = stats.BestStreak.ToString();
        bestStreakLabel.Text = stats.BestStreakStart.HasValue
            ? $"🏆 {stats.BestStreakStart:dd MMM} → {stats.BestStreakEnd:dd MMM}"
            : "Mejor racha 🏆";

        // Calendar
        RenderCalendar(stats);

        // Last 7 days
        RenderWeek(stats);

        // Top words
        RenderTopWords(stats);
    }

    private void RenderCalendar(StatsCalculator.AllStats stats)
    {
        calendarPanel.Children.Clear();

        if (stats.Daily.Count == 0) return;

        bool dark = IsDark;
        var calColors = dark ? DarkCalColors : LightCalColors;
        var textBrush = new SolidColorBrush(dark ? DarkText : LightText);

        var today = DateOnly.FromDateTime(DateTime.Now);
        var firstDate = stats.Daily[0].Date;

        // Calculate calendar boundaries
        var startDay = firstDate; // first day with data
        int daysSince = today.DayNumber - startDay.DayNumber;
        int totalWeeks = (daysSince / 7) + 2; // +1 for current partial week, +1 for padding

        // Build a dictionary for quick lookup
        var dayIndex = new Dictionary<DateOnly, int>(stats.Daily.Count);
        for (int i = 0; i < stats.Daily.Count; i++)
            dayIndex[stats.Daily[i].Date] = i;

        // Find max words for intensity scaling
        int maxWords = stats.Daily.Max(d => d.WordCount);
        maxWords = Math.Max(maxWords, 1);

        // Create week columns, newest at right
        var calStart = today.AddDays(-(totalWeeks * 7) + 1);
        // Adjust so Monday is start
        int dowOffset = ((int)calStart.DayOfWeek + 6) % 7; // Monday=0
        calStart = calStart.AddDays(-dowOffset);

        // Month labels
        var monthLabelsPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        calendarPanel.Children.Add(monthLabelsPanel);

        for (int w = 0; w < totalWeeks; w++)
        {
            var weekStart = calStart.AddDays(w * 7);
            var weekEnd = weekStart.AddDays(6);

            string label = "";
            if (weekStart.Month == weekEnd.Month)
            {
                // Show month name only for first week of each month
                // We'll just show the month abbreviation
            }
            label = weekStart switch
            {
                _ when w == 0 => CultureInfo.CurrentCulture.DateTimeFormat.GetAbbreviatedMonthName(weekStart.Month),
                _ when weekStart.Month != calStart.AddDays((w - 1) * 7).Month
                    => CultureInfo.CurrentCulture.DateTimeFormat.GetAbbreviatedMonthName(weekStart.Month),
                _ => "",
            };

            monthLabelsPanel.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = 10,
                Width = 14,
                Height = 12,
                Opacity = 0.6,
                Foreground = textBrush,
                TextAlignment = TextAlignment.Center,
            });
            // gap between "columns" is already managed by Width
        }

        // Day name labels (Mon, Wed, Fri)
        var gridPanel = new Grid();
        gridPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        for (int w = 0; w < totalWeeks; w++)
            gridPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });

        // Day names column
        var dayNamesPanel = new StackPanel { Margin = new Thickness(0, 0, 4, 0) };
        var dayNames = new[] { "L", "M", "M", "J", "V", "S", "D" };
        for (int d = 0; d < 7; d++)
        {
            var tb = new TextBlock
            {
                Text = d % 2 == 0 ? dayNames[d] : "", // show L, M, J, S
                FontSize = 9,
                Height = 14,
                Opacity = 0.4,
                Foreground = textBrush,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center,
            };
            dayNamesPanel.Children.Add(tb);
        }
        Grid.SetColumn(dayNamesPanel, 0);
        gridPanel.Children.Add(dayNamesPanel);

        // Cells
        for (int w = 0; w < totalWeeks; w++)
        {
            var colPanel = new StackPanel();
            for (int d = 0; d < 7; d++)
            {
                var cellDate = calStart.AddDays(w * 7 + d);
                int intensity = 0;
                bool hasData = dayIndex.TryGetValue(cellDate, out int idx);
                if (hasData && stats.Daily[idx].WordCount > 0)
                {
                    double ratio = (double)stats.Daily[idx].WordCount / maxWords;
                    intensity = ratio switch
                    {
                        > 0.75 => 4,
                        > 0.50 => 3,
                        > 0.25 => 2,
                        _ => 1,
                    };
                }

                var cell = new Border
                {
                    Width = 12,
                    Height = 12,
                    CornerRadius = new CornerRadius(2),
                    Background = new SolidColorBrush(calColors[intensity]),
                    Margin = new Thickness(1),
                    ToolTip = hasData
                        ? $"{cellDate:ddd dd MMM} — {stats.Daily[idx].WordCount} palabras, {stats.Daily[idx].NoteCount} notas"
                        : $"{cellDate:ddd dd MMM} — Sin actividad",
                };

                // Today highlight
                if (cellDate == today)
                {
                    cell.BorderBrush = new SolidColorBrush(dark ? Colors.White : Colors.Black);
                    cell.BorderThickness = new Thickness(1);
                }

                colPanel.Children.Add(cell);
            }
            Grid.SetColumn(colPanel, w + 1);
            gridPanel.Children.Add(colPanel);
        }

        calendarPanel.Children.Add(gridPanel);

        // Legend colors
        ColorFromRect(c0, calColors[0]);
        ColorFromRect(c1, calColors[1]);
        ColorFromRect(c2, calColors[2]);
        ColorFromRect(c3, calColors[3]);
        ColorFromRect(c4, calColors[4]);
    }

    private static void ColorFromRect(Rectangle rect, Color color)
    {
        rect.Fill = new SolidColorBrush(color);
    }

    private void RenderWeek(StatsCalculator.AllStats stats)
    {
        weekPanel.Children.Clear();
        bool dark = IsDark;
        var textColor = dark ? DarkText : LightText;
        var barColor = dark ? Color.FromRgb(0x5B, 0x8E, 0xC9) : Color.FromRgb(0x4A, 0x7D, 0xB5);
        var mutedColor = dark ? DarkMuted : LightMuted;

        var today = DateOnly.FromDateTime(DateTime.Now);
        var dayNames = new[] { "lun", "mar", "mié", "jue", "vie", "sáb", "dom" };

        var dayIndex = stats.Daily.ToDictionary(d => d.Date);
        int maxWords = 1;
        var last7 = new List<StatsCalculator.DailyStats>(7);
        for (int i = 6; i >= 0; i--)
        {
            var d = today.AddDays(-i);
            if (dayIndex.TryGetValue(d, out var ds))
            {
                last7.Add(ds);
                if (ds.WordCount > maxWords) maxWords = ds.WordCount;
            }
            else
            {
                last7.Add(new StatsCalculator.DailyStats(d, 0, 0));
            }
        }

        foreach (var day in last7)
        {
            int dow = ((int)day.Date.DayOfWeek + 6) % 7; // Mon=0
            string label = day.Date == today ? "hoy" : dayNames[dow];

            var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Day name
            var nameTb = new TextBlock
            {
                Text = label,
                FontSize = 11,
                Foreground = new SolidColorBrush(mutedColor),
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(nameTb, 0);
            row.Children.Add(nameTb);

            // Bar
            double fraction = day.WordCount / (double)maxWords;
            double barWidth = Math.Max(20, fraction * 180);
            var bar = new Border
            {
                Width = barWidth,
                Height = 16,
                CornerRadius = new CornerRadius(3),
                Background = new SolidColorBrush(barColor),
                Opacity = 0.3 + fraction * 0.7,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(4, 0, 4, 0),
            };
            Grid.SetColumn(bar, 1);
            row.Children.Add(bar);

            // Count
            var countTb = new TextBlock
            {
                Text = day.WordCount.ToString("N0"),
                FontSize = 11,
                Foreground = new SolidColorBrush(textColor),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(4, 0, 0, 0),
            };
            Grid.SetColumn(countTb, 2);
            row.Children.Add(countTb);

            weekPanel.Children.Add(row);
        }
    }

    private void RenderTopWords(StatsCalculator.AllStats stats)
    {
        topWordsPanel.Children.Clear();
        bool dark = IsDark;
        var textColor = dark ? DarkText : LightText;
        var mutedColor = dark ? DarkMuted : LightMuted;

        if (stats.TopWords.Count == 0)
        {
            topWordsPanel.Children.Add(new TextBlock
            {
                Text = "No hay suficientes palabras aún.",
                FontSize = 12,
                Opacity = 0.5,
                Foreground = new SolidColorBrush(mutedColor),
            });
            return;
        }

        int rank = 1;
        foreach (var wf in stats.TopWords)
        {
            var row = new Grid { Margin = new Thickness(0, 3, 0, 3) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var rankTb = new TextBlock
            {
                Text = $"#{rank}",
                FontSize = 11,
                Foreground = new SolidColorBrush(mutedColor),
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(rankTb, 0);
            row.Children.Add(rankTb);

            var wordTb = new TextBlock
            {
                Text = wf.Word,
                FontSize = 12,
                FontWeight = FontWeights.Medium,
                Foreground = new SolidColorBrush(textColor),
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(wordTb, 1);
            row.Children.Add(wordTb);

            var countTb = new TextBlock
            {
                Text = $"({wf.Count})",
                FontSize = 11,
                Opacity = 0.6,
                Foreground = new SolidColorBrush(mutedColor),
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(countTb, 2);
            row.Children.Add(countTb);

            topWordsPanel.Children.Add(row);
            rank++;
        }
    }

    private void CalendarScroll_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        // Redirect mouse wheel to the outer ScrollViewer so it scrolls vertically
        if (!e.Handled && sender is ScrollViewer sv)
        {
            e.Handled = true;
            var evt = new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
            {
                RoutedEvent = MouseWheelEvent,
                Source = sv
            };
            scrollViewer.RaiseEvent(evt);
        }
    }

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }

    private void CloseBtn_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
