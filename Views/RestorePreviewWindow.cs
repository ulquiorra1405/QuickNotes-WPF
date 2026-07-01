using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Shapes;
using QuickNotes.Models;

namespace QuickNotes.Views;

/// <summary>
/// Small preview dialog for backup restore.
/// Shows note count, size, and comparison with current state.
/// </summary>
public class RestorePreviewWindow : Window
{
    private readonly NotesStore.BackupPreview _preview;

    public RestorePreviewWindow(NotesStore.BackupPreview preview)
    {
        _preview = preview;
        Title = "Restaurar copia de seguridad";
        Width = 420;
        Height = 320;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = new SolidColorBrush(Color.FromRgb(0x26, 0x26, 0x26));
        ShowInTaskbar = false;
        Topmost = true;

        BuildUI();
    }

    private void BuildUI()
    {
        var grid = new Grid { Margin = new Thickness(20) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // Title
        var titleBlock = new TextBlock
        {
            Text = "Vista previa del backup",
            FontSize = 17,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(0xDD, 0xDD, 0xDD)),
            Margin = new Thickness(0, 0, 0, 12),
        };
        Grid.SetRow(titleBlock, 0);
        grid.Children.Add(titleBlock);

        // Content panel
        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
        var lblBrush = new SolidColorBrush(Color.FromRgb(0xBB, 0xBB, 0xBB));
        var valBrush = new SolidColorBrush(Color.FromRgb(0xEE, 0xEE, 0xEE));
        var sectionBrush = new SolidColorBrush(Color.FromArgb(0x88, 0xBB, 0xBB, 0xBB));

        // ── Backup info ──
        panel.Children.Add(SectionLabel("Archivo de copia", sectionBrush));
        AddInfoRow(panel, "Nombre:", _preview.FileName, lblBrush, valBrush);
        AddInfoRow(panel, "Fecha:", _preview.BackupDate, lblBrush, valBrush);
        AddInfoRow(panel, "Tamaño:", FormatSize(_preview.FileSizeBytes), lblBrush, valBrush);

        panel.Children.Add(new Rectangle
        {
            Height = 1,
            Fill = new SolidColorBrush(Color.FromArgb(0x20, 0xFF, 0xFF, 0xFF)),
            Margin = new Thickness(0, 8, 0, 4)
        });

        // ── Notes count ──
        panel.Children.Add(SectionLabel("Notas", sectionBrush));

        var backupCountColor = _preview.NoteCount >= _preview.CurrentNoteCount
            ? new SolidColorBrush(Color.FromRgb(0x7F, 0xDD, 0x7F))
            : new SolidColorBrush(Color.FromRgb(0xDD, 0xAA, 0x7F));

        AddInfoRow(panel, "En el backup:", $"{_preview.NoteCount} notas activas", lblBrush, backupCountColor);

        string currentState = _preview.CurrentNoteTotal > _preview.CurrentNoteCount
            ? $"{_preview.CurrentNoteCount} activas (+{_preview.CurrentNoteTotal - _preview.CurrentNoteCount} archivadas/eliminadas)"
            : $"{_preview.CurrentNoteCount} activas";
        AddInfoRow(panel, "Estado actual:", currentState, lblBrush, valBrush);

        // Comparison
        int diff = _preview.NoteCount - _preview.CurrentNoteCount;
        string comparisonText = diff switch
        {
            > 0 => $"El backup tiene {diff} nota(s) mas que el estado actual",
            < 0 => $"El estado actual tiene {-diff} nota(s) mas que el backup",
            0 => "Misma cantidad de notas en ambos",
        };
        var diffColor = diff switch
        {
            > 0 => new SolidColorBrush(Color.FromRgb(0xDD, 0xCC, 0x66)),
            < 0 => new SolidColorBrush(Color.FromRgb(0x99, 0xCC, 0xFF)),
            0 => new SolidColorBrush(Color.FromRgb(0x7F, 0xDD, 0x7F)),
        };
        var comparison = new TextBlock
        {
            Text = comparisonText,
            FontSize = 12,
            Foreground = diffColor,
            Margin = new Thickness(0, 6, 0, 0),
            TextWrapping = TextWrapping.Wrap,
        };
        panel.Children.Add(comparison);

        Grid.SetRow(panel, 1);
        grid.Children.Add(panel);

        // Buttons
        var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
        Grid.SetRow(btnPanel, 2);

        var cancelBtn = new Button { Content = "Cancelar", Width = 90, Height = 30, Cursor = System.Windows.Input.Cursors.Hand, FontSize = 13, Margin = new Thickness(0, 0, 10, 0) };
        cancelBtn.Style = MainWindow.MakeBtnStyle(Color.FromRgb(0xBB, 0xBB, 0xBB), Color.FromRgb(0x3A, 0x3A, 0x3A), Color.FromRgb(0xFF, 0xFF, 0xFF), Color.FromRgb(0x55, 0x55, 0x55));
        cancelBtn.Click += (_, _) => Close();
        btnPanel.Children.Add(cancelBtn);

        var restoreBtn = new Button { Content = "Restaurar", Width = 90, Height = 30, Cursor = System.Windows.Input.Cursors.Hand, FontSize = 13 };
        restoreBtn.Style = MainWindow.MakeBtnStyle(Color.FromRgb(0xFF, 0xFF, 0xFF), Color.FromRgb(0x5A, 0x5A, 0x5A), Color.FromRgb(0xFF, 0xFF, 0xFF), Color.FromRgb(0x77, 0x77, 0x77));
        restoreBtn.Click += OnRestore_Click;
        btnPanel.Children.Add(restoreBtn);

        grid.Children.Add(btnPanel);
        Content = grid;
    }

    private void OnRestore_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            "Se restaurara el backup seleccionado.\n\n" +
            "La aplicacion se cerrara y se volvera a abrir automaticamente.\n\n" +
            "Estas seguro?",
            "Confirmar restauracion",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes)
            return;

        if (NotesStore.RestoreFromBackup(_preview.FilePath))
        {
            MessageBox.Show(
                "Backup restaurado correctamente.\nLa aplicacion se reiniciara.",
                "Restauracion exitosa",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            // Restart the app
            var appPath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
            Close();
            Application.Current.Shutdown();

            if (appPath != null)
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = appPath,
                    UseShellExecute = true,
                });
            }
        }
        else
        {
            MessageBox.Show("Error al restaurar el backup.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static TextBlock SectionLabel(string text, Brush brush)
    {
        return new TextBlock
        {
            Text = text,
            FontSize = 11,
            Foreground = brush,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 6, 0, 1),
        };
    }

    private static void AddInfoRow(StackPanel panel, string label, string value, Brush lblBrush, Brush valBrush)
    {
        var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var lbl = new TextBlock { Text = label, FontSize = 13, Foreground = lblBrush, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
        Grid.SetColumn(lbl, 0);
        var val = new TextBlock { Text = value, FontSize = 13, Foreground = valBrush, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        Grid.SetColumn(val, 1);
        row.Children.Add(lbl);
        row.Children.Add(val);
        panel.Children.Add(row);
    }

    private static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
        _ => $"{bytes / (1024.0 * 1024.0):F1} MB",
    };
}
