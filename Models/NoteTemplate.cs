namespace QuickNotes.Models;

/// <summary>
/// Represents a note template (built-in or user-created).
/// </summary>
public class NoteTemplate
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Icon { get; set; } = "📄";
    public string Content { get; set; } = "";
    public bool IsBuiltIn { get; set; } = true; // true = hardcoded, false = user-saved

    // Built-in templates
    public static List<NoteTemplate> GetBuiltIns()
    {
        return
        [
            new()
            {
                Id = -1,
                Name = "Nota en blanco",
                Icon = "✏️",
                Content = "",
                IsBuiltIn = true,
            },
            new()
            {
                Id = -2,
                Name = "Reunión",
                Icon = "🗓️",
                Content = @"<FlowDocument PagePadding=""0"" FontFamily=""Calibri"" FontSize=""13"" xmlns=""http://schemas.microsoft.com/winfx/2006/xaml/presentation""><Paragraph FontSize=""20"" FontWeight=""Bold""><Run>Reunión: </Run><Run TextDecorations=""Underline"" FontWeight=""Normal"" FontSize=""13"">[título]</Run></Paragraph><Paragraph FontSize=""13""><Run FontWeight=""Bold"">📅 Fecha: </Run><Run TextDecorations=""Underline"">[fecha]</Run></Paragraph><Paragraph FontSize=""13""><Run FontWeight=""Bold"">👥 Asistentes:</Run></Paragraph><List MarkerStyle=""Disc""><ListItem><Paragraph><Run> </Run></Paragraph></ListItem><ListItem><Paragraph><Run> </Run></Paragraph></ListItem></List><Paragraph FontSize=""13""><Run FontWeight=""Bold"">📝 Agenda:</Run></Paragraph><List MarkerStyle=""Decimal""><ListItem><Paragraph><Run> </Run></Paragraph></ListItem><ListItem><Paragraph><Run> </Run></Paragraph></ListItem><ListItem><Paragraph><Run> </Run></Paragraph></ListItem></List><Paragraph FontSize=""13""><Run FontWeight=""Bold"">✅ Acuerdos:</Run></Paragraph><List MarkerStyle=""Disc""><ListItem><Paragraph><Run> </Run></Paragraph></ListItem><ListItem><Paragraph><Run> </Run></Paragraph></ListItem></List></FlowDocument>",
                IsBuiltIn = true,
            },
            new()
            {
                Id = -3,
                Name = "Tarea",
                Icon = "✅",
                Content = @"<FlowDocument PagePadding=""0"" FontFamily=""Calibri"" FontSize=""13"" xmlns=""http://schemas.microsoft.com/winfx/2006/xaml/presentation""><Paragraph FontSize=""20"" FontWeight=""Bold""><Run>Tarea</Run></Paragraph><Paragraph FontSize=""13""><Run>◻ </Run><Run TextDecorations=""Underline"">[descripción]</Run></Paragraph><Paragraph FontSize=""13""><Run>◻ </Run></Paragraph><Paragraph FontSize=""13""><Run>◻ </Run></Paragraph><Paragraph FontSize=""13"" Margin=""0,8,0,0""><Run FontWeight=""Bold"">📅 Vence: </Run><Run TextDecorations=""Underline"">[fecha]</Run></Paragraph></FlowDocument>",
                IsBuiltIn = true,
            },
            new()
            {
                Id = -4,
                Name = "Idea",
                Icon = "💡",
                Content = @"<FlowDocument PagePadding=""0"" FontFamily=""Calibri"" FontSize=""13"" xmlns=""http://schemas.microsoft.com/winfx/2006/xaml/presentation""><Paragraph FontSize=""20"" FontWeight=""Bold""><Run>💡 Idea</Run></Paragraph><Paragraph FontSize=""13""><Run>[descripción]</Run></Paragraph><Paragraph FontSize=""13"" Margin=""0,8,0,0""><Run FontWeight=""Bold"">Contexto:</Run></Paragraph><Paragraph FontSize=""13""><Run> </Run></Paragraph><Paragraph FontSize=""13"" Margin=""0,8,0,0""><Run FontWeight=""Bold"">Próximos pasos:</Run></Paragraph><List MarkerStyle=""Disc""><ListItem><Paragraph><Run> </Run></Paragraph></ListItem><ListItem><Paragraph><Run> </Run></Paragraph></ListItem></List></FlowDocument>",
                IsBuiltIn = true,
            },
            new()
            {
                Id = -5,
                Name = "Diario",
                Icon = "📓",
                Content = @"<FlowDocument PagePadding=""0"" FontFamily=""Calibri"" FontSize=""13"" xmlns=""http://schemas.microsoft.com/winfx/2006/xaml/presentation""><Paragraph FontSize=""20"" FontWeight=""Bold""><Run>📓 [fecha de hoy]</Run></Paragraph><Paragraph FontSize=""13""><Run>Hoy...</Run></Paragraph></FlowDocument>",
                IsBuiltIn = true,
            },
        ];
    }

    /// <summary>
    /// Creates a Note from this template with a unique title.
    /// </summary>
    public Note CreateNote(string defaultColor)
    {
        string title = Name == "Nota en blanco" ? "" : Name;
        string content = Content;
        return new Note
        {
            Title = title,
            Text = content,
            Color = defaultColor,
            LastModified = DateTime.Now,
        };
    }
}
