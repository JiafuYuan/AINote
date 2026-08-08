using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using Avalonia;
using Avalonia.Threading;
using AINote.Models;
using AINote.Services;

namespace AINote.ViewModels;

public sealed class MainWindowViewModel : ObservableObject
{
    private readonly DataService _dataService = new();
    private readonly AiAnalyzer _aiAnalyzer = new();
    private readonly AinoteSyncService _syncService = new();
    private readonly List<NoteItem> _notes;
    private readonly Dictionary<string, NoteRowVm> _rowMap = new();
    private AppSettings _settings;
    private string _selectedSidebarKey = "all";
    private NoteRowVm? _selectedNote;
    private string _addNoteTitle = "";
    private string _addNoteText = "";
    private string _searchText = "";
    private string _statusText = "就绪";
    private string _currentViewTitle = "全部";
    private bool _settingsOpen;
    private bool _addNoteOpen;
    private bool _addNoteAnalyzing;
    private bool _sidebarOpen;
    private bool _isNarrow;
    private bool _isAiAnalyzing;
    private bool _isTestingAi;
    private string _aiTestResult = "";
    private bool _isCheckingUpdate;
    private bool _isUpdateAvailable;
    private string? _latestReleaseUrl;
    private UpdateInfo? _latestUpdateInfo;
    private bool _updateDialogOpen;
    private string _updateVersionText = "";
    private string _updateReleaseNotes = "";
    private bool _isDownloadingUpdate;
    private string _updateDownloadStatus = "";
    private double _updateDownloadProgress;
    private string _updateStatusText = $"当前版本 {UpdateService.CurrentVersionText}";
    private string _temperatureText;
    private bool _isSyncing;
    private string _syncStatusText = "未同步";
    private string _toastText = "";
    private bool _toastOpen;
    private DispatcherTimer? _toastTimer;

    public MainWindowViewModel()
    {
        _notes = _dataService.LoadNotes();
        _settings = _dataService.LoadSettings();
        _temperatureText = _settings.Temperature.ToString("0.##", CultureInfo.InvariantCulture);

        OpenAddNoteCommand = new RelayCommand(() =>
        {
            AddNoteTitle = "";
            AddNoteText = "";
            AddNoteOpen = true;
        });
        CloseAddNoteCommand = new RelayCommand(() => AddNoteOpen = false);
        AddNoteCommand = new RelayCommand(() => _ = AddNoteAsync());
        SaveNoteCommand = new RelayCommand(() => _ = SaveSelectedAsync());
        DeleteNoteCommand = new RelayCommand(() => _ = DeleteSelectedAsync());
        AnalyzeNoteCommand = new RelayCommand(() => _ = AnalyzeSelectedAsync());
        ToggleSettingsCommand = new RelayCommand(() => SettingsOpen = !SettingsOpen);
        CloseSettingsCommand = new RelayCommand(() => SettingsOpen = false);
        TestAiCommand = new RelayCommand(() => _ = TestAiAsync());
        CheckUpdateCommand = new RelayCommand(() => _ = CheckUpdateAsync());
        OpenUpdatePageCommand = new RelayCommand(OpenUpdatePage);
        DownloadUpdateCommand = new RelayCommand(() => _ = DownloadUpdateAsync());
        CloseUpdateDialogCommand = new RelayCommand(() => UpdateDialogOpen = false);
        OpenUpdateDialogCommand = new RelayCommand(OpenUpdateDialog);
        SelectViewCommand = new RelayCommand(SelectView);
        CloseSidebarCommand = new RelayCommand(() => SidebarOpen = false);
        ToggleSidebarCommand = new RelayCommand(() => SidebarOpen = !SidebarOpen);
        BackCommand = new RelayCommand(GoBack);
        SetStarsCommand = new RelayCommand(SetStars);
        SetDueTodayCommand = new RelayCommand(() => SetDueDate(DateTime.Today));
        SetDueTomorrowCommand = new RelayCommand(() => SetDueDate(DateTime.Today.AddDays(1)));
        ClearDueCommand = new RelayCommand(() => SetDueDate(null));
        ClearSearchCommand = new RelayCommand(() => SearchText = "");
        CloseToastCommand = new RelayCommand(() => ToastOpen = false);
        SyncNowCommand = new RelayCommand(() => _ = SyncNowAsync());

        foreach (var note in _notes)
        {
            var row = new NoteRowVm(note, OnNoteChanged, OnNoteMetaChanged);
            _rowMap[note.Id] = row;
        }

        RebuildSidebar();
        RefreshVisibleNotes();

        if (_settings.SyncEnabled && !string.IsNullOrWhiteSpace(_settings.SyncBaseUrl))
            _ = SyncNowAsync();
    }

    public ObservableCollection<NoteRowVm> VisibleNotes { get; } = new();
    public ObservableCollection<SidebarItemVm> SmartSidebarItems { get; } = new();
    public ObservableCollection<SidebarItemVm> CategorySidebarItems { get; } = new();
    public ObservableCollection<SidebarItemVm> TagSidebarItems { get; } = new();

    public ICommand OpenAddNoteCommand { get; }
    public ICommand CloseAddNoteCommand { get; }
    public ICommand AddNoteCommand { get; }
    public ICommand SaveNoteCommand { get; }
    public ICommand DeleteNoteCommand { get; }
    public ICommand AnalyzeNoteCommand { get; }
    public ICommand ToggleSettingsCommand { get; }
    public ICommand CloseSettingsCommand { get; }
    public ICommand TestAiCommand { get; }
    public ICommand CheckUpdateCommand { get; }
    public ICommand OpenUpdatePageCommand { get; }
    public ICommand DownloadUpdateCommand { get; }
    public ICommand CloseUpdateDialogCommand { get; }
    public ICommand OpenUpdateDialogCommand { get; }
    public ICommand SelectViewCommand { get; }
    public ICommand CloseSidebarCommand { get; }
    public ICommand ToggleSidebarCommand { get; }
    public ICommand BackCommand { get; }
    public ICommand SetStarsCommand { get; }
    public ICommand SetDueTodayCommand { get; }
    public ICommand SetDueTomorrowCommand { get; }
    public ICommand ClearDueCommand { get; }
    public ICommand ClearSearchCommand { get; }
    public ICommand CloseToastCommand { get; }
    public ICommand SyncNowCommand { get; }

    public string AddNoteText
    {
        get => _addNoteText;
        set => SetProperty(ref _addNoteText, value);
    }

    public string AddNoteTitle
    {
        get => _addNoteTitle;
        set => SetProperty(ref _addNoteTitle, value);
    }

    public string ToastText
    {
        get => _toastText;
        set => SetProperty(ref _toastText, value);
    }

