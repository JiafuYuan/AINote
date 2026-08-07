using System.Globalization;
using System.Text.Json;
using AINote.Models;
using Microsoft.Data.Sqlite;

namespace AINote.Services;

public sealed class DataService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _dataDir;
    private readonly string _dbPath;
    private readonly string _notesPath;
    private readonly string _settingsPath;

    static DataService()
    {
        SQLitePCL.Batteries_V2.Init();
    }

    public DataService(string? dataDirectory = null)
    {
        var root = dataDirectory;
        if (string.IsNullOrWhiteSpace(root))
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            if (string.IsNullOrWhiteSpace(appData))
                appData = Environment.GetFolderPath(Environment.SpecialFolder.Personal);
            root = Path.Combine(appData, "AINote");
        }

        _dataDir = Path.GetFullPath(root);
        _dbPath = Path.Combine(_dataDir, "ainote.db");
        _notesPath = Path.Combine(_dataDir, "notes.json");
        _settingsPath = Path.Combine(_dataDir, "settings.json");
    }

    public string DbPath => _dbPath;
    public string NotesPath => _notesPath;
    public string SettingsPath => _settingsPath;

    public List<NoteItem> LoadNotes()
    {
        try
        {
            Directory.CreateDirectory(_dataDir);
            var dbExisted = File.Exists(_dbPath);
            EnsureDatabase();

            if (!dbExisted)
            {
                if (File.Exists(_notesPath))
                    MigrateLegacyNotes();
                else
                {
                    var seed = CreateSeed();
                    SaveNotesCore(seed);
                    return seed;
                }
            }

            return ReadNotes();
        }
        catch
        {
            return ReadLegacyNotesOrNew();
        }
    }

    public void SaveNotes(IEnumerable<NoteItem> notes)
    {
        SaveNotesCore(notes);
    }

    public AppSettings LoadSettings()
    {
        try
        {
            Directory.CreateDirectory(_dataDir);
            EnsureDatabase();

            var settings = ReadSettingsFromDb();
            if (settings is not null) return settings;

            if (File.Exists(_settingsPath))
            {
                MigrateLegacySettings();
                settings = ReadSettingsFromDb();
                if (settings is not null) return settings;
            }

            return new AppSettings();
        }
        catch
        {
            return ReadLegacySettingsOrNew();
        }
    }

    private AppSettings? ReadSettingsFromDb()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Value FROM Settings WHERE Key = $key";
        command.Parameters.AddWithValue("$key", "app");
        var value = command.ExecuteScalar() as string;
        if (string.IsNullOrWhiteSpace(value)) return null;
        return JsonSerializer.Deserialize<AppSettings>(value, JsonOptions);
    }

    public void SaveSettings(AppSettings settings)
    {
        EnsureDatabase();
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO Settings (Key, Value)
            VALUES ($key, $value)
            ON CONFLICT(Key) DO UPDATE SET Value = excluded.Value
            """;
        command.Parameters.AddWithValue("$key", "app");
        command.Parameters.AddWithValue("$value", JsonSerializer.Serialize(settings, JsonOptions));
        command.ExecuteNonQuery();
        transaction.Commit();
    }

    private void EnsureDatabase()
    {
        Directory.CreateDirectory(_dataDir);
        using var connection = OpenConnection();
        using var notesCommand = connection.CreateCommand();
        notesCommand.CommandText = """
            CREATE TABLE IF NOT EXISTS Notes (
                Id TEXT PRIMARY KEY NOT NULL,
                Title TEXT NOT NULL,
                Content TEXT NOT NULL,
                Summary TEXT NOT NULL,
                Category TEXT NOT NULL,
                Tags TEXT NOT NULL,
                Stars INTEGER NOT NULL,
                DueDate TEXT NULL,
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL
            )
            """;
        notesCommand.ExecuteNonQuery();

        using var settingsCommand = connection.CreateCommand();
        settingsCommand.CommandText = """
            CREATE TABLE IF NOT EXISTS Settings (
                Key TEXT PRIMARY KEY NOT NULL,
                Value TEXT NOT NULL
            )
            """;
        settingsCommand.ExecuteNonQuery();
    }

    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection($"Data Source={_dbPath}");
        connection.Open();
        return connection;
    }

    private List<NoteItem> ReadNotes()
    {
        var notes = new List<NoteItem>();
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, Title, Content, Summary, Category, Tags, Stars, DueDate, CreatedAt, UpdatedAt
            FROM Notes
            ORDER BY CreatedAt ASC
            """;

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            notes.Add(new NoteItem
            {
                Id = reader.GetString(0),
                Title = reader.GetString(1),
                Content = reader.GetString(2),
                Summary = reader.GetString(3),
                Category = reader.GetString(4),
                Tags = ParseTags(reader.GetString(5)),
                Stars = reader.GetInt32(6),
                DueDate = reader.IsDBNull(7) ? null : ParseDateTime(reader.GetString(7)),
                CreatedAt = ParseDateTime(reader.GetString(8)),
                UpdatedAt = ParseDateTime(reader.GetString(9))
            });
        }

        return notes;
    }

    private void SaveNotesCore(IEnumerable<NoteItem> notes)
    {
        EnsureDatabase();
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();

        using (var deleteCommand = connection.CreateCommand())
        {
            deleteCommand.Transaction = transaction;
            deleteCommand.CommandText = "DELETE FROM Notes";
            deleteCommand.ExecuteNonQuery();
        }

        using var insertCommand = connection.CreateCommand();
        insertCommand.Transaction = transaction;
        insertCommand.CommandText = """
            INSERT INTO Notes (
                Id, Title, Content, Summary, Category, Tags, Stars, DueDate, CreatedAt, UpdatedAt
            )
            VALUES (
                $id, $title, $content, $summary, $category, $tags, $stars, $dueDate, $createdAt, $updatedAt
            )
            """;

        var idParam = insertCommand.Parameters.Add("$id", SqliteType.Text);
        var titleParam = insertCommand.Parameters.Add("$title", SqliteType.Text);
        var contentParam = insertCommand.Parameters.Add("$content", SqliteType.Text);
        var summaryParam = insertCommand.Parameters.Add("$summary", SqliteType.Text);
        var categoryParam = insertCommand.Parameters.Add("$category", SqliteType.Text);
        var tagsParam = insertCommand.Parameters.Add("$tags", SqliteType.Text);
        var starsParam = insertCommand.Parameters.Add("$stars", SqliteType.Integer);
        var dueDateParam = insertCommand.Parameters.Add("$dueDate", SqliteType.Text);
        var createdAtParam = insertCommand.Parameters.Add("$createdAt", SqliteType.Text);
        var updatedAtParam = insertCommand.Parameters.Add("$updatedAt", SqliteType.Text);

        foreach (var note in notes)
        {
            idParam.Value = note.Id;
            titleParam.Value = note.Title ?? "";
            contentParam.Value = note.Content ?? "";
            summaryParam.Value = note.Summary ?? "";
            categoryParam.Value = string.IsNullOrWhiteSpace(note.Category) ? "未分类" : note.Category;
            tagsParam.Value = JsonSerializer.Serialize(note.Tags ?? new List<string>(), JsonOptions);
            starsParam.Value = Math.Clamp(note.Stars, 0, 5);
            dueDateParam.Value = note.DueDate.HasValue
                ? FormatDateTime(note.DueDate.Value)
                : DBNull.Value;
            createdAtParam.Value = FormatDateTime(note.CreatedAt);
            updatedAtParam.Value = FormatDateTime(note.UpdatedAt);
            insertCommand.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    private void MigrateLegacyNotes()
    {
        try
        {
            var notes = JsonSerializer.Deserialize<List<NoteItem>>(File.ReadAllText(_notesPath), JsonOptions);
            if (notes is null || notes.Count == 0) return;
            foreach (var note in notes)
                note.Tags ??= new List<string>();
            SaveNotesCore(notes);
        }
        catch
        {
            // Keep the database usable even if the legacy file cannot be read.
        }
    }

    private void MigrateLegacySettings()
    {
        try
        {
            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_settingsPath), JsonOptions);
            if (settings is null) return;
            SaveSettings(settings);
        }
        catch
        {
            // Ignore malformed legacy settings and use defaults.
        }
    }

    private List<NoteItem> ReadLegacyNotesOrNew()
    {
        try
        {
            if (File.Exists(_notesPath))
            {
                var notes = JsonSerializer.Deserialize<List<NoteItem>>(File.ReadAllText(_notesPath), JsonOptions);
                if (notes is not null)
                {
                    foreach (var note in notes)
                        note.Tags ??= new List<string>();
                    return notes;
                }
            }
        }
        catch
        {
            // Fall through to seed notes.
        }

        return CreateSeed();
    }

    private AppSettings ReadLegacySettingsOrNew()
    {
        try
        {
            if (File.Exists(_settingsPath))
            {
                var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_settingsPath), JsonOptions);
                if (settings is not null) return settings;
            }
        }
        catch
        {
            // Ignore malformed legacy settings and use defaults.
        }

        return new AppSettings();
    }

    private static List<string> ParseTags(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return new List<string>();
        try
        {
            return JsonSerializer.Deserialize<List<string>>(value, JsonOptions) ?? new List<string>();
        }
        catch
        {
            return new List<string>();
        }
    }

    private static string FormatDateTime(DateTime value)
    {
        return value.ToString("O", CultureInfo.InvariantCulture);
    }

    private static DateTime ParseDateTime(string value)
    {
        return DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
    }

    private static List<NoteItem> CreateSeed()
    {
        var today = DateTime.Today;
        return new List<NoteItem>
        {
            new()
            {
                Title = "整理项目周报",
                Content = "汇总本周工作进展、风险与下周计划，重点说明 AI 记事本项目进度。",
                Summary = "整理项目周报，重点说明 AI 记事本项目进展。",
                Category = "工作",
                Tags = { "周报", "项目" },
                Stars = 4,
                DueDate = today,
            },
            new()
            {
                Title = "预约牙医",
                Content = "检查牙齿，尽量约在下班后的时间。",
                Summary = "预约下班后的牙医检查。",
                Category = "生活",
                Tags = { "健康", "预约" },
                Stars = 3,
                DueDate = today.AddDays(1),
            },
            new()
            {
                Title = "阅读《Avalonia 实战》第四章",
                Content = "重点阅读布局、样式和 DataTemplate，整理移动端适配笔记。",
                Summary = "学习 Avalonia 布局与样式，整理移动端适配笔记。",
                Category = "学习",
                Tags = { "阅读", "Avalonia" },
                Stars = 4,
                DueDate = today.AddDays(3),
            },
            new()
            {
                Title = "灵感：新的笔记分类方式",
                Content = "可以按日期、标签和星级组合成智能清单，不用手工维护文件夹。",
                Summary = "用日期、标签和星级组合智能清单。",
                Category = "灵感",
                Tags = { "创意", "整理" },
                Stars = 5,
            },
        };
    }
}
