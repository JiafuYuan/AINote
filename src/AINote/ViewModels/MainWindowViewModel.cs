using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using Avalonia;
using AINote.Models;
using AINote.Services;

namespace AINote.ViewModels;

public sealed class MainWindowViewModel : ObservableObject
{
    private readonly DataService _dataService = new();
    private readonly AiAnalyzer _aiAnalyzer = new();
    private readonly List<NoteItem> _notes;
    private readonly Dictionary<string, NoteRowVm> _rowMap = new();
    private AppSettings _settings;
    private string _selectedSidebarKey = "all";
    private NoteRowVm? _selectedNote;
    private string _quickText = "";
    private string _searchText = "";
    private string _statusText = "就绪";
    private string _currentViewTitle = "全部";
    private bool _settingsOpen;
    private bool _sidebarOpen;
    private bool _isNarrow;
    private bool _isAiAnalyzing;
    private bool _isTestingAi;
    private string _aiTestResult = "";
    private string _temperatureText;

    public MainWindowViewModel()
    {
        _settings = _dataService.LoadSettings();
        _temperatureText = _settings.Temperature.ToString("0.##", CultureInfo.InvariantCulture);
        _notes = _dataService.LoadNotes();

        QuickAddCommand = new RelayCommand(() => _ = QuickAddAsync());
        SaveNoteCommand = new RelayCommand(SaveSelected);
        DeleteNoteCommand = new RelayCommand(DeleteSelected);
        AnalyzeNoteCommand = new RelayCommand(() => _ = AnalyzeSelectedAsync());
        ToggleSettingsCommand = new RelayCommand(() => SettingsOpen = !SettingsOpen);
        CloseSettingsCommand = new RelayCommand(() => SettingsOpen = false);
        TestAiCommand = new RelayCommand(() => _ = TestAiAsync());
        SelectViewCommand = new RelayCommand(SelectView);
        CloseSidebarCommand = new RelayCommand(() => SidebarOpen = false);
        ToggleSidebarCommand = new RelayCommand(() => SidebarOpen = !SidebarOpen);
        BackCommand = new RelayCommand(GoBack);
        SetStarsCommand = new RelayCommand(SetStars);
        SetDueTodayCommand = new RelayCommand(() => SetDueDate(DateTime.Today));
        SetDueTomorrowCommand = new RelayCommand(() => SetDueDate(DateTime.Today.AddDays(1)));
        ClearDueCommand = new RelayCommand(() => SetDueDate(null));
        ClearSearchCommand = new RelayCommand(() => SearchText = "");

        foreach (var note in _notes)
        {
            var row = new NoteRowVm(note, OnNoteChanged, OnNoteMetaChanged);
            _rowMap[note.Id] = row;
        }

        RebuildSidebar();
        RefreshVisibleNotes();
    }

    public ObservableCollection<NoteRowVm> VisibleNotes { get; } = new();
    public ObservableCollection<SidebarItemVm> SmartSidebarItems { get; } = new();
    public ObservableCollection<SidebarItemVm> CategorySidebarItems { get; } = new();
    public ObservableCollection<SidebarItemVm> TagSidebarItems { get; } = new();

    public ICommand QuickAddCommand { get; }
    public ICommand SaveNoteCommand { get; }
    public ICommand DeleteNoteCommand { get; }
    public ICommand AnalyzeNoteCommand { get; }
    public ICommand ToggleSettingsCommand { get; }
    public ICommand CloseSettingsCommand { get; }
    public ICommand TestAiCommand { get; }
    public ICommand SelectViewCommand { get; }
    public ICommand CloseSidebarCommand { get; }
    public ICommand ToggleSidebarCommand { get; }
    public ICommand BackCommand { get; }
    public ICommand SetStarsCommand { get; }
    public ICommand SetDueTodayCommand { get; }
    public ICommand SetDueTomorrowCommand { get; }
    public ICommand ClearDueCommand { get; }
    public ICommand ClearSearchCommand { get; }