    public bool ToastOpen
    {
        get => _toastOpen;
        set => SetProperty(ref _toastOpen, value);
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

    public bool AddNoteOpen
    {
        get => _addNoteOpen;
        set
        {
            if (SetProperty(ref _addNoteOpen, value))
                OnPropertyChanged(nameof(IsAddNoteClosed));
        }
    }

    public bool IsAddNoteClosed => !AddNoteOpen;

    public bool AddNoteAnalyzing
    {
        get => _addNoteAnalyzing;
        set => SetProperty(ref _addNoteAnalyzing, value);
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

    public bool IsCheckingUpdate
    {
        get => _isCheckingUpdate;
        set => SetProperty(ref _isCheckingUpdate, value);
    }

    public bool IsUpdateAvailable
    {
        get => _isUpdateAvailable;
        set => SetProperty(ref _isUpdateAvailable, value);
    }

    public string UpdateStatusText
    {
        get => _updateStatusText;
        set => SetProperty(ref _updateStatusText, value);
    }

    public string? LatestReleaseUrl
    {
        get => _latestReleaseUrl;
        set => SetProperty(ref _latestReleaseUrl, value);
    }

    public bool UpdateDialogOpen
    {
        get => _updateDialogOpen;
        set => SetProperty(ref _updateDialogOpen, value);
    }

    public string UpdateVersionText
    {
        get => _updateVersionText;
        set => SetProperty(ref _updateVersionText, value);
    }

    public string UpdateReleaseNotes
    {
        get => _updateReleaseNotes;
        set => SetProperty(ref _updateReleaseNotes, value);
    }

    public bool IsDownloadingUpdate
    {
        get => _isDownloadingUpdate;
        set => SetProperty(ref _isDownloadingUpdate, value);
    }

    public string UpdateDownloadStatus
    {
        get => _updateDownloadStatus;
        set => SetProperty(ref _updateDownloadStatus, value);
    }

    public double UpdateDownloadProgress
    {
        get => _updateDownloadProgress;
        set => SetProperty(ref _updateDownloadProgress, value);
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

    public bool SyncEnabled
    {
        get => _settings.SyncEnabled;
        set
        {
            if (_settings.SyncEnabled == value) return;
            _settings.SyncEnabled = value;
            OnPropertyChanged();
            SaveSettings();
        }
    }

    public string SyncBaseUrl
    {
        get => _settings.SyncBaseUrl;
        set
        {
            if (_settings.SyncBaseUrl == value) return;
            _settings.SyncBaseUrl = value;
            OnPropertyChanged();
            SaveSettings();
        }
    }

    public bool IsSyncing
    {
        get => _isSyncing;
        set => SetProperty(ref _isSyncing, value);
    }

    public string SyncStatusText
    {
        get => _syncStatusText;
        set => SetProperty(ref _syncStatusText, value);
    }

    public void SetLayoutWidth(double width)
    {
        var narrow = width < 760;
        IsNarrow = narrow;
        if (!narrow) SidebarOpen = false;
    }

    public void ShowToast(string text)
    {
        ToastText = text;
        ToastOpen = true;
        _toastTimer?.Stop();
        _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3.2) };
        _toastTimer.Tick += (_, _) =>
        {
            _toastTimer?.Stop();
            ToastOpen = false;
        };
        _toastTimer.Start();
    }

    private async Task AddNoteAsync()
    {
        if (AddNoteAnalyzing) return;

        AddNoteAnalyzing = true;
        try
        {
            var row = AddNoteFromText(AddNoteTitle, AddNoteText);
            if (row is null) return;

            AddNoteTitle = "";
            AddNoteText = "";
            AddNoteOpen = false;
            SelectedNote = row;
            if (_settings.AiAutoAnalyze)
            {
                await AnalyzeAndApplyAsync(row);
            }
            else if (SyncEnabled)
            {
                await TryUpsertAsync(row.Model);
            }
            ShowToast("已保存笔记");
        }
        finally
        {
            AddNoteAnalyzing = false;
        }
    }

    private NoteRowVm? AddNoteFromText(string? rawTitle, string? rawText)
    {
        var text = rawText?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(text)) return null;

        var title = rawTitle?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(title))
            title = text.Length <= 80 ? text : text[..80].TrimEnd() + "…";

        var note = new NoteItem
        {
            Title = title,
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
        return row;
    }

