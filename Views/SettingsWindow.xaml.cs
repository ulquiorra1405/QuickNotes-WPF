using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using QuickNotes.Models;

namespace QuickNotes.Views;

public partial class SettingsWindow : Window
{
    private readonly NotesStore _store;
    private readonly MainWindow _mainWindow;
    private readonly string _currentTheme;

    // Local working copies of settings (modified before save)
    private string _selectedTheme;
    private bool _startupVal;
    private int _autoSaveVal;
    private bool _backupVal;
    private string _backupPathVal;
    private bool _confirmVal;
    private string _defaultColorVal;
    private int _fontSizeVal;
    private bool _compactVal;
    private string _fontVal;
    private bool _animVal;

    public SettingsWindow(NotesStore store, MainWindow mainWindow, string currentTheme = "dark")
    {
        InitializeComponent();

        _store = store;
        _mainWindow = mainWindow;
        _currentTheme = currentTheme;
        Owner = mainWindow;

        // Snapshot current settings
        _selectedTheme = store.Theme;
        _startupVal = store.StartWithWindows;
        _autoSaveVal = store.AutoSaveInterval;
        _backupVal = store.BackupEnabled;
        _backupPathVal = store.BackupPath ?? "";
        _confirmVal = store.ConfirmOnExit;
        _defaultColorVal = string.IsNullOrEmpty(store.DefaultColor) ? Note.RandomColor() : store.DefaultColor;
        _fontSizeVal = store.NoteFontSize;
        _compactVal = store.CompactMode;
        _fontVal = store.NoteFontFamily;
        _animVal = store.AnimationsEnabled;

        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var isLight = _currentTheme == "light";
        Background = new SolidColorBrush(isLight ? Color.FromRgb(0xF0, 0xF0, 0xF0) : Color.FromRgb(0x26, 0x26, 0x26));
        BuildUI();
    }

    private void BuildUI()
    {
        var isLight = _currentTheme == "light";

        var winBg = isLight ? Color.FromRgb(0xF0, 0xF0, 0xF0) : Color.FromRgb(0x26, 0x26, 0x26);
        var textBright = isLight ? Color.FromRgb(0x1A, 0x1A, 0x1A) : Color.FromRgb(0xDD, 0xDD, 0xDD);
        var textMuted = isLight ? Color.FromRgb(0x55, 0x55, 0x55) : Color.FromRgb(0xBB, 0xBB, 0xBB);
        var textDim = isLight ? Color.FromArgb(0x88, 0x00, 0x00, 0x00) : Color.FromArgb(0x88, 0xBB, 0xBB, 0xBB);
        var sepColor = isLight ? Color.FromArgb(0x20, 0x00, 0x00, 0x00) : Color.FromArgb(0x20, 0xFF, 0xFF, 0xFF);
        var inputBg = isLight ? Color.FromRgb(0xE0, 0xE0, 0xE0) : Color.FromRgb(0x3A, 0x3A, 0x3A);
        var inputBorder = isLight ? Color.FromArgb(0x40, 0x00, 0x00, 0x00) : Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF);
        var inputFg = isLight ? Color.FromRgb(0x1A, 0x1A, 0x1A) : Color.FromRgb(0xDD, 0xDD, 0xDD);

        // Button color schemes
        var btnDefaultFg = isLight ? Color.FromRgb(0x44, 0x44, 0x44) : Color.FromRgb(0xBB, 0xBB, 0xBB);
        var btnDefaultBg = isLight ? Color.FromRgb(0xD0, 0xD0, 0xD0) : Color.FromRgb(0x3A, 0x3A, 0x3A);
        var btnDefaultHoverBg = isLight ? Color.FromRgb(0xBE, 0xBE, 0xBE) : Color.FromRgb(0x55, 0x55, 0x55);
        var btnActiveFg = Color.FromRgb(0xFF, 0xFF, 0xFF);
        var btnActiveBg = isLight ? Color.FromRgb(0x55, 0x55, 0x55) : Color.FromRgb(0x5A, 0x5A, 0x5A);
        var btnActiveHoverBg = isLight ? Color.FromRgb(0x44, 0x44, 0x44) : Color.FromRgb(0x77, 0x77, 0x77);

