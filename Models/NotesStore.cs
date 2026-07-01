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
    public ObservableCollection<Tag> Tags { get; } = new();
    public ObservableCollection<Notebook> Notebooks { get; } = new();

    public double MainLeft { get; set; } = 100;
    public double MainTop { get; set; } = 100;
    public double MainWidth { get; set; } = 380;
    public double MainHeight { get; set; } = 420;

    public string Theme { get; set; } = "dark";
    public bool StartWithWindows { get; set; }
    public int AutoSaveInterval { get; set; } = 10;
    public bool BackupEnabled { get; set; } = true;
    public string BackupPath { get; set; } = "";
    public bool ConfirmOnExit { get; set; } = true;
    public string DefaultColor { get; set; } = "";
    public int NoteFontSize { get; set; } = 13;
    public bool CompactMode { get; set; }
    public string NoteFontFamily { get; set; } = "Calibri";
    public bool AnimationsEnabled { get; set; } = true;
    public string OpenNoteIds { get; set; } = "";
    public string SidebarSection { get; set; } = "all";

    public NotesStore()
    {
        Directory.CreateDirectory(folder);
    }

    private SqliteConnection OpenDb()
    {
        var conn = new SqliteConnection($"Data Source={dbPath}");
        conn.Open();

        // WAL mode for better concurrent read/write performance
        using (var pragma = conn.CreateCommand())
        {
            pragma.CommandText = "PRAGMA journal_mode=WAL";
            pragma.ExecuteNonQuery();
        }

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS notes (
                Id TEXT PRIMARY KEY,
                Title TEXT NOT NULL DEFAULT '',
                Text TEXT NOT NULL DEFAULT '',
                Color TEXT NOT NULL DEFAULT '#F8F9FA',
                Icon TEXT NOT NULL DEFAULT '',
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
            CREATE TABLE IF NOT EXISTS tags (
                Id TEXT PRIMARY KEY,
                Name TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS note_tags (
                NoteId TEXT NOT NULL,
                TagId TEXT NOT NULL,
                PRIMARY KEY (NoteId, TagId)
            );
            CREATE TABLE IF NOT EXISTS notebooks (
                Id TEXT PRIMARY KEY,
                Name TEXT NOT NULL,
                Color TEXT NOT NULL DEFAULT '#3A3A3A',
                Icon TEXT NOT NULL DEFAULT '📕'
            );
            """;
        cmd.ExecuteNonQuery();

        // Migration: drop IsMinimized if present (legacy column from old mini-notes)
        using var check = conn.CreateCommand();
        check.CommandText = "SELECT COUNT(*) FROM pragma_table_info('notes') WHERE name='IsMinimized'";
        var hasMinimized = (long)(check.ExecuteScalar() ?? 0) > 0;
        if (hasMinimized)
        {
            using var migrate = conn.CreateCommand();
            migrate.CommandText = "ALTER TABLE notes DROP COLUMN IsMinimized";
            migrate.ExecuteNonQuery();
        }

        // Migration: add Icon column if missing
        using var checkIcon = conn.CreateCommand();
        checkIcon.CommandText = "SELECT COUNT(*) FROM pragma_table_info('notes') WHERE name='Icon'";
        var hasIcon = (long)(checkIcon.ExecuteScalar() ?? 0) > 0;
        if (!hasIcon)
        {
            using var addIcon = conn.CreateCommand();
            addIcon.CommandText = "ALTER TABLE notes ADD COLUMN Icon TEXT NOT NULL DEFAULT ''";
            addIcon.ExecuteNonQuery();
        }

        // Migration: add IsArchived column if missing
        using var checkArchived = conn.CreateCommand();
        checkArchived.CommandText = "SELECT COUNT(*) FROM pragma_table_info('notes') WHERE name='IsArchived'";
        var hasArchived = (long)(checkArchived.ExecuteScalar() ?? 0) > 0;
        if (!hasArchived)
        {
            using var addArchived = conn.CreateCommand();
            addArchived.CommandText = "ALTER TABLE notes ADD COLUMN IsArchived INTEGER NOT NULL DEFAULT 0";
            addArchived.ExecuteNonQuery();
        }

        // Migration: add IsDeleted column if missing
        using var checkDeleted = conn.CreateCommand();
        checkDeleted.CommandText = "SELECT COUNT(*) FROM pragma_table_info('notes') WHERE name='IsDeleted'";
        var hasDeleted = (long)(checkDeleted.ExecuteScalar() ?? 0) > 0;
        if (!hasDeleted)
        {
            using var addDeleted = conn.CreateCommand();
            addDeleted.CommandText = "ALTER TABLE notes ADD COLUMN IsDeleted INTEGER NOT NULL DEFAULT 0";
            addDeleted.ExecuteNonQuery();
        }

        // Migration: add DeletedAt column if missing
        using var checkDeletedAt = conn.CreateCommand();
        checkDeletedAt.CommandText = "SELECT COUNT(*) FROM pragma_table_info('notes') WHERE name='DeletedAt'";
        var hasDeletedAt = (long)(checkDeletedAt.ExecuteScalar() ?? 0) > 0;
        if (!hasDeletedAt)
        {
            using var addDeletedAt = conn.CreateCommand();
            addDeletedAt.CommandText = "ALTER TABLE notes ADD COLUMN DeletedAt TEXT";
            addDeletedAt.ExecuteNonQuery();
        }

        // Migration: add NotebookId column if missing
        using var checkNb = conn.CreateCommand();
        checkNb.CommandText = "SELECT COUNT(*) FROM pragma_table_info('notes') WHERE name='NotebookId'";
        var hasNb = (long)(checkNb.ExecuteScalar() ?? 0) > 0;
        if (!hasNb)
        {
            using var addNb = conn.CreateCommand();
            addNb.CommandText = "ALTER TABLE notes ADD COLUMN NotebookId TEXT";
            addNb.ExecuteNonQuery();
        }

        return conn;
    }

    public void PurgeTrash()
    {
        var cutoff = DateTime.Now.AddDays(-7);
        using var conn = OpenDb();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM notes WHERE IsDeleted = 1 AND DeletedAt IS NOT NULL AND DeletedAt < $cutoff";
        cmd.Parameters.AddWithValue("$cutoff", cutoff.ToString("O"));
        cmd.ExecuteNonQuery();
    }

    public void LoadNotebooks(SqliteConnection conn)
    {
        Notebooks.Clear();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM notebooks ORDER BY Name ASC";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            Notebooks.Add(new Notebook
            {
                Id = Guid.Parse(reader.GetString(reader.GetOrdinal("Id"))),
                Name = reader.GetString(reader.GetOrdinal("Name")),
                Color = reader.GetString(reader.GetOrdinal("Color")),
                Icon = reader.GetString(reader.GetOrdinal("Icon")),
            });
        }
    }

    public void LoadTags(SqliteConnection conn)
    {
        Tags.Clear();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM tags ORDER BY Name ASC";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            Tags.Add(new Tag
            {
                Id = Guid.Parse(reader.GetString(reader.GetOrdinal("Id"))),
                Name = reader.GetString(reader.GetOrdinal("Name")),
            });
        }
    }

    public void LoadNoteTags(SqliteConnection conn)
    {
        // Load tag associations into each note
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT NoteId, TagId FROM note_tags";
        using var reader = cmd.ExecuteReader();
        var map = new Dictionary<Guid, List<Guid>>();
        while (reader.Read())
        {
            var noteId = Guid.Parse(reader.GetString(0));
            var tagId = Guid.Parse(reader.GetString(1));
            if (!map.ContainsKey(noteId)) map[noteId] = new();
            map[noteId].Add(tagId);
        }
        foreach (var n in Notes)
        {
            if (map.TryGetValue(n.Id, out var ids))
                n.TagIds = ids;
        }
    }

    public void Load()
    {
        TryMigrateJson();
        PurgeTrash();

        using var conn = OpenDb();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM notes ORDER BY IsPinned DESC, OrderNum ASC";
        using var reader = cmd.ExecuteReader();
        Notes.Clear();
        while (reader.Read())
        {
            Notes.Add(ReadNote(reader));
        }

        if (Notes.Count == 0 || Notes.All(n => n.IsDeleted))
            Notes.Add(new Note());

        LoadTags(conn);
        LoadNotebooks(conn);
        LoadNoteTags(conn);

        LoadSettingsFromDb(conn);
    }

    public void SaveNoteTags(SqliteConnection conn, Note note)
    {
        using var del = conn.CreateCommand();
        del.CommandText = "DELETE FROM note_tags WHERE NoteId = $id";
        del.Parameters.AddWithValue("$id", note.Id.ToString());
        del.ExecuteNonQuery();

        foreach (var tagId in note.TagIds)
        {
            using var ins = conn.CreateCommand();
            ins.CommandText = "INSERT INTO note_tags (NoteId, TagId) VALUES ($nid, $tid)";
            ins.Parameters.AddWithValue("$nid", note.Id.ToString());
            ins.Parameters.AddWithValue("$tid", tagId.ToString());
            ins.ExecuteNonQuery();
        }
    }

    public void Save()
    {
        using var conn = OpenDb();
        using var tx = conn.BeginTransaction();

        // Delete all existing notes first so removed notes don't linger in DB
        using (var del = conn.CreateCommand())
        {
            del.CommandText = "DELETE FROM notes";
            del.ExecuteNonQuery();
        }

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO notes
                (Id, Title, Text, Color, Icon, IsPinned, IsArchived, IsDeleted, DeletedAt,
                 NotebookId, OrderNum, LastModified,
                 WinLeft, WinTop, WinWidth, WinHeight)
            VALUES ($id, $title, $text, $color, $icon, $pin, $archived, $deleted, $deletedAt,
                    $nbId, $ord, $mod,
                    $wl, $wt, $ww, $wh)
            """;

        foreach (var note in Notes)
        {
            BindNote(cmd, note);
            cmd.ExecuteNonQuery();
            SaveNoteTags(conn, note);
        }

        // Save tags table
        using (var delTags = conn.CreateCommand())
        {
            delTags.CommandText = "DELETE FROM tags";
            delTags.ExecuteNonQuery();
        }
        foreach (var tag in Tags)
        {
            using var insTag = conn.CreateCommand();
            insTag.CommandText = "INSERT INTO tags (Id, Name) VALUES ($id, $name)";
            insTag.Parameters.AddWithValue("$id", tag.Id.ToString());
            insTag.Parameters.AddWithValue("$name", tag.Name);
            insTag.ExecuteNonQuery();
        }

        // Save notebooks table
        using (var delNbs = conn.CreateCommand())
        {
            delNbs.CommandText = "DELETE FROM notebooks";
            delNbs.ExecuteNonQuery();
        }
        foreach (var nb in Notebooks)
        {
            using var insNb = conn.CreateCommand();
            insNb.CommandText = "INSERT INTO notebooks (Id, Name, Color, Icon) VALUES ($id, $name, $color, $icon)";
            insNb.Parameters.AddWithValue("$id", nb.Id.ToString());
            insNb.Parameters.AddWithValue("$name", nb.Name);
            insNb.Parameters.AddWithValue("$color", nb.Color);
            insNb.Parameters.AddWithValue("$icon", nb.Icon);
            insNb.ExecuteNonQuery();
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
        SaveSetting(conn, "BackupPath", BackupPath ?? "");
        SaveSetting(conn, "ConfirmOnExit", ConfirmOnExit ? "1" : "0");
        SaveSetting(conn, "DefaultColor", DefaultColor ?? "");
        SaveSetting(conn, "NoteFontSize", NoteFontSize.ToString(CultureInfo.InvariantCulture));
        SaveSetting(conn, "CompactMode", CompactMode ? "1" : "0");
        SaveSetting(conn, "NoteFontFamily", NoteFontFamily ?? "Calibri");
        SaveSetting(conn, "AnimationsEnabled", AnimationsEnabled ? "1" : "0");
        SaveSetting(conn, "OpenNoteIds", OpenNoteIds ?? "");
        SaveSetting(conn, "SidebarSection", SidebarSection ?? "all");
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
                case "BackupPath": BackupPath = val; break;
                case "ConfirmOnExit": ConfirmOnExit = val == "1"; break;
                case "DefaultColor": DefaultColor = val; break;
                case "NoteFontSize": if (int.TryParse(val, NumberStyles.Any, CultureInfo.InvariantCulture, out var nfs)) NoteFontSize = Math.Clamp(nfs, 8, 48); break;
                case "CompactMode": CompactMode = val == "1"; break;
                case "NoteFontFamily": NoteFontFamily = val; break;
                case "AnimationsEnabled": AnimationsEnabled = val == "1"; break;
                case "OpenNoteIds": OpenNoteIds = val; break;
                case "SidebarSection": SidebarSection = val; break;
            }
        }
    }

    private static Note ReadNote(SqliteDataReader r)
    {
        var idxId = r.GetOrdinal("Id");
        var idxTitle = r.GetOrdinal("Title");
        var idxText = r.GetOrdinal("Text");
        var idxColor = r.GetOrdinal("Color");
        var idxIcon = r.GetOrdinal("Icon");
        var idxPinned = r.GetOrdinal("IsPinned");
        var idxArchived = r.GetOrdinal("IsArchived");
        var idxDeleted = r.GetOrdinal("IsDeleted");
        var idxDeletedAt = r.GetOrdinal("DeletedAt");
        var idxOrder = r.GetOrdinal("OrderNum");
        var idxMod = r.GetOrdinal("LastModified");
        var idxNb = r.GetOrdinal("NotebookId");
        var idxWl = r.GetOrdinal("WinLeft");
        var idxWt = r.GetOrdinal("WinTop");
        var idxWw = r.GetOrdinal("WinWidth");
        var idxWh = r.GetOrdinal("WinHeight");

        var dt = DateTime.Parse(r.GetString(idxMod), CultureInfo.InvariantCulture);
        return new Note
        {
            Id = Guid.Parse(r.GetString(idxId)),
            Title = r.IsDBNull(idxTitle) ? "" : r.GetString(idxTitle),
            Text = r.IsDBNull(idxText) ? "" : r.GetString(idxText),
            Color = r.IsDBNull(idxColor) ? "#F8F9FA" : r.GetString(idxColor),
            Icon = r.IsDBNull(idxIcon) ? "" : r.GetString(idxIcon),
            IsPinned = r.GetInt32(idxPinned) != 0,
            IsArchived = r.GetInt32(idxArchived) != 0,
            IsDeleted = r.GetInt32(idxDeleted) != 0,
            DeletedAt = r.IsDBNull(idxDeletedAt) ? null : DateTime.Parse(r.GetString(idxDeletedAt), CultureInfo.InvariantCulture),
            Order = r.GetInt32(idxOrder),
            LastModified = dt,
            NotebookId = r.IsDBNull(idxNb) ? null : Guid.Parse(r.GetString(idxNb)),
            WinLeft = r.IsDBNull(idxWl) ? double.NaN : r.GetDouble(idxWl),
            WinTop = r.IsDBNull(idxWt) ? double.NaN : r.GetDouble(idxWt),
            WinWidth = r.IsDBNull(idxWw) ? double.NaN : r.GetDouble(idxWw),
            WinHeight = r.IsDBNull(idxWh) ? double.NaN : r.GetDouble(idxWh),
        };
    }

    private static void BindNote(SqliteCommand cmd, Note note)
    {
        cmd.Parameters.Clear();
        cmd.Parameters.AddWithValue("$id", note.Id.ToString());
        cmd.Parameters.AddWithValue("$title", note.Title ?? "");
        cmd.Parameters.AddWithValue("$text", note.Text ?? "");
        cmd.Parameters.AddWithValue("$color", note.Color ?? "#F8F9FA");
        cmd.Parameters.AddWithValue("$icon", note.Icon ?? "");
        cmd.Parameters.AddWithValue("$pin", note.IsPinned ? 1 : 0);
        cmd.Parameters.AddWithValue("$archived", note.IsArchived ? 1 : 0);
        cmd.Parameters.AddWithValue("$deleted", note.IsDeleted ? 1 : 0);
        cmd.Parameters.AddWithValue("$deletedAt", note.DeletedAt?.ToString("O") ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$nbId", note.NotebookId?.ToString() ?? (object)DBNull.Value);
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
                    (Id, Title, Text, Color, IsPinned, OrderNum, LastModified,
                     WinLeft, WinTop, WinWidth, WinHeight)
                VALUES ($id, $title, $text, $color, $pin, $ord, $mod,
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
            string dir = string.IsNullOrWhiteSpace(BackupPath)
                ? Path.Combine(folder, "backups")
                : BackupPath;
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