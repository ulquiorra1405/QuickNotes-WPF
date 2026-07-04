using System.Globalization;

namespace QuickNotes.Models;

public class Reminder
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid NoteId { get; set; }
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public DateTime DueAt { get; set; }
    public bool IsCompleted { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public bool IsOverdue => !IsCompleted && DueAt <= DateTime.Now;

    /// <summary>Returns true when the reminder is due within the last X seconds.</summary>
    public bool IsFresh(int withinSeconds = 60)
        => !IsCompleted && DueAt <= DateTime.Now && DueAt > DateTime.Now.AddSeconds(-withinSeconds);

    public Reminder Clone() => new()
    {
        Id = Id,
        NoteId = NoteId,
        Title = Title,
        Description = Description,
        DueAt = DueAt,
        IsCompleted = IsCompleted,
        CreatedAt = CreatedAt,
    };
}
