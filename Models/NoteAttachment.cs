using System;
using System.IO;

namespace QuickNotes.Models;

public class NoteAttachment
{
    public int Id { get; set; }
    public Guid NoteId { get; set; }
    public string FileName { get; set; } = "";
    public string StoredPath { get; set; } = "";
    public long SizeBytes { get; set; }
    public string ContentType { get; set; } = "";
    public DateTime AddedAt { get; set; } = DateTime.Now;

    [System.Text.Json.Serialization.JsonIgnore]
    public string SizeDisplay
    {
        get
        {
            if (SizeBytes < 1024) return $"{SizeBytes} B";
            if (SizeBytes < 1024 * 1024) return $"{SizeBytes / 1024.0:F0} KB";
            return $"{SizeBytes / (1024.0 * 1024.0):F1} MB";
        }
    }

    [System.Text.Json.Serialization.JsonIgnore]
    public string Icon
    {
        get
        {
            var ext = Path.GetExtension(FileName).ToLowerInvariant();
            return ext switch
            {
                ".pdf" => "📄",
                ".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp" or ".webp" => "🖼️",
                ".mp3" or ".wav" or ".flac" or ".ogg" or ".m4a" => "🎵",
                ".mp4" or ".avi" or ".mkv" or ".mov" or ".webm" => "🎬",
                ".doc" or ".docx" => "📝",
                ".xls" or ".xlsx" => "📊",
                ".zip" or ".rar" or ".7z" => "📦",
                ".exe" or ".msi" => "⚙️",
                ".txt" or ".md" => "📃",
                _ => "📎"
            };
        }
    }
}
