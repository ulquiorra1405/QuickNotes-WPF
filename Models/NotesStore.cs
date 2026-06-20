using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using Microsoft.Data.Sqlite;

namespace QuickNotes.Models;

public class NotesStore
{
    private static readonly string folder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "QuickNotes");
    private static readonly string dbPath = Path.Combine(folder, "notes.db");

    public ObservableCollection<Note> Notes { get; } = new();

    public double MainLeft { get; set; } = 100;
    public double MainTop { get; set; } = 100;
    public double MainWidth { get; set; } = 380;
    public double MainHeight { get; set; } = 420;

    public string Theme { get; set; } = "dark";
    public bool StartWithWindows { get; set; }
    public int AutoSaveInterval { get; set; } = 10;
    public bool BackupEnabled { get; set; } = true;
    public bool ConfirmOnExit { get; set; } = true;
    public string DefaultColor { get; set; } = "";
    public int NoteFontSize { get; set; } = 13;
    public string TabBarPosition { get; set; } = "right";
    public bool CompactMode { get; set; }
    public string NoteFontFamily { get; set; } = "Calibri";
    public bool AnimationsEnabled { get; set; } = true;
    public string OpenNoteIds { get; set; } = "";

    public NotesStore()
    {
        Directory.CreateDirectory(folder);
    }

    private SqliteConnection OpenDb()
    {
        var conn = new SqliteConnection($"Data Source={dbPath}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS notes (
                Id TEXT PRIMARY KEY,
                Title TEXT NOT NULL DEFAULT '',
                Text TEXT NOT NULL DEFAULT '',
                Color TEXT NOT NULL DEFAULT '#F8F9FA',
                IsMinimized INTEGER NOT NULL DEFAULT 0,
                IsPinned INTEGER NOT NULL DEFAULT 0,
                OrderNum INTEGER NOT NULL DEFAULT 0,
                LastModified TEXT NOT NULL,
                WinLeft REAL,
                WinTop REAL,
                WinWidth REAL,
                WinHeight REAL
            );
            CREATE TABLE IF NOT EXISTS settings (
                Key TEXT PRIMARY KEY,
                Value TEXT NOT NULL
            );
            """;
        cmd.ExecuteNonQuery();
        return conn;
    }

    public void Load()
    {
        TryMigrateJson();

        using var conn = OpenDb();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM notes ORDER BY IsPinned DESC, OrderNum ASC";
        using var reader = cmd.ExecuteReader();
        Notes.Clear();
        while (reader.Read())
        {
            Notes.Add(ReadNote(reader));
        }

        if (Notes.Count == 0)
            Notes.Add(new Note());

        LoadSettingsFromDb(conn);
    }

    public void Save()
    {
        using var conn = OpenDb();
        using var tx = conn.BeginTransaction();

        for (int i = 0; i < Notes.Count; i++)
            Notes[i].Order = i;

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT OR REPLACE INTO notes
                (Id, Title, Text, Color, IsMinimized, IsPinned, OrderNum, LastModified,
                 WinLeft, WinTop, WinWidth, WinHeight)
            VALUES ($id, $title, $text, $color, $min, $pin, $ord, $mod,
                    $wl, $wt, $ww, $wh)
            """;

        foreach (var note in Notes)
        {
            BindNote(cmd, note);
            cmd.ExecuteNonQuery();
        }

        SaveSettingsToDb(conn);
        tx.Commit();
        MaybeBackup(conn);
    }

    public void SaveSettings()
    {
        using var conn = OpenDb();
        SaveSettingsToDb(conn);
    }

    private void SaveSettingsToDb(SqliteConnection conn)
    {
        SaveSetting(conn, "MainLeft", MainLeft.ToString(CultureInfo.InvariantCulture));
        SaveSetting(conn, "MainTop", MainTop.ToString(CultureInfo.InvariantCulture));
        SaveSetting(conn, "MainWidth", MainWidth.ToString(CultureInfo.InvariantCulture));
        SaveSetting(conn, "MainHeight", MainHeight.ToString(CultureInfo.InvariantCulture));
        SaveSetting(conn, "Theme", Theme);
        SaveSetting(conn, "StartWithWindows", StartWithWindows ? "1" : "0");
        SaveSetting(conn, "AutoSaveInterval", AutoSaveInterval.ToString(CultureInfo.InvariantCulture));
        SaveSetting(conn, "BackupEnabled", BackupEnabled ? "1" : "0");
        SaveSetting(conn, "ConfirmOnExit", ConfirmOnExit ? "1" : "0");
        SaveSetting(conn, "DefaultColor", DefaultColor ?? "");
        SaveSetting(conn, "NoteFontSize", NoteFontSize.ToString(CultureInfo.InvariantCulture));
        SaveSetting(conn, "TabBarPosition", TabBarPosition ?? "right");
        SaveSetting(conn, "CompactMode", CompactMode ? "1" : "0");
        SaveSetting(conn, "NoteFontFamily", NoteFontFamily ?? "Calibri");
        SaveSetting(conn, "AnimationsEnabled", AnimationsEnabled ? "1" : "0");
        SaveSetting(conn, "OpenNoteIds", OpenNoteIds ?? "");
    }

    private static void SaveSetting(SqliteConnection conn, string key, string? value)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT OR REPLACE INTO settings (Key, Value) VALUES ($k, $v)";
        cmd.Parameters.AddWithValue("$k", key);
        cmd.Parameters.AddWithValue("$v", value ?? "");
        cmd.ExecuteNonQuery();
    }

    private void LoadSettingsFromDb(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Key, Value FROM settings";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var key = reader.GetString(0);
            var val = reader.GetString(1);
            switch (key)
            {
                case "MainLeft": if (double.TryParse(val, NumberStyles.Any, CultureInfo.InvariantCulture, out var dl)) MainLeft = dl; break;
                case "MainTop": if (double.TryParse(val, NumberStyles.Any, CultureInfo.InvariantCulture, out var dt)) MainTop = dt; break;
                case "MainWidth": if (double.TryParse(val, NumberStyles.Any, CultureInfo.InvariantCulture, out var dw)) MainWidth = dw; break;
                case "MainHeight": if (double.TryParse(val, NumberStyles.Any, CultureInfo.InvariantCulture, out var dh)) MainHeight = dh; break;
                case "Theme": Theme = val; break;
                case "StartWithWindows": StartWithWindows = val == "1"; break;
                case "AutoSaveInterval": if (int.TryParse(val, NumberStyles.Any, CultureInfo.InvariantCulture, out var asi)) AutoSaveInterval = Math.Clamp(asi, 3, 300); break;
                case "BackupEnabled": BackupEnabled = val == "1"; break;
                case "ConfirmOnExit": ConfirmOnExit = val == "1"; break;
                case "DefaultColor": DefaultColor = val; break;
                case "NoteFontSize": if (int.TryParse(val, NumberStyles.Any, CultureInfo.InvariantCulture, out var nfs)) NoteFontSize = Math.Clamp(nfs, 8, 48); break;
                case "TabBarPosition": TabBarPosition = val is "left" or "right" or "top" or "bottom" ? val : "right"; break;
                case "CompactMode": CompactMode = val == "1"; break;
                case "NoteFontFamily": NoteFontFamily = val; break;
                case "AnimationsEnabled": AnimationsEnabled = val == "1"; break;
                case "OpenNoteIds": OpenNoteIds = val; break;
            }
        }
    }

    private static Note ReadNote(SqliteDataReader r)
    {
        var dt = DateTime.Parse(r.GetString(7), CultureInfo.InvariantCulture);
        return new Note
        {
            Id = Guid.Parse(r.GetString(0)),
            Title = r.IsDBNull(1) ? "" : r.GetString(1),
            Text = r.IsDBNull(2) ? "" : r.GetString(2),
            Color = r.IsDBNull(3) ? "#F8F9FA" : r.GetString(3),
            IsMinimized = r.GetInt32(4) != 0,
            IsPinned = r.GetInt32(5) != 0,
            Order = r.GetInt32(6),
            LastModified = dt,
            WinLeft = r.IsDBNull(8) ? double.NaN : r.GetDouble(8),
            WinTop = r.IsDBNull(9) ? double.NaN : r.GetDouble(9),
            WinWidth = r.IsDBNull(10) ? double.NaN : r.GetDouble(10),
            WinHeight = r.IsDBNull(11) ? double.NaN : r.GetDouble(11),
        };
    }

    private static void BindNote(SqliteCommand cmd, Note note)
    {
        cmd.Parameters.Clear();
        cmd.Parameters.AddWithValue("$id", note.Id.ToString());
        cmd.Parameters.AddWithValue("$title", note.Title ?? "");
        cmd.Parameters.AddWithValue("$text", note.Text ?? "");
        cmd.Parameters.AddWithValue("$color", note.Color ?? "#F8F9FA");
        cmd.Parameters.AddWithValue("$min", note.IsMinimized ? 1 : 0);
        cmd.Parameters.AddWithValue("$pin", note.IsPinned ? 1 : 0);
        cmd.Parameters.AddWithValue("$ord", note.Order);
        cmd.Parameters.AddWithValue("$mod", note.LastModified.ToString("O"));
        cmd.Parameters.AddWithValue("$wl", double.IsNaN(note.WinLeft) ? DBNull.Value : note.WinLeft);
        cmd.Parameters.AddWithValue("$wt", double.IsNaN(note.WinTop) ? DBNull.Value : note.WinTop);
        cmd.Parameters.AddWithValue("$ww", double.IsNaN(note.WinWidth) ? DBNull.Value : note.WinWidth);
        cmd.Parameters.AddWithValue("$wh", double.IsNaN(note.WinHeight) ? DBNull.Value : note.WinHeight);
    }

    private void TryMigrateJson()
    {
        var jsonFile = Path.Combine(folder, "notes.json");
        if (!File.Exists(jsonFile)) return;
        if (File.Exists(dbPath)) return;

        try
        {
            var json = File.ReadAllText(jsonFile);
            var list = System.Text.Json.JsonSerializer.Deserialize<List<Note>>(json);
            if (list == null || list.Count == 0) return;

            using var conn = OpenDb();
            using var tx = conn.BeginTransaction();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO notes
                    (Id, Title, Text, Color, IsMinimized, IsPinned, OrderNum, LastModified,
                     WinLeft, WinTop, WinWidth, WinHeight)
                VALUES ($id, $title, $text, $color, $min, $pin, $ord, $mod,
                        $wl, $wt, $ww, $wh)
                """;

            foreach (var note in list.OrderBy(n => n.Order))
            {
                BindNote(cmd, note);
                cmd.ExecuteNonQuery();
            }

            var settingsFile = Path.Combine(folder, "settings.json");
            if (File.Exists(settingsFile))
            {
                var sjson = File.ReadAllText(settingsFile);
                var sd = System.Text.Json.JsonSerializer.Deserialize<SettingsData>(sjson);
                if (sd != null)
                {
                    MainLeft = sd.MainLeft;
                    MainTop = sd.MainTop;
                    MainWidth = sd.MainWidth;
                    MainHeight = sd.MainHeight;
                }
            }

            SaveSettingsToDb(conn);
            tx.Commit();

            File.Move(jsonFile, Path.Combine(folder, "notes.json.bak"));
            var sfile = Path.Combine(folder, "settings.json");
            if (File.Exists(sfile))
                File.Move(sfile, Path.Combine(folder, "settings.json.bak"));
        }
        catch (Exception ex)
        {
            ErrorLog.Write(ex, "TryMigrateJson");
        }
    }

    public void SortNotes()
    {
        var sorted = Notes.OrderByDescending(n => n.IsPinned).ThenBy(n => n.Order).ToList();
        for (int i = 0; i < sorted.Count; i++)
        {
            var idx = Notes.IndexOf(sorted[i]);
            if (idx != i)
                Notes.Move(idx, i);
        }
    }

    private void MaybeBackup(SqliteConnection conn)
    {
        if (!BackupEnabled) return;
        try
        {
            string dir = Path.Combine(folder, "backups");
            Directory.CreateDirectory(dir);

            string today = DateTime.Now.ToString("yyyy-MM-dd");
            if (Directory.GetFiles(dir, $"notes_{today}_*.db").Length > 0)
                return;

            var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HHmmss");
            var backupPath = Path.Combine(dir, $"notes_{timestamp}.db");

            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"VACUUM INTO '{backupPath.Replace("'", "''")}'";
            cmd.ExecuteNonQuery();

            var backups = Directory.GetFiles(dir, "notes_*.db")
                                   .OrderByDescending(f => f)
                                   .ToList();
            while (backups.Count > 10)
            {
                File.Delete(backups.Last());
                backups.RemoveAt(backups.Count - 1);
            }
        }
        catch (Exception ex)
        {
            ErrorLog.Write(ex, "Backup");
        }
    }

    private class SettingsData
    {
        public double MainLeft { get; set; } = 100;
        public double MainTop { get; set; } = 100;
        public double MainWidth { get; set; } = 380;
        public double MainHeight { get; set; } = 420;
    }
}