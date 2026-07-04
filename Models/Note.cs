using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Markup;

namespace QuickNotes.Models;

public class Note : INotifyPropertyChanged
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int Order { get; set; }
    private bool _isPinned;
    public bool IsPinned
    {
        get => _isPinned;
        set { _isPinned = value; OnPropertyChanged(); }
    }
    private bool _isMimetized;
    public bool IsMimetized
    {
        get => _isMimetized;
        set { _isMimetized = value; OnPropertyChanged(); }
    }
    private bool _isArchived;
    public bool IsArchived
    {
        get => _isArchived;
        set { _isArchived = value; OnPropertyChanged(); }
    }
    private bool _isDeleted;
    public bool IsDeleted
    {
        get => _isDeleted;
        set { _isDeleted = value; OnPropertyChanged(); }
    }
    public DateTime? DeletedAt { get; set; }
    public Guid? NotebookId { get; set; }
    public List<Guid> TagIds { get; set; } = new();
    [System.Text.Json.Serialization.JsonIgnore]
    public string TagsDisplay
    {
        get
        {
            if (TagIds.Count == 0) return "";
            return string.Join(", ", TagIds);
        }
    }
    public double WinLeft { get; set; } = double.NaN;
    public double WinTop { get; set; } = double.NaN;
    public double WinWidth { get; set; } = double.NaN;
    public double WinHeight { get; set; } = double.NaN;

    private string _title = "";
    public string Title
    {
        get => _title;
        set { _title = value; OnPropertyChanged(); }
    }

    private string _text = "";
    public string Text
    {
        get => _text;
        set { _text = value; OnPropertyChanged(); OnPropertyChanged(nameof(PlainText)); }
    }

    private string _color = "#F8F9FA";
    public string Color
    {
        get => _color;
        set { _color = value; OnPropertyChanged(); OnPropertyChanged(nameof(TextForeground)); }
    }

    private bool _isDirty;
    private bool _isSearchMatch = true;
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsSearchMatch
    {
        get => _isSearchMatch;
        set { _isSearchMatch = value; OnPropertyChanged(); }
    }

    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsDirty
    {
        get => _isDirty;
        set { _isDirty = value; OnPropertyChanged(); }
    }

    private DateTime _lastModified = DateTime.Now;
    public DateTime LastModified
    {
        get => _lastModified;
        set
        {
            if (_lastModified != value)
            {
                _lastModified = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(LastModifiedDisplay));
                OnPropertyChanged(nameof(DateGroup));
            }
        }
    }

    [System.Text.Json.Serialization.JsonIgnore]
    public DateTime DateGroup => _lastModified.Date;

    [System.Text.Json.Serialization.JsonIgnore]
    public string LastModifiedDisplay
    {
        get
        {
            if (LastModified.Date == DateTime.Today)
                return LastModified.ToString("t");
            return LastModified.ToString("d");
        }
    }

    private string _icon = "📝";
    public string Icon
    {
        get => _icon;
        set { _icon = value; OnPropertyChanged(); }
    }

    private int _attachmentCount;
    [System.Text.Json.Serialization.JsonIgnore]
    public int AttachmentCount
    {
        get => _attachmentCount;
        set
        {
            _attachmentCount = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasAttachments));
        }
    }

    [System.Text.Json.Serialization.JsonIgnore]
    public bool HasAttachments => _attachmentCount > 0;

    [System.Text.Json.Serialization.JsonIgnore]
    public string PlainText
    {
        get
        {
            if (string.IsNullOrEmpty(Text)) return "";
            var t = Text;
            if (!t.StartsWith("<")) return t;
            try
            {
                var doc = (System.Windows.Documents.FlowDocument)System.Windows.Markup.XamlReader.Parse(t);
                var range = new System.Windows.Documents.TextRange(
                    doc.ContentStart, doc.ContentEnd);
                return range.Text.TrimEnd('\r', '\n');
            }
            catch (Exception ex) { ErrorLog.Write(ex, "PlainText"); return t; }
        }
    }

    public static readonly string[] Palette =
    [
        "#F8F9FA", "#FDDED7", "#FDE8C8", "#FEF3C4",
        "#D4EDDA", "#C8F0EA", "#D0E4F5", "#E2D5F6",
        "#3A3A3A", "#4A3030", "#304A30", "#4A4A30",
        "#304A4A", "#4A304A", "#30304A", "#4A3A30"
    ];

    [System.Text.Json.Serialization.JsonIgnore]
    public string TextForeground
    {
        get
        {
            if (string.IsNullOrEmpty(Color) || Color.Length < 7) return "#3A3A3A";
            var r = Convert.ToInt32(Color.Substring(1, 2), 16);
            var g = Convert.ToInt32(Color.Substring(3, 2), 16);
            var b = Convert.ToInt32(Color.Substring(5, 2), 16);
            return (0.299 * r + 0.587 * g + 0.114 * b) < 140 ? "#FFFFFF" : "#3A3A3A";
        }
    }

    private static readonly Random _rng = new();

    public static string RandomColor() => Palette[_rng.Next(Palette.Length)];

    public static readonly string[] EmojiPalette =
    [
        // Page 1: Nature & Weather
        "☀️", "🌙", "⭐", "🌟", "🌈", "☁️",
        "⛅", "🌧️", "⚡", "🔥", "💧", "🌊",
        "🌍", "🌎", "🌏", "🌋", "🏔️", "🏖️",
        "🌺", "🌸", "🌻", "🌿", "🍀", "🌵",

        // Page 2: Food & Drink
        "🍎", "🍊", "🍋", "🍇", "🍓", "🍒",
        "🥑", "🥦", "🥕", "🌽", "🍞", "🧀",
        "🍕", "🍔", "🌮", "🥗", "🍰", "🍪",
        "☕", "🍵", "🥤", "🍺", "🍷", "🧊",

        // Page 3: Activities & Travel
        "🎵", "🎶", "🎤", "🎧", "🎸", "🎹",
        "🎮", "🕹️", "🎲", "♟️", "🎯", "🎳",
        "⚽", "🏀", "🏈", "⚾", "🎾", "🏐",
        "🚗", "✈️", "🚀", "🛸", "🚲", "⛵",

        // Page 4: Symbols & Misc
        "❤️", "🧡", "💛", "💚", "💙", "💜",
        "✅", "❌", "❓", "❗", "💯", "🔝",
        "🔁", "🔂", "▶️", "⏩", "⏪", "🔄",
        "♻️", "⚛️", "🛡️", "💎", "🧲", "🎁",

        // Page 5: Office & Notes
        "📝", "📄", "📃", "📑", "📋", "📌",
        "📍", "📎", "🖇️", "✂️", "📏", "📐",
        "📕", "📗", "📘", "📙", "📚", "📖",
        "🔖", "🏷️", "📁", "📂", "🗂️", "📇",

        // Page 6: Tech & Tools
        "💻", "🖥️", "⌨️", "🖱️", "💽", "💾",
        "💿", "📀", "📱", "📲", "🔋", "🔌",
        "🔧", "🔨", "🪛", "🔩", "⚙️", "🗜️",
        "💡", "🔦", "📡", "🔬", "🔭", "🧪",
    ];

    public static string RandomIcon() => EmojiPalette[_rng.Next(EmojiPalette.Length)];

    public Note Clone() => new()
    {
        Id = Id,
        Title = Title,
        Text = Text,
        Color = Color,
        Icon = Icon,
        IsPinned = IsPinned,
        IsMimetized = IsMimetized,
        IsArchived = IsArchived,
        IsDeleted = IsDeleted,
        DeletedAt = DeletedAt,
        NotebookId = NotebookId,
        TagIds = new List<Guid>(TagIds),
        Order = Order,
        LastModified = LastModified,
        WinLeft = WinLeft,
        WinTop = WinTop,
        WinWidth = WinWidth,
        WinHeight = WinHeight,
    };

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
