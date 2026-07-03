using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace QuickNotes.Views;

/// <summary>
/// Tiny floating window that shows the grid size indicator during Ctrl+resize.
/// Just a label like "4 × 3" near the cursor.
/// </summary>
public class GridSizeIndicator : Window
{
    private readonly TextBlock _label;

    public GridSizeIndicator()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;
        IsHitTestVisible = false;
        Focusable = false;
        Width = 80;
        Height = 30;

        var border = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xDD, 0x00, 0x00, 0x00)),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8, 3, 8, 3),
        };

        _label = new TextBlock
        {
            Foreground = Brushes.White,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            TextAlignment = TextAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };

        border.Child = _label;
        Content = border;
    }

    public void Update(int unitsW, int unitsH)
    {
        _label.Text = $"{unitsW} × {unitsH}";
    }
}