        var outerGrid = new Grid { Margin = new Thickness(24), Background = new SolidColorBrush(winBg) };
        outerGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        outerGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        outerGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var titleBlock = new TextBlock
        {
            Text = "Ajustes",
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(textBright),
            Margin = new Thickness(0, 0, 0, 12),
        };
        Grid.SetRow(titleBlock, 0);
        outerGrid.Children.Add(titleBlock);

        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetRow(scroll, 1);

        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };

        var lblBrush = new SolidColorBrush(textMuted);
        Label MakeLabel(string text) => new Label { Content = text, FontSize = 12, Foreground = lblBrush, Padding = new Thickness(0, 8, 0, 2) };

        // ── Theme ──
        panel.Children.Add(MakeLabel("Tema"));
        var themePanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        string[] themeOptions = ["Oscuro", "Claro", "Sistema"];
        string[] themeValues = ["dark", "light", "system"];
        var themeBtns = new Button[3];
        int currentTheme = Array.IndexOf(themeValues, _selectedTheme);
        if (currentTheme < 0) currentTheme = 0;
        for (int i = 0; i < 3; i++)
        {
            int idx = i;
            var btn = new Button { Content = themeOptions[i], Width = 90, Height = 30, Cursor = Cursors.Hand, FontSize = 13, Margin = new Thickness(0, 0, 6, 0) };
            btn.Click += (_, _) =>
            {
                _selectedTheme = themeValues[idx];
                for (int j = 0; j < themeBtns.Length; j++)
                    UpdateThemeBtn(themeBtns[j], j == idx);
            };
            UpdateThemeBtn(btn, i == currentTheme);
            themeBtns[i] = btn;
            themePanel.Children.Add(btn);
        }
        panel.Children.Add(themePanel);

        panel.Children.Add(new Rectangle { Height = 1, Fill = new SolidColorBrush(sepColor), Margin = new Thickness(0, 8, 0, 4) });

        // ── Autosave ──
        panel.Children.Add(MakeLabel("Autoguardado"));
        int[] autoValues = [5, 10, 30, 60];
        int currentAuto = Array.IndexOf(autoValues, _autoSaveVal);
        if (currentAuto < 0) currentAuto = 1;
        var autoPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        var autoBtns = new Button[4];
        for (int i = 0; i < 4; i++)
        {
            int idx = i;
            var btn = new Button { Content = $"{autoValues[i]}s", Width = 60, Height = 30, Cursor = Cursors.Hand, FontSize = 12, Margin = new Thickness(0, 0, 6, 0) };
            btn.Click += (_, _) =>
            {
                _autoSaveVal = autoValues[idx];
                for (int j = 0; j < autoBtns.Length; j++)
                    UpdateThemeBtn(autoBtns[j], j == idx);
            };
            UpdateThemeBtn(btn, i == currentAuto);
            autoBtns[i] = btn;
            autoPanel.Children.Add(btn);
        }
        panel.Children.Add(autoPanel);

        // ── Backup ──
        panel.Children.Add(MakeLabel("Copia de seguridad"));
        var backupCheck = new CheckBox
        {
            Content = "Realizar copia de seguridad diaria",
            FontSize = 13,
            Foreground = lblBrush,
            IsChecked = _backupVal,
            Margin = new Thickness(0, 0, 0, 4),
        };
        panel.Children.Add(backupCheck);

        // Backup path
        var pathGrid = new Grid { Margin = new Thickness(0, 0, 0, 4) };
        pathGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        pathGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var pathBox = new TextBox
        {
            Text = _backupPathVal,
            FontSize = 12,
            Height = 28,
            VerticalContentAlignment = VerticalAlignment.Center,
            Foreground = new SolidColorBrush(inputFg),
            Background = new SolidColorBrush(inputBg),
            BorderBrush = new SolidColorBrush(inputBorder),
            BorderThickness = new Thickness(1),
            CaretBrush = new SolidColorBrush(Colors.White),
            Margin = new Thickness(0, 0, 6, 0),
            TextWrapping = TextWrapping.NoWrap,
        };
        Grid.SetColumn(pathBox, 0);
        // Sync back on text change
        pathBox.TextChanged += (_, _) => _backupPathVal = pathBox.Text;
        var browseBtn = new Button
        {
            Content = "Examinar…",
            Width = 80,
            Height = 28,
            Cursor = Cursors.Hand,
            FontSize = 12,
        };
        Grid.SetColumn(browseBtn, 1);
        browseBtn.Style = MainWindow.MakeBtnStyle(btnDefaultFg, btnDefaultBg, btnActiveFg, btnDefaultHoverBg);
        browseBtn.Click += (_, _) =>
        {
            var initialPath = string.IsNullOrWhiteSpace(_backupPathVal)
                ? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
                : _backupPathVal;
            var result = Helpers.FolderPicker.Show(initialPath, "Seleccionar carpeta para copias de seguridad");
            if (result != null)
            {
                _backupPathVal = result;
                pathBox.Text = result;
            }
        };
        pathGrid.Children.Add(pathBox);
        pathGrid.Children.Add(browseBtn);
        panel.Children.Add(pathGrid);

        // Restore button
        var restoreRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        var restoreBtn = new Button
        {
            Content = "Restaurar desde backup...",
            Height = 28,
            Cursor = Cursors.Hand,
            FontSize = 12,
        };
        restoreBtn.Style = MainWindow.MakeBtnStyle(btnDefaultFg, btnDefaultBg, btnActiveFg, btnDefaultHoverBg);
        restoreBtn.Click += (_, _) =>
        {
            // Open backup file picker, defaulting to backup folder
            var defaultDir = string.IsNullOrWhiteSpace(_backupPathVal)
                ? System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    "QuickNotes", "backups")
                : _backupPathVal;
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Seleccionar archivo de backup",
                Filter = "Backup files (*.db)|*.db|All files (*.*)|*.*",
                InitialDirectory = defaultDir,
                DefaultExt = ".db",
            };
            if (dlg.ShowDialog() == true)
            {
                var preview = NotesStore.GetBackupPreview(
                    dlg.FileName,
                    _store.Notes.Count(n => !n.IsDeleted),
                    _store.Notes.Count
                );
                if (preview == null)
                {
                    MessageBox.Show("No se pudo leer el archivo de backup.", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
                var win = new RestorePreviewWindow(preview)
                {
                    Owner = this,
                };
                win.ShowDialog();
            }
        };
        restoreRow.Children.Add(restoreBtn);
        panel.Children.Add(restoreRow);
        panel.Children.Add(new TextBlock
        {
            Text = "Dejar vacío para usar la carpeta predeterminada (Documentos/QuickNotes/backups)",
            FontSize = 10,
            Foreground = new SolidColorBrush(textDim),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 4),
        });

        // ── Confirm exit ──
        panel.Children.Add(MakeLabel("Salida"));
        var confirmCheck = new CheckBox
        {
            Content = "Confirmar al salir",
            FontSize = 13,
            Foreground = lblBrush,
            IsChecked = _confirmVal,
            Margin = new Thickness(0, 0, 0, 4),
        };
        panel.Children.Add(confirmCheck);

        panel.Children.Add(new Rectangle { Height = 1, Fill = new SolidColorBrush(sepColor), Margin = new Thickness(0, 8, 0, 4) });

        // ── Default color ──
        var defColorHeader = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 2) };
        defColorHeader.Children.Add(new TextBlock { Text = "Color por defecto", FontSize = 12, Foreground = lblBrush, VerticalAlignment = VerticalAlignment.Center });
        var randomBtn = new Button
        {
            Content = "🎲",
            Width = 26,
            Height = 22,
            FontSize = 12,
            Cursor = Cursors.Hand,
            Margin = new Thickness(8, 0, 0, 0),
            ToolTip = "Color aleatorio al crear nota",
        };
        var isRandomDef = string.IsNullOrEmpty(_defaultColorVal);
        UpdateThemeBtn(randomBtn, isRandomDef);
        defColorHeader.Children.Add(randomBtn);
        panel.Children.Add(defColorHeader);
        var colorPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
        var settingColorDots = new Border[16];
        var lightRow = new StackPanel { Orientation = Orientation.Horizontal };
        var darkRow = new StackPanel { Orientation = Orientation.Horizontal };
        TextBlock defaultColorLabel = null!;
        for (int ci = 0; ci < Note.Palette.Length; ci++)
        {
            int cIdx = ci;
            var dot = new Border
            {
                Width = 18,
                Height = 18,
                CornerRadius = new CornerRadius(9),
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(Note.Palette[ci])!),
                Tag = Note.Palette[ci],
                Cursor = Cursors.Hand,
                Margin = new Thickness(2),
                BorderThickness = new Thickness(2),
                BorderBrush = Note.Palette[ci] == _defaultColorVal
                    ? new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF))
                    : new SolidColorBrush(Color.FromArgb(0x40, 0x00, 0x00, 0x00)),
            };
            dot.MouseDown += (_, _) =>
            {
                _defaultColorVal = Note.Palette[cIdx];
                defaultColorLabel.Text = _defaultColorVal;
                UpdateThemeBtn(randomBtn, false);
                for (int dj = 0; dj < settingColorDots.Length; dj++)
                {
                    string c = Note.Palette[dj];
                    var isSel = c == _defaultColorVal;
                    settingColorDots[dj].BorderBrush = new SolidColorBrush(isSel ? Colors.White : Color.FromArgb(0x40, 0x00, 0x00, 0x00));
                }
            };
            settingColorDots[ci] = dot;
            if (ci < 8) lightRow.Children.Add(dot);
            else darkRow.Children.Add(dot);
        }
        colorPanel.Children.Add(lightRow);
        colorPanel.Children.Add(darkRow);
        panel.Children.Add(colorPanel);
        defaultColorLabel = new TextBlock
        {
            Text = string.IsNullOrEmpty(_store.DefaultColor) ? "Aleatorio" : _defaultColorVal,
            FontSize = 10,
            Foreground = new SolidColorBrush(textDim),
            Margin = new Thickness(2, 0, 0, 4),
        };
        panel.Children.Add(defaultColorLabel);
        randomBtn.Click += (_, _) =>
        {
            _defaultColorVal = "";
            defaultColorLabel.Text = "Aleatorio";
            for (int dj = 0; dj < settingColorDots.Length; dj++)
                settingColorDots[dj].BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 0x00, 0x00, 0x00));
            UpdateThemeBtn(randomBtn, true);
        };

        // ── Font size ──
        panel.Children.Add(MakeLabel("Tamaño de fuente"));
        var fontRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 4) };
        var fontSizeLabel = new TextBlock { Text = _fontSizeVal.ToString(), FontSize = 16, Foreground = new SolidColorBrush(textBright), Width = 40, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var decBtn = new Button { Content = "−", Width = 30, Height = 30, Cursor = Cursors.Hand, FontSize = 16 };
        decBtn.Style = MainWindow.MakeBtnStyle(btnDefaultFg, btnDefaultBg, btnActiveFg, btnDefaultHoverBg);
        decBtn.Click += (_, _) => { _fontSizeVal = Math.Max(8, _fontSizeVal - 1); fontSizeLabel.Text = _fontSizeVal.ToString(); };
        var incBtn = new Button { Content = "+", Width = 30, Height = 30, Cursor = Cursors.Hand, FontSize = 16 };
        incBtn.Style = MainWindow.MakeBtnStyle(btnDefaultFg, btnDefaultBg, btnActiveFg, btnDefaultHoverBg);
        incBtn.Click += (_, _) => { _fontSizeVal = Math.Min(48, _fontSizeVal + 1); fontSizeLabel.Text = _fontSizeVal.ToString(); };
        fontRow.Children.Add(decBtn);
        fontRow.Children.Add(fontSizeLabel);
        fontRow.Children.Add(incBtn);
        panel.Children.Add(fontRow);

        panel.Children.Add(new Rectangle { Height = 1, Fill = new SolidColorBrush(sepColor), Margin = new Thickness(0, 8, 0, 4) });

        // ── Compact mode ──
        panel.Children.Add(MakeLabel("Tarjetas"));
        var compactCheck = new CheckBox
        {
            Content = "Modo compacto",
            FontSize = 13,
            Foreground = lblBrush,
            IsChecked = _compactVal,
            Margin = new Thickness(0, 0, 0, 4),
        };
        panel.Children.Add(compactCheck);

        // ── Font family ──
        panel.Children.Add(MakeLabel("Fuente del contenido"));
        string[] fontOptions = ["Calibri", "Segoe UI", "Consolas", "Georgia", "Verdana", "Arial", "Times New Roman"];
        int currentFont = Array.IndexOf(fontOptions, _store.NoteFontFamily);
        if (currentFont < 0) currentFont = 0;
        var fontCombo = new ComboBox
        {
            ItemsSource = fontOptions,
            SelectedIndex = currentFont,
            Width = 200,
            Height = 30,
            FontSize = 13,
            Cursor = Cursors.Hand,
            Margin = new Thickness(0, 0, 0, 4),
            HorizontalAlignment = HorizontalAlignment.Left,
            Style = MainWindow.MakeComboStyle(),
            Foreground = new SolidColorBrush(inputFg),
            Background = new SolidColorBrush(inputBg),
            BorderBrush = new SolidColorBrush(inputBorder),
        };
        fontCombo.SelectionChanged += (_, args) =>
        {
            if (args.AddedItems.Count > 0)
                _fontVal = (string)args.AddedItems[0]!;
        };
        fontCombo.PreviewMouseDown += (_, ev) =>
        {
            fontCombo.IsDropDownOpen = !fontCombo.IsDropDownOpen;
            ev.Handled = true;
        };
        panel.Children.Add(fontCombo);

        // ── Animations ──
        panel.Children.Add(MakeLabel("Animaciones"));
        var animCheck = new CheckBox
        {
            Content = "Habilitar animaciones",
            FontSize = 13,
            Foreground = lblBrush,
            IsChecked = _animVal,
            Margin = new Thickness(0, 0, 0, 4),
        };
        panel.Children.Add(animCheck);

        panel.Children.Add(new Rectangle { Height = 1, Fill = new SolidColorBrush(sepColor), Margin = new Thickness(0, 8, 0, 4) });

        // ── Start with Windows ──
        panel.Children.Add(MakeLabel("Inicio"));
        var startupCheck = new CheckBox
        {
            Content = "Iniciar con Windows",
            FontSize = 13,
            Foreground = lblBrush,
            IsChecked = _startupVal,
        };
        panel.Children.Add(startupCheck);

        scroll.Content = panel;
        outerGrid.Children.Add(scroll);

        // Buttons
        var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
        Grid.SetRow(btnPanel, 2);

        var cancelBtn = new Button { Content = "Cancelar", Width = 90, Height = 30, Cursor = Cursors.Hand, FontSize = 13, Margin = new Thickness(0, 0, 10, 0) };
        cancelBtn.Style = MainWindow.MakeBtnStyle(Color.FromRgb(0xBB, 0xBB, 0xBB), Color.FromRgb(0x3A, 0x3A, 0x3A), Color.FromRgb(0xFF, 0xFF, 0xFF), Color.FromRgb(0x55, 0x55, 0x55));
        cancelBtn.Click += (_, _) => Close();
        btnPanel.Children.Add(cancelBtn);

        var saveBtn = new Button { Content = "Guardar", Width = 90, Height = 30, Cursor = Cursors.Hand, FontSize = 13 };
        saveBtn.Style = MainWindow.MakeBtnStyle(Color.FromRgb(0xFF, 0xFF, 0xFF), btnActiveBg, Color.FromRgb(0xFF, 0xFF, 0xFF), btnActiveHoverBg);
        saveBtn.Click += (_, _) =>
        {
            _store.Theme = _selectedTheme;
            _store.StartWithWindows = startupCheck.IsChecked == true;
            _store.AutoSaveInterval = _autoSaveVal;
            _store.BackupEnabled = backupCheck.IsChecked == true;
            _store.BackupPath = _backupPathVal;
            _store.ConfirmOnExit = confirmCheck.IsChecked == true;
            _store.DefaultColor = _defaultColorVal;
            _store.NoteFontSize = _fontSizeVal;
            _store.CompactMode = compactCheck.IsChecked == true;
            _store.NoteFontFamily = _fontVal;
            _store.AnimationsEnabled = animCheck.IsChecked == true;
            _store.SaveSettings();

            _mainWindow.SetAutoSaveInterval(_autoSaveVal);
            _mainWindow.ApplyTheme(_selectedTheme);
            _mainWindow.ApplyCompactMode(_store.CompactMode);
            _mainWindow.SetStartWithWindows(_store.StartWithWindows);
            foreach (Window w in Application.Current.Windows)
                if (w is NoteWindow nw)
                    nw.SetFontFamily(_fontVal);
            Close();
            _mainWindow.ShowStatus("Ajustes guardados");
        };
        btnPanel.Children.Add(saveBtn);

        outerGrid.Children.Add(btnPanel);
        Content = outerGrid;
    }

    private void UpdateThemeBtn(Button btn, bool active)
    {
        var isLight = _currentTheme == "light";
        if (active)
        {
            var bg = isLight ? Color.FromRgb(0x55, 0x55, 0x55) : Color.FromRgb(0x5A, 0x5A, 0x5A);
            var hoverBg = isLight ? Color.FromRgb(0x44, 0x44, 0x44) : Color.FromRgb(0x77, 0x77, 0x77);
            btn.Style = MainWindow.MakeBtnStyle(Color.FromRgb(0xFF, 0xFF, 0xFF), bg,
                Color.FromRgb(0xFF, 0xFF, 0xFF), hoverBg);
        }
        else
        {
            var fg = isLight ? Color.FromRgb(0x44, 0x44, 0x44) : Color.FromRgb(0xBB, 0xBB, 0xBB);
            var bg = isLight ? Color.FromRgb(0xD0, 0xD0, 0xD0) : Color.FromRgb(0x3A, 0x3A, 0x3A);
            var hoverBg = isLight ? Color.FromRgb(0xBE, 0xBE, 0xBE) : Color.FromRgb(0x55, 0x55, 0x55);
            btn.Style = MainWindow.MakeBtnStyle(fg, bg,
                Color.FromRgb(0xFF, 0xFF, 0xFF), hoverBg);
        }
    }
}
