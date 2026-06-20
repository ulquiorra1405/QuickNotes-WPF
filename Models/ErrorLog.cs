using System.IO;

namespace QuickNotes.Models;

public static class ErrorLog
{
    private static readonly string logPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "QuickNotes", "error.log");

    private static readonly object _lock = new();

    public static void Write(Exception ex, string context)
    {
        try
        {
            lock (_lock)
            {
                var dir = Path.GetDirectoryName(logPath);
                if (dir != null) Directory.CreateDirectory(dir);
                File.AppendAllText(logPath,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {context}: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}\n\n");
            }
        }
        catch { }
    }

    public static void Write(string message)
    {
        try
        {
            lock (_lock)
            {
                var dir = Path.GetDirectoryName(logPath);
                if (dir != null) Directory.CreateDirectory(dir);
                File.AppendAllText(logPath,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}\n");
            }
        }
        catch { }
    }
}