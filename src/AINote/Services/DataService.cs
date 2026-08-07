using System.Text.Json;
using AINote.Models;

namespace AINote.Services;

public sealed class DataService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _dataDir;

    public DataService()
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrWhiteSpace(root))
            root = Environment.GetFolderPath(Environment.SpecialFolder.Personal);
        _dataDir = Path.Combine(root, "AINote");
    }

    public string NotesPath => Path.Combine(_dataDir, "notes.json");
    public string SettingsPath => Path.Combine(_dataDir, "settings.json");

    public List<NoteItem> LoadNotes()
    {
        try
        {
            if (!File.Exists(NotesPath)) return CreateSeed();
            var json = File.ReadAllText(NotesPath);
            var notes = JsonSerializer.Deserialize<List<NoteItem>>(json, JsonOptions);
            if (notes is null) return CreateSeed();
            return notes;
        }
        catch
        {
            return CreateSeed();
        }
    }

    public void SaveNotes(IEnumerable<NoteItem> notes)
    {
        Directory.CreateDirectory(_dataDir);
        File.WriteAllText(NotesPath, JsonSerializer.Serialize(notes.ToList(), JsonOptions));
    }

    public AppSettings LoadSettings()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return new AppSettings();
            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath), JsonOptions) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void SaveSettings(AppSettings settings)
    {
        Directory.CreateDirectory(_dataDir);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, JsonOptions));
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
