using System.ComponentModel;

namespace QuickNotes.Models;

public class Tag : INotifyPropertyChanged
{
    public Guid Id { get; set; } = Guid.NewGuid();

    private string _name = "";
    public string Name
    {
        get => _name;
        set { _name = value; OnPropertyChanged(); }
    }

    public int Count { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsExpanded { get; set; } = true;

    /// <summary>
    /// Persistable archive key for sidebar state.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsInSidebar { get; set; }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
