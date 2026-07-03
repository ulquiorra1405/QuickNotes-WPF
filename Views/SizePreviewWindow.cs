using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace QuickNotes.Views;

/// <summary>
/// Overlay window that shows the snapped-to-grid preview during Ctrl+resize.
/// Semi-transparent copy of the note with grid lines and size label.
/// </summary>
public class SizePreviewWindow : Window
{
    private readonly Border _overlayBorder;
    private readonly Canvas _gridCanvas;
    private readonly TextBlock _gridLabel;
    private readonly TextBlock _dimensionLabel;

    public SizePreviewWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;
        IsHitTestVisible = false;
        Focusable = false;
        Width = 1;
        Height = 1;

        var root = new Grid();

        // Main overlay rectangle — mimics the note's look
        _overlayBorder = new Border
        {
            CornerRadius = new CornerRadius(12),
            Opacity = 0.45,
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x80, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(2),
            Margin = new Thickness(2),
        };

        _gridCanvas = new Canvas { IsHitTestVisible = false };
        _overlayBorder.Child = _gridCanvas;
        root.Children.Add(_overlayBorder);

        // Size label — "5 × 4" + "360 × 288" at the top center
        var labelBg = new Border
        {
            VerticalAlignment = VerticalAlignment.Top,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 10, 0, 0),
            Background = new SolidColorBrush(Color.FromArgb(0xAA, 0x00, 0x00, 0x00)),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 5, 12, 5),
        };

        var labelStack = new StackPanel { Orientation = Orientation.Horizontal };
        _gridLabel = new TextBlock
        {
            Text = "4 × 4",
            Foreground = Brushes.White,
            FontSize = 18,
            FontWeight = FontWeights.Bold,
        };
        _dimensionLabel = new TextBlock
        {
            Text = "  288 × 288",
            Foreground = new SolidColorBrush(Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF)),
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(6, 0, 0, 1),
        };
        labelStack.Children.Add(_gridLabel);
        labelStack.Children.Add(_dimensionLabel);
        labelBg.Child = labelStack;
        root.Children.Add(labelBg);

        Content = root;
    }

    /// <summary>
    /// Updates the preview window position, size, grid lines, and labels.
    /// </summary>
    public void Update(double left, double top, double width, double height, string? noteColor)
    {
        // Compensate for the 2px margin on the border
        Left = left - 2;
        Top = top - 2;
        Width = width + 4;
        Height = height + 4;

        int unitsW = Math.Max(3, (int)Math.Round(width / 72.0));
        int unitsH = Math.Max(2, (int)Math.Round(height / 72.0));
        _gridLabel.Text = $"{unitsW} × {unitsH}";
        _dimensionLabel.Text = $"  {(int)width} × {(int)height}";

        if (!string.IsNullOrEmpty(noteColor))
        {
            try
            {
                var color = (Color)ColorConverter.ConvertFromString(noteColor);
                _overlayBorder.Background = new SolidColorBrush(color);
                bool dark = (0.299 * color.R + 0.587 * color.G + 0.114 * color.B) < 140;
                _overlayBorder.BorderBrush = new SolidColorBrush(
                    dark
                        ? Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF)
                        : Color.FromArgb(0x60, 0x00, 0x00, 0x00));
            }
            catch { }
        }

        DrawGridLines(width, height, noteColor);
    }

    private void DrawGridLines(double width, double height, string? noteColor)
    {
        _gridCanvas.Children.Clear();

        bool dark = true;
        if (!string.IsNullOrEmpty(noteColor))
        {
            try
            {
                var color = (Color)ColorConverter.ConvertFromString(noteColor);
                dark = (0.299 * color.R + 0.587 * color.G + 0.114 * color.B) < 140;
            }
            catch { }
        }

        var lineColor = dark
            ? Color.FromArgb(0x28, 0xFF, 0xFF, 0xFF)
            : Color.FromArgb(0x28, 0x00, 0x00, 0x00);

        // Vertical lines
        for (double x = 72; x < width; x += 72)
        {
            var line = new Line
            {
                X1 = x, Y1 = 0, X2 = x, Y2 = height,
                Stroke = new SolidColorBrush(lineColor),
                StrokeThickness = 0.5,
            };
            _gridCanvas.Children.Add(line);
        }

        // Horizontal lines
        for (double y = 72; y < height; y += 72)
        {
            var line = new Line
            {
                X1 = 0, Y1 = y, X2 = width, Y2 = y,
                Stroke = new SolidColorBrush(lineColor),
                StrokeThickness = 0.5,
            };
            _gridCanvas.Children.Add(line);
        }
    }
}