    private async Task AnalyzeAndApplyAsync(NoteRowVm row)
    {
        IsAiAnalyzing = true;
        StatusText = SyncEnabled ? "后台 AI 正在分析…" : "正在按本地规则整理…";
        try
        {
            var input = string.IsNullOrWhiteSpace(row.Model.Content) ? row.Model.Title : row.Model.Content;
            var result = await AnalyzeContentAsync(row.Model.Title, input);
            if (!_rowMap.ContainsKey(row.Model.Id)) return;

            var aiTitle = result.Title?.Trim();
            if (!string.IsNullOrWhiteSpace(aiTitle))
            {
                row.Model.Title = aiTitle;
            }
            row.Model.Category = string.IsNullOrWhiteSpace(result.Category) ? "其他" : result.Category;
            row.Model.Tags = result.Tags;
            row.Model.Stars = Math.Clamp(result.Stars, 0, 5);
            row.Model.Summary = result.Summary;
            if (result.DueDate.HasValue)
            {
                row.Model.DueDate = result.DueDate.Value;
            }
            row.Model.UpdatedAt = DateTime.Now;
            row.Refresh();
            SaveNotes();
            RebuildSidebar();
            RefreshVisibleNotes();
            StatusText = result.UsedLocalFallback
                ? SyncEnabled
                    ? "后台 AI 未调用成功，已按服务器规则归类"
                    : "已按本地规则归类"
                : "后台 AI 分析完成";
            await TryUpsertAsync(row.Model);
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

    private async Task<AiAnalysisResult> AnalyzeContentAsync(string title, string content)
    {
        if (SyncEnabled &&
            Uri.TryCreate(SyncBaseUrl, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            try
            {
                var result = await _syncService.AnalyzeAsync(SyncBaseUrl, title, content);
                SyncStatusText = result.UsedLocalFallback
                    ? "后台 AI 使用服务器本地规则"
                    : "后台 AI 已配置";
                return result;
            }
            catch
            {
                SyncStatusText = "当前离线，使用本地规则";
            }
        }

        var local = await _aiAnalyzer.AnalyzeAsync(content, _settings, title);
        local.UsedLocalFallback = true;
        return local;
    }

    private async Task AnalyzeSelectedAsync()
    {
        if (SelectedNote is null) return;
        await AnalyzeAndApplyAsync(SelectedNote);
        ShowToast(StatusText);
    }

    private async Task SaveSelectedAsync()
    {
        if (SelectedNote is null) return;
        SaveNotes();
        StatusText = $"已保存 {DateTime.Now:HH:mm:ss}";
        ShowToast("已保存");
        await TryUpsertAsync(SelectedNote.Model);
    }

    private async Task DeleteSelectedAsync()
    {
        if (SelectedNote is null) return;
        var model = SelectedNote.Model;
        var id = model.Id;
        _notes.Remove(model);
        _rowMap.Remove(id);
        SelectedNote = null;
        SaveNotes();
        RebuildSidebar();
        RefreshVisibleNotes();
        StatusText = "已删除笔记";
        ShowToast("已删除");
        await TryDeleteAsync(id);
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

    private async Task SyncNowAsync()
    {
        if (IsSyncing) return;
        if (!SyncEnabled)
        {
            SyncStatusText = "云同步未启用";
            return;
        }

        var baseUrl = SyncBaseUrl?.Trim().TrimEnd('/') ?? "";
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            SyncStatusText = "后台地址无效";
            return;
        }

        IsSyncing = true;
        SyncStatusText = "正在获取后台笔记…";
        try
        {
            var remoteNotes = await _syncService.GetNotesAsync(baseUrl);
            var localById = _notes.ToDictionary(x => x.Id);
            var remoteIds = new HashSet<string>(StringComparer.Ordinal);
            var uploadList = new List<NoteItem>();
            var localChanged = false;

            foreach (var remote in remoteNotes)
            {
                remoteIds.Add(remote.Id);
                if (localById.TryGetValue(remote.Id, out var local))
                {
                    if (RemoteTimestampUtc(remote.UpdatedAt) > LocalTimestampUtc(local.UpdatedAt))
                    {
                        ApplyRemoteNote(local, remote);
                        localChanged = true;
                    }
                    else if (LocalTimestampUtc(local.UpdatedAt) > RemoteTimestampUtc(remote.UpdatedAt))
                    {
                        uploadList.Add(local);
                    }
                }
                else
                {
                    var note = CreateFromRemote(remote);
                    _notes.Add(note);
                    _rowMap[note.Id] = new NoteRowVm(note, OnNoteChanged, OnNoteMetaChanged);
                    localChanged = true;
                }
            }

            foreach (var local in _notes)
            {
                if (!remoteIds.Contains(local.Id))
                    uploadList.Add(local);
            }

            if (uploadList.Count > 0)
            {
                SyncStatusText = $"正在上传 {uploadList.Count} 条本地笔记…";
                await _syncService.BatchUpsertAsync(baseUrl, uploadList);
            }

            if (localChanged)
            {
                SaveNotes();
                RebuildSidebar();
                RefreshVisibleNotes();
            }

            SyncStatusText = $"同步完成，本地 {_notes.Count} 条笔记";
            StatusText = SyncStatusText;
        }
        catch
        {
            SyncStatusText = "当前离线，使用本地数据";
            StatusText = SyncStatusText;
        }
        finally
        {
            IsSyncing = false;
        }
    }

    private async Task TryUpsertAsync(NoteItem note)
    {
        if (!SyncEnabled) return;

        try
        {
            await _syncService.UpsertAsync(SyncBaseUrl, note);
            SyncStatusText = "已同步到后台";
        }
        catch
        {
            SyncStatusText = "当前离线，已保存在本地";
            StatusText = SyncStatusText;
        }
    }

    private async Task TryDeleteAsync(string id)
    {
        if (!SyncEnabled) return;

        try
        {
            await _syncService.DeleteAsync(SyncBaseUrl, id);
            SyncStatusText = "已同步删除";
        }
        catch (Exception ex)
        {
            if (ex.Message.Contains("不存在", StringComparison.OrdinalIgnoreCase))
            {
                SyncStatusText = "后台笔记不存在，已忽略";
                return;
            }

            SyncStatusText = "当前离线，删除仅在本地生效";
            StatusText = SyncStatusText;
        }
    }

    private static NoteItem CreateFromRemote(NoteItem remote)
    {
        return new NoteItem
        {
            Id = remote.Id,
            Title = remote.Title,
            Content = remote.Content,
            Summary = remote.Summary,
            Category = string.IsNullOrWhiteSpace(remote.Category) ? "未分类" : remote.Category,
            Tags = new List<string>(remote.Tags),
            Stars = Math.Clamp(remote.Stars, 0, 5),
            DueDate = remote.DueDate,
            CreatedAt = RemoteToLocal(remote.CreatedAt),
            UpdatedAt = RemoteToLocal(remote.UpdatedAt)
        };
    }

    private void ApplyRemoteNote(NoteItem local, NoteItem remote)
    {
        local.Title = remote.Title;
        local.Content = remote.Content;
        local.Summary = remote.Summary;
        local.Category = string.IsNullOrWhiteSpace(remote.Category) ? "未分类" : remote.Category;
        local.Tags = new List<string>(remote.Tags);
        local.Stars = Math.Clamp(remote.Stars, 0, 5);
        local.DueDate = remote.DueDate;
        local.CreatedAt = RemoteToLocal(remote.CreatedAt);
        local.UpdatedAt = RemoteToLocal(remote.UpdatedAt);
        _ = _rowMap.TryGetValue(local.Id, out var row);
        row?.Refresh();
    }

    private static DateTime RemoteTimestampUtc(DateTime value)
    {
        return value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };
    }

    private static DateTime LocalTimestampUtc(DateTime value)
    {
        return value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Local).ToUniversalTime()
        };
    }

