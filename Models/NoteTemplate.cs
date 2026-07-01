namespace QuickNotes.Models;

/// <summary>
/// Represents a note template (built-in or user-created).
/// </summary>
public class NoteTemplate
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Icon { get; set; } = @"📄";
    public string Content { get; set; } = "";
    public bool IsBuiltIn { get; set; } = true;

    public static List<NoteTemplate> GetBuiltIns()
    {
        return
        [
            new()
            {
                Id = -1,
                Name = "Nota en blanco",
                Icon = @"✏️",
                Content = "",
                IsBuiltIn = true,
            },
            new()
            {
                Id = -2,
                Name = "Reuni\u00f3n",
                Icon = @"🗓️",
                Content = @"<FlowDocument PagePadding=""0"" FontFamily=""Calibri"" FontSize=""13"" xmlns=""http://schemas.microsoft.com/winfx/2006/xaml/presentation""><Paragraph FontSize=""13""><Run>📝 </Run><Run FontWeight=""Bold"">Reunión: </Run><Run TextDecorations=""Underline"">[título del proyecto/cliente]</Run></Paragraph><Paragraph FontSize=""13""><Run>📅 Fecha: </Run><Run TextDecorations=""Underline"">_______________</Run></Paragraph><Paragraph FontSize=""13""><Run>👥 Asistentes: </Run><Run TextDecorations=""Underline"">_______________</Run><LineBreak/><Run TextDecorations=""Underline"">_________________________</Run></Paragraph><Paragraph FontSize=""13"" Margin=""0,4,0,4""><Run FontWeight=""Bold"">Agenda</Run></Paragraph><List MarkerStyle=""Decimal""><ListItem><Paragraph><Run> </Run></Paragraph></ListItem><ListItem><Paragraph><Run> </Run></Paragraph></ListItem><ListItem><Paragraph><Run> </Run></Paragraph></ListItem></List><Paragraph FontSize=""13"" Margin=""0,4,0,4""><Run>➡️ </Run><Run FontWeight=""Bold"">Acuerdos</Run></Paragraph><List MarkerStyle=""Disc""><ListItem><Paragraph><Run> </Run></Paragraph></ListItem><ListItem><Paragraph><Run> </Run></Paragraph></ListItem></List><Paragraph FontSize=""13"" Margin=""0,4,0,4""><Run>⏰ </Run><Run FontWeight=""Bold"">Próximos pasos</Run></Paragraph><List MarkerStyle=""None""><ListItem><Paragraph><Run>◻ </Run></Paragraph></ListItem><ListItem><Paragraph><Run>◻ </Run></Paragraph></ListItem></List></FlowDocument>",
                IsBuiltIn = true,
            },
            new()
            {
                Id = -3,
                Name = "Tarea",
                Icon = @"✅",
                Content = @"<FlowDocument PagePadding=""0"" FontFamily=""Calibri"" FontSize=""13"" xmlns=""http://schemas.microsoft.com/winfx/2006/xaml/presentation""><Paragraph FontSize=""13""><Run>🎯 </Run><Run FontWeight=""Bold"">Tarea: </Run><Run TextDecorations=""Underline"">[descripción]</Run></Paragraph><Paragraph FontSize=""13""><Run>🔴 Prioridad: </Run><Run FontStyle=""Italic"">Alta / Media / Baja</Run></Paragraph><Paragraph FontSize=""13""><Run>📅 Vence: </Run><Run TextDecorations=""Underline"">_______________</Run></Paragraph><Paragraph FontSize=""12"" Margin=""0,6,0,0""><Run>◻ Paso 1</Run></Paragraph><Paragraph FontSize=""12""><Run>◻ Paso 2</Run></Paragraph><Paragraph FontSize=""12""><Run>◻ Paso 3</Run></Paragraph><Paragraph FontSize=""13"" Margin=""0,6,0,0""><Run>🏷️ Etiquetas: </Run><Run TextDecorations=""Underline"">_______________</Run></Paragraph></FlowDocument>",
                IsBuiltIn = true,
            },
            new()
            {
                Id = -4,
                Name = "Idea",
                Icon = @"💡",
                Content = @"<FlowDocument PagePadding=""0"" FontFamily=""Calibri"" FontSize=""13"" xmlns=""http://schemas.microsoft.com/winfx/2006/xaml/presentation""><Paragraph FontSize=""13""><Run>💡 </Run><Run FontWeight=""Bold"">Idea salvaje</Run></Paragraph><Paragraph FontSize=""12""><Run FontStyle=""Italic"">_________________________</Run></Paragraph><Paragraph FontSize=""13"" Margin=""0,6,0,0""><Run>🤔 </Run><Run FontWeight=""Bold"">¿Por qué funcionaría?</Run></Paragraph><Paragraph FontSize=""12""><Run FontStyle=""Italic"">_________________________</Run></Paragraph><Paragraph FontSize=""13"" Margin=""0,6,0,0""><Run>⚡ </Run><Run FontWeight=""Bold"">¿Qué se necesita?</Run></Paragraph><List MarkerStyle=""Disc""><ListItem><Paragraph><Run> </Run></Paragraph></ListItem><ListItem><Paragraph><Run> </Run></Paragraph></ListItem></List><Paragraph FontSize=""13"" Margin=""0,6,0,0""><Run>🔥 Si no lo hago hoy, me arrepiento: </Run><Run FontStyle=""Italic"">Sí / No</Run></Paragraph></FlowDocument>",
                IsBuiltIn = true,
            },
            new()
            {
                Id = -5,
                Name = "Diario",
                Icon = @"📓",
                Content = @"<FlowDocument PagePadding=""0"" FontFamily=""Calibri"" FontSize=""13"" xmlns=""http://schemas.microsoft.com/winfx/2006/xaml/presentation""><Paragraph FontSize=""13""><Run>📓 </Run><Run FontStyle=""Italic"">[fecha de hoy]</Run></Paragraph><Paragraph FontSize=""13"" Margin=""0,6,0,0""><Run FontWeight=""Bold"">¿Qué pasó hoy?</Run></Paragraph><Paragraph FontSize=""12""><Run FontStyle=""Italic"">_________________________</Run></Paragraph><Paragraph FontSize=""13"" Margin=""0,6,0,0""><Run FontWeight=""Bold"">¿Qué aprendí?</Run></Paragraph><Paragraph FontSize=""12""><Run FontStyle=""Italic"">_________________________</Run></Paragraph><Paragraph FontSize=""13"" Margin=""0,6,0,0""><Run>💭 </Run><Run FontWeight=""Bold"">Algo que quiero recordar</Run></Paragraph><Paragraph FontSize=""12""><Run FontStyle=""Italic"">_________________________</Run></Paragraph><Paragraph FontSize=""13"" Margin=""0,6,0,0""><Run FontWeight=""Bold"">Mañana:</Run></Paragraph><Paragraph FontSize=""12""><Run>◻ </Run></Paragraph></FlowDocument>",
                IsBuiltIn = true,
            },
        ];
    }

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