    public string QuickText
    {
        get => _quickText;
        set => SetProperty(ref _quickText, value);
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
                RefreshVisibleNotes();
        }
    }

    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    public string CurrentViewTitle
    {
        get => _currentViewTitle;
        set => SetProperty(ref _currentViewTitle, value);
    }

    public string CurrentViewCountText => $"{VisibleNotes.Count} 条笔记";

    public NoteRowVm? SelectedNote
    {
        get => _selectedNote;
        set
        {
            if (SetProperty(ref _selectedNote, value))
            {
                OnPropertyChanged(nameof(HasSelection));
                OnPropertyChanged(nameof(NoSelection));
                StatusText = value is null ? "就绪" : $"已选择：{value.Title}";
            }
        }
    }

    public bool HasSelection => SelectedNote is not null;
    public bool NoSelection => SelectedNote is null;

    public bool SettingsOpen
    {
        get => _settingsOpen;
        set => SetProperty(ref _settingsOpen, value);
    }

    public bool SidebarOpen
    {
        get => _sidebarOpen;
        set => SetProperty(ref _sidebarOpen, value);
    }

    public bool IsNarrow
    {
        get => _isNarrow;
        set => SetProperty(ref _isNarrow, value);
    }

    public bool IsAiAnalyzing
    {
        get => _isAiAnalyzing;
        set => SetProperty(ref _isAiAnalyzing, value);
    }

    public bool IsTestingAi
    {
        get => _isTestingAi;
        set => SetProperty(ref _isTestingAi, value);
    }

    public string AiTestResult
    {
        get => _aiTestResult;
        set => SetProperty(ref _aiTestResult, value);
    }

    public bool AiEnabled
    {
        get => _settings.AiEnabled;
        set
        {
            if (_settings.AiEnabled == value) return;
            _settings.AiEnabled = value;
            OnPropertyChanged();
            SaveSettings();
        }
    }

    public bool AiAutoAnalyze
    {
        get => _settings.AiAutoAnalyze;
        set
        {
            if (_settings.AiAutoAnalyze == value) return;
            _settings.AiAutoAnalyze = value;
            OnPropertyChanged();
            SaveSettings();
        }
    }

    public string ApiBaseUrl
    {
        get => _settings.ApiBaseUrl;
        set
        {
            if (_settings.ApiBaseUrl == value) return;
            _settings.ApiBaseUrl = value;
            OnPropertyChanged();
            SaveSettings();
        }
    }

    public string ApiKey
    {
        get => _settings.ApiKey;
        set
        {
            if (_settings.ApiKey == value) return;
            _settings.ApiKey = value;
            OnPropertyChanged();
            SaveSettings();
        }
    }

    public string Model
    {
        get => _settings.Model;
        set
        {
            if (_settings.Model == value) return;
            _settings.Model = value;
            OnPropertyChanged();
            SaveSettings();
        }
    }

    public string TemperatureText
    {
        get => _temperatureText;
        set
        {
            if (_temperatureText == value) return;
            _temperatureText = value;
            if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
                _settings.Temperature = Math.Clamp(parsed, 0, 2);
            OnPropertyChanged();
            SaveSettings();
        }
    }

    public void SetLayoutWidth(double width)
    {
        var narrow = width < 760;
        IsNarrow = narrow;
        if (!narrow) SidebarOpen = false;
    }

    private async Task QuickAddAsync()
    {
        var text = QuickText?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(text)) return;

        var note = new NoteItem
        {
            Title = text.Length <= 80 ? text : text[..80].TrimEnd() + "…",
            Content = text,
            CreatedAt = DateTime.Now,
            UpdatedAt = DateTime.Now,
        };
        var row = new NoteRowVm(note, OnNoteChanged, OnNoteMetaChanged);
        _notes.Insert(0, note);
        _rowMap[note.Id] = row;
        SaveNotes();
        RefreshVisibleNotes();
        RebuildSidebar();
        QuickText = "";
        SelectedNote = row;

        if (_settings.AiAutoAnalyze)
            await AnalyzeAndApplyAsync(row);
        else
            StatusText = "已添加笔记";
    }

    private async Task AnalyzeAndApplyAsync(NoteRowVm row)
    {
        IsAiAnalyzing = true;
        StatusText = "AI 正在分析…";
        try
        {
            var input = string.IsNullOrWhiteSpace(row.Model.Content) ? row.Model.Title : row.Model.Content;
            var result = await _aiAnalyzer.AnalyzeAsync(input, _settings);
            if (!_rowMap.ContainsKey(row.Model.Id)) return;

            row.Model.Category = string.IsNullOrWhiteSpace(result.Category) ? "其他" : result.Category;
            row.Model.Tags = result.Tags;
            row.Model.Stars = Math.Clamp(result.Stars, 0, 5);
            row.Model.Summary = result.Summary;
            row.Model.UpdatedAt = DateTime.Now;
            row.Refresh();
            SaveNotes();
            RebuildSidebar();
            RefreshVisibleNotes();
            StatusText = result.UsedLocalFallback ? "AI 未调用成功，已按本地规则归类" : "AI 分析完成";
        }
        catch (Exception ex)
        {
            StatusText = $"AI 分析失败：{ex.Message}";
        }
        finally
        {
            IsAiAnalyzing = false;
        }
    }

    private async Task AnalyzeSelectedAsync()
    {
        if (SelectedNote is null) return;
        await AnalyzeAndApplyAsync(SelectedNote);
    }

    private void SaveSelected()
    {
        SaveNotes();
        StatusText = $"已保存 {DateTime.Now:HH:mm:ss}";
    }

    private void DeleteSelected()
    {
        if (SelectedNote is null) return;
        var model = SelectedNote.Model;
        _notes.Remove(model);
        _rowMap.Remove(model.Id);
        SelectedNote = null;
        SaveNotes();
        RebuildSidebar();
        RefreshVisibleNotes();
        StatusText = "已删除笔记";
    }

    private void SelectView(object? parameter)
    {
        var key = parameter as string ?? "all";
        _selectedSidebarKey = key;
        CurrentViewTitle = key switch
        {
            "all" => "全部",
            "today" => "今天",
            "upcoming" => "即将到来",
            "uncategorized" => "未分类",
            "starred" => "星级",
            _ when key.StartsWith("category:", StringComparison.Ordinal) => key["category:".Length..],
            _ when key.StartsWith("tag:", StringComparison.Ordinal) => "#" + key["tag:".Length..],
            _ => "全部",
        };
        ApplySidebarSelection();
        RefreshVisibleNotes();
        if (IsNarrow) SidebarOpen = false;
    }

    private void GoBack()
    {
        SelectedNote = null;
        SidebarOpen = false;
    }

    private void SetStars(object? parameter)
    {
        if (SelectedNote is null || !int.TryParse(parameter?.ToString(), out var stars)) return;
        SelectedNote.Stars = Math.Clamp(stars, 0, 5);
    }

    private void SetDueDate(DateTime? date)
    {
        if (SelectedNote is null) return;
        SelectedNote.DueDate = date;
    }

    private void RefreshVisibleNotes()
    {
        IEnumerable<NoteRowVm> source = _rowMap.Values;
        var today = DateTime.Today;

        switch (_selectedSidebarKey)
        {
            case "today":
                source = source.Where(x => x.Model.DueDate?.Date == today);
                break;
            case "upcoming":
                source = source.Where(x => x.Model.DueDate?.Date > today);
                break;
            case "uncategorized":
                source = source.Where(x => string.IsNullOrWhiteSpace(x.Model.Category) || x.Model.Category == "未分类");
                break;
            case "starred":
                source = source.Where(x => x.Model.Stars > 0);
                break;
        }

        if (_selectedSidebarKey.StartsWith("category:", StringComparison.Ordinal))
        {
            var category = _selectedSidebarKey["category:".Length..];
            source = source.Where(x => x.Model.Category == category);
        }
        else if (_selectedSidebarKey.StartsWith("tag:", StringComparison.Ordinal))
        {
            var tag = _selectedSidebarKey["tag:".Length..];
            source = source.Where(x => x.Model.Tags.Contains(tag));
        }

        var q = SearchText.Trim();
        if (!string.IsNullOrWhiteSpace(q))
        {
            source = source.Where(x =>
                x.Model.Title.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                x.Model.Content.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                x.Model.Summary.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                x.Model.Category.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                x.Model.Tags.Any(t => t.Contains(q, StringComparison.OrdinalIgnoreCase)));
        }

        var sorted = source
            .OrderByDescending(x => x.Model.Stars)
            .ThenBy(x => x.Model.DueDate ?? DateTime.MaxValue)
            .ThenByDescending(x => x.Model.UpdatedAt)
            .ToList();

        VisibleNotes.Clear();
        foreach (var row in sorted) VisibleNotes.Add(row);
        OnPropertyChanged(nameof(CurrentViewCountText));
    }

    private void RebuildSidebar()
    {
        SmartSidebarItems.Clear();
        CategorySidebarItems.Clear();
        TagSidebarItems.Clear();

        var today = DateTime.Today;
        AddSidebarItem(SmartSidebarItems, "all", "全部", _notes.Count);
        AddSidebarItem(SmartSidebarItems, "today", "今天", _notes.Count(x => x.DueDate?.Date == today));
        AddSidebarItem(SmartSidebarItems, "upcoming", "即将到来", _notes.Count(x => x.DueDate?.Date > today));
        AddSidebarItem(SmartSidebarItems, "starred", "星级", _notes.Count(x => x.Stars > 0));
        AddSidebarItem(SmartSidebarItems, "uncategorized", "未分类", _notes.Count(x => string.IsNullOrWhiteSpace(x.Category) || x.Category == "未分类"));

        foreach (var group in _notes
                     .Where(x => !string.IsNullOrWhiteSpace(x.Category) && x.Category != "未分类")
                     .GroupBy(x => x.Category)
                     .OrderByDescending(x => x.Count())
                     .ThenBy(x => x.Key))
        {
            AddSidebarItem(CategorySidebarItems, "category:" + group.Key, group.Key, group.Count());
        }

        foreach (var group in _notes
                     .SelectMany(x => x.Tags)
                     .Where(x => !string.IsNullOrWhiteSpace(x))
                     .GroupBy(x => x)
                     .OrderByDescending(x => x.Count())
                     .ThenBy(x => x.Key))
        {
            AddSidebarItem(TagSidebarItems, "tag:" + group.Key, group.Key, group.Count());
        }

        ApplySidebarSelection();
    }

    private void AddSidebarItem(ObservableCollection<SidebarItemVm> target, string key, string label, int count)
    {
        var item = new SidebarItemVm(key, label, SelectViewCommand) { Count = count };
        target.Add(item);
    }

    private void ApplySidebarSelection()
    {
        foreach (var item in SmartSidebarItems.Concat(CategorySidebarItems).Concat(TagSidebarItems))
            item.IsSelected = item.Key == _selectedSidebarKey;
    }

    private void OnNoteChanged()
    {
        SaveNotes();
        StatusText = $"已保存 {DateTime.Now:HH:mm:ss}";
    }

    private void OnNoteMetaChanged()
    {
        RebuildSidebar();
        RefreshVisibleNotes();
    }

    private void SaveNotes() => _dataService.SaveNotes(_notes);

    private void SaveSettings() => _dataService.SaveSettings(_settings);

    private async Task TestAiAsync()
    {
        IsTestingAi = true;
        AiTestResult = "正在测试连接…";
        try
        {
            AiTestResult = await _aiAnalyzer.TestAsync(_settings);
        }
        catch (Exception ex)
        {
            AiTestResult = $"连接失败：{ex.Message}";
        }
        finally
        {
            IsTestingAi = false;
        }
    }
}
