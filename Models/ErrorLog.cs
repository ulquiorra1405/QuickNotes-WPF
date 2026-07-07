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
                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {context}: {ex.GetType().Name}: {ex.Message}");
                var inner = ex.InnerException;
                while (inner != null)
                {
                    sb.AppendLine($"  Inner: {inner.GetType().Name}: {inner.Message}");
                    inner = inner.InnerException;
                }
                sb.AppendLine(ex.StackTrace);
                sb.AppendLine();
                File.AppendAllText(logPath, sb.ToString());
            }
        }
        catch { }
    }

    public static void Write(string text)
    {
        try
        {
            lock (_lock)
            {
                var dir = Path.GetDirectoryName(logPath);
                if (dir != null) Directory.CreateDirectory(dir);
                var msg = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {text}\n\n";
                File.AppendAllText(logPath, msg);
            }
        }
        catch { }
    }
}
