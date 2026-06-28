using System.ComponentModel;

namespace QuickNotes.Models;

public class Notebook : INotifyPropertyChanged
{
    public Guid Id { get; set; } = Guid.NewGuid();

    private string _name = "";
    public string Name
    {
        get => _name;
        set { _name = value; OnPropertyChanged(); }
    }

    private string _color = "#3A3A3A";
    public string Color
    {
        get => _color;
        set { _color = value; OnPropertyChanged(); }
    }

    private string _icon = "📕";
    public string Icon
    {
        get => _icon;
        set { _icon = value; OnPropertyChanged(); }
    }

    public int Count { get; set; }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