    private static DateTime RemoteToLocal(DateTime value)
    {
        return value.Kind switch
        {
            DateTimeKind.Utc => value.ToLocalTime(),
            DateTimeKind.Local => value,
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc).ToLocalTime()
        };
    }

    private async Task TestAiAsync()
    {
        IsTestingAi = true;
        AiTestResult = "正在测试后台连接…";
        try
        {
            if (!SyncEnabled)
            {
                AiTestResult = "请先启用云同步";
                return;
            }

            if (string.IsNullOrWhiteSpace(SyncBaseUrl))
            {
                AiTestResult = "请填写后台地址";
                return;
            }

            if (!Uri.TryCreate(SyncBaseUrl.Trim(), UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                AiTestResult = "后台地址格式无效";
                return;
            }

            var result = await _syncService.AnalyzeAsync(
                SyncBaseUrl,
                "连接测试",
                "这是一条用于测试后台 AI 配置的笔记内容。");
            AiTestResult = result.UsedLocalFallback
                ? "后台已连通，当前使用服务器本地规则"
                : $"后台 AI 已连通，分类：{result.Category}";
        }
        catch (InvalidOperationException ex)
        {
            var msg = ex.Message;
            if (msg.Contains("数据格式不正确", StringComparison.Ordinal))
                AiTestResult = "连接失败：后台返回的数据格式不正确，请确认后台服务版本";
            else if (msg.Contains("连接被拒绝", StringComparison.Ordinal) || msg.Contains("refused", StringComparison.OrdinalIgnoreCase))
                AiTestResult = "连接失败：无法连接到后台，请检查地址和端口";
            else if (msg.Contains("超时", StringComparison.Ordinal) || msg.Contains("timed out", StringComparison.OrdinalIgnoreCase))
                AiTestResult = "连接失败：请求超时，后台响应过慢";
            else
                AiTestResult = $"连接失败：{msg}";
        }
        catch (HttpRequestException ex)
        {
            AiTestResult = $"连接失败：网络异常 — {ex.Message}";
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

    private async Task CheckUpdateAsync()
    {
        if (IsCheckingUpdate)
            return;

        IsCheckingUpdate = true;
        UpdateStatusText = "正在检查更新…";
        IsUpdateAvailable = false;
        _latestUpdateInfo = null;
        try
        {
            var info = await UpdateService.CheckAsync();
            if (info is null)
            {
                UpdateStatusText = "检查更新失败，请检查网络";
                return;
            }

            if (!info.HasUpdate)
            {
                UpdateStatusText = $"当前已是最新版本 {UpdateService.CurrentVersionText}";
                return;
            }

            _latestUpdateInfo = info;
            LatestReleaseUrl = info.HtmlUrl;
            IsUpdateAvailable = true;
            UpdateVersionText = info.Tag.TrimStart('v', 'V');
            UpdateReleaseNotes = string.IsNullOrWhiteSpace(info.Body)
                ? "暂无更新说明，可点击手动下载查看发布页。"
                : info.Body;
            UpdateDownloadStatus = "";
            UpdateDownloadProgress = 0;
            UpdateDialogOpen = true;
            UpdateStatusText = $"发现新版本 {UpdateVersionText}";
        }
        catch
        {
            UpdateStatusText = "检查更新失败，请稍后重试";
        }
        finally
        {
            IsCheckingUpdate = false;
        }
    }

    private void OpenUpdateDialog()
    {
        if (_latestUpdateInfo is null)
            return;

        UpdateVersionText = _latestUpdateInfo.Tag.TrimStart('v', 'V');
        UpdateReleaseNotes = string.IsNullOrWhiteSpace(_latestUpdateInfo.Body)
            ? "暂无更新说明，可点击手动下载查看发布页。"
            : _latestUpdateInfo.Body;
        UpdateDownloadStatus = "";
        UpdateDownloadProgress = 0;
        UpdateDialogOpen = true;
    }

    private void OpenUpdatePage()
    {
        var url = _latestUpdateInfo?.HtmlUrl ?? LatestReleaseUrl;
        if (!string.IsNullOrWhiteSpace(url))
            UpdateService.OpenReleasePage(url);
    }

    private async Task DownloadUpdateAsync()
    {
        var info = _latestUpdateInfo;
        if (info is null || IsDownloadingUpdate)
            return;

        IsDownloadingUpdate = true;
        UpdateDownloadStatus = "正在下载更新…";
        UpdateDownloadProgress = 0;
        var progress = new Progress<double>(value =>
            Dispatcher.UIThread.Post(() => UpdateDownloadProgress = value * 100d));
        try
        {
            var ok = await UpdateService.DownloadAndInstallAsync(info, progress);
            if (!ok)
            {
                UpdateDownloadStatus = "没有找到当前平台的安装包";
                UpdateStatusText = "没有找到当前平台的安装包";
                return;
            }

#if ANDROID
            UpdateDownloadStatus = "安装包已准备，请在系统界面确认安装";
            UpdateDialogOpen = false;
            UpdateStatusText = UpdateDownloadStatus;
#else
            UpdateDownloadStatus = "下载完成，正在安装并重启…";
            UpdateStatusText = UpdateDownloadStatus;
#endif
        }
        catch (Exception ex)
        {
            UpdateDownloadStatus = $"更新失败：{ex.Message}";
            UpdateStatusText = $"更新失败：{ex.Message}";
        }
        finally
        {
            IsDownloadingUpdate = false;
        }
    }
}
