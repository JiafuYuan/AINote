using System.Globalization;
using Avalonia.Media;
using AINote.Models;

namespace AINote.ViewModels;

public sealed class NoteRowVm : ObservableObject
{
    private readonly Action _changed;
    private readonly Action _metaChanged;

    public NoteRowVm(NoteItem model, Action changed, Action? metaChanged = null)
    {
        Model = model;
        _changed = changed;
        _metaChanged = metaChanged ?? changed;
    }

    public NoteItem Model { get; }

    public string Title
    {
        get => Model.Title;
        set
        {
            if (Model.Title == value) return;
            Model.Title = value;
            Model.UpdatedAt = DateTime.Now;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Preview));
            _changed();
        }
    }

    public string Content
    {
        get => Model.Content;
        set
        {
            if (Model.Content == value) return;
            Model.Content = value;
            Model.UpdatedAt = DateTime.Now;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Preview));
            _changed();
        }
    }

    public string Summary
    {
        get => Model.Summary;
        set
        {
            if (Model.Summary == value) return;
            Model.Summary = value;
            Model.UpdatedAt = DateTime.Now;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Preview));
            _changed();
        }
    }

    public string Category
    {
        get => Model.Category;
        set
        {
            if (Model.Category == value) return;
            Model.Category = string.IsNullOrWhiteSpace(value) ? "未分类" : value.Trim();
            Model.UpdatedAt = DateTime.Now;
            OnPropertyChanged();
            OnPropertyChanged(nameof(MetaText));
            OnPropertyChanged(nameof(CategoryBrush));
            _changed();
            _metaChanged();
        }
    }

    public string TagsText
    {
        get => string.Join(", ", Model.Tags);
        set
        {
            var tags = SplitTags(value);
            if (tags.SequenceEqual(Model.Tags)) return;
            Model.Tags = tags;
            Model.UpdatedAt = DateTime.Now;
            OnPropertyChanged();
            OnPropertyChanged(nameof(MetaText));
            _changed();
            _metaChanged();
        }
    }

    public int Stars
    {
        get => Model.Stars;
        set
        {
            var stars = Math.Clamp(value, 0, 5);
            if (Model.Stars == stars) return;
            Model.Stars = stars;
            Model.UpdatedAt = DateTime.Now;
            OnPropertyChanged();
            OnPropertyChanged(nameof(StarsText));
            OnPropertyChanged(nameof(MetaText));
            _changed();
            _metaChanged();
        }
    }

    public DateTime? DueDate
    {
        get => Model.DueDate;
        set
        {
            if (Model.DueDate == value) return;
            Model.DueDate = value;
            Model.UpdatedAt = DateTime.Now;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DueText));
            OnPropertyChanged(nameof(DueDateText));
            OnPropertyChanged(nameof(MetaText));
            _changed();
            _metaChanged();
        }
    }

    public string DueDateText
    {
        get => Model.DueDate?.ToString("yyyy-MM-dd") ?? "";
        set
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                DueDate = null;
                return;
            }
            if (DateTime.TryParse(value, out var date))
                DueDate = date.Date;
        }
    }

    public string DueText => Model.DueDate?.ToString("M月d日 ddd", CultureInfo.GetCultureInfo("zh-CN")) ?? "无日期";
    public string StarsText => Model.Stars > 0 ? new string('★', Model.Stars) : "无星级";
    public string MetaText => $"{Category} · {StarsText} · {DueText}";
    public string Preview => string.IsNullOrWhiteSpace(Model.Summary)
        ? (string.IsNullOrWhiteSpace(Model.Content) ? "暂无内容" : Model.Content)
        : Model.Summary;

    public IBrush CategoryBrush => Category switch
    {
        "工作" => new SolidColorBrush(Color.FromRgb(96, 165, 250)),
        "学习" => new SolidColorBrush(Color.FromRgb(167, 139, 250)),
        "生活" => new SolidColorBrush(Color.FromRgb(52, 211, 153)),
        "健康" => new SolidColorBrush(Color.FromRgb(244, 114, 182)),
        "购物" => new SolidColorBrush(Color.FromRgb(251, 191, 36)),
        "旅行" => new SolidColorBrush(Color.FromRgb(34, 211, 238)),
        "灵感" => new SolidColorBrush(Color.FromRgb(251, 146, 60)),
        _ => new SolidColorBrush(Color.FromRgb(148, 163, 184)),
    };

    public void Refresh()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Content));
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(Category));
        OnPropertyChanged(nameof(TagsText));
        OnPropertyChanged(nameof(Stars));
        OnPropertyChanged(nameof(DueDate));
        OnPropertyChanged(nameof(DueDateText));
        OnPropertyChanged(nameof(DueText));
        OnPropertyChanged(nameof(StarsText));
        OnPropertyChanged(nameof(MetaText));
        OnPropertyChanged(nameof(Preview));
        OnPropertyChanged(nameof(CategoryBrush));
    }

    private static List<string> SplitTags(string value)
        => value.Split(new[] { ',', '，', '、', '#', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct()
            .Take(8)
            .ToList();
}
