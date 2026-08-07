using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Globalization;
using AINote.Models;

namespace AINote.Services;

public sealed partial class AiAnalyzer
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(45) };

    public async Task<AiAnalysisResult> AnalyzeAsync(
        string input,
        AppSettings settings,
        string? originalTitle = null,
        CancellationToken ct = default)
    {
        if (!settings.AiEnabled || string.IsNullOrWhiteSpace(settings.ApiKey) || string.IsNullOrWhiteSpace(input))
        {
            var local = AnalyzeLocally(input, originalTitle);
            local.UsedLocalFallback = true;
            return local;
        }

        try
        {
            return await AnalyzeWithAiAsync(input, settings, originalTitle, ct);
        }
        catch
        {
            var local = AnalyzeLocally(input, originalTitle);
            local.UsedLocalFallback = true;
            return local;
        }
    }

    public async Task<string> TestAsync(AppSettings settings, CancellationToken ct = default)
    {
        if (!settings.AiEnabled || string.IsNullOrWhiteSpace(settings.ApiKey))
            return "请先启用 AI 并填写 API Key";

        try
        {
            await AnalyzeWithAiAsync("你好，请回复连接正常。", settings, null, ct);
            return $"连接成功，模型：{settings.Model}";
        }
        catch (Exception ex)
        {
            return $"连接失败：{ex.Message}";
        }
    }

    private async Task<AiAnalysisResult> AnalyzeWithAiAsync(
        string input,
        AppSettings settings,
        string? originalTitle,
        CancellationToken ct)
    {
        var now = DateTime.Now;
        var systemPrompt =
            "你是中文笔记整理助手。根据用户输入的笔记内容，只返回一个 JSON 对象，不要输出解释或 Markdown。" +
            $"当前日期：{now:yyyy-MM-dd}。" +
            "JSON 格式：{\"title\":\"简洁标题\",\"category\":\"分类\",\"tags\":[\"标签\"],\"stars\":1,\"summary\":\"一句话摘要\",\"dueDate\":\"YYYY-MM-DD HH:mm\"}。" +
            "分类只能从：工作、学习、生活、健康、购物、旅行、灵感、其他 中选择。stars 是 1 到 5 的整数。" +
            "title 是 20 字以内的简洁标题，若输入已有标题可优化为更清晰、可检索的标题。" +
            "dueDate 必须解析输入中的日期时间，例如“明晚八点”应返回明天 20:00；没有明确日期时间时返回 null。";

        var userMessage = string.IsNullOrWhiteSpace(originalTitle)
            ? input
            : $"原标题：{originalTitle.Trim()}\n内容：{input}";
        var payload = new
        {
            model = settings.Model,
            temperature = settings.Temperature,
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userMessage },
            },
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, BuildUrl(settings.ApiBaseUrl));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
        request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        using var response = await _http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(ct);
        return ParseAiJson(json) ?? AnalyzeLocally(input, originalTitle);
    }

    private static string BuildUrl(string baseUrl)
    {
        var url = baseUrl.Trim().TrimEnd('/');
        if (url.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
            return url;
        return url + "/chat/completions";
    }

    private static AiAnalysisResult? ParseAiJson(string text)
    {
        var payload = ExtractJsonPayload(text);
        if (payload is null) return null;

        try
        {
            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;

            var category = GetString(root, "category");
            if (string.IsNullOrWhiteSpace(category)) return null;

            var tags = new List<string>();
            if (root.TryGetProperty("tags", out var tagEl) && tagEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in tagEl.EnumerateArray().Take(6))
                {
                    var tag = item.GetString()?.Trim();
                    if (!string.IsNullOrWhiteSpace(tag)) tags.Add(tag);
                }
            }

            var stars = root.TryGetProperty("stars", out var starEl) && starEl.TryGetInt32(out var s) ? s : 3;
            stars = Math.Clamp(stars, 1, 5);
            DateTime? dueDate = null;
            if (root.TryGetProperty("dueDate", out var dueEl) && dueEl.ValueKind == JsonValueKind.String)
            {
                dueDate = ParseDueDate(dueEl.GetString());
            }

            return new AiAnalysisResult
            {
                Title = GetString(root, "title"),
                Category = category,
                Tags = tags,
                Stars = stars,
                Summary = GetString(root, "summary"),
                DueDate = dueDate,
            };
        }
        catch
        {
            return null;
        }
    }

    private static string? ExtractJsonPayload(string text)
    {
        var trimmed = text.Trim();

        try
        {
            using var doc = JsonDocument.Parse(trimmed);
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("category", out _))
                return trimmed;

            if (root.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty("choices", out var choices) &&
                choices.ValueKind == JsonValueKind.Array &&
                choices.GetArrayLength() > 0)
            {
                var message = choices[0].ValueKind == JsonValueKind.Object &&
                              choices[0].TryGetProperty("message", out var m)
                    ? m
                    : choices[0];

                if (message.ValueKind == JsonValueKind.Object &&
                    message.TryGetProperty("content", out var content) &&
                    content.ValueKind == JsonValueKind.String)
                {
                    return content.GetString();
                }
            }
        }
        catch
        {
            // Some providers wrap the JSON in Markdown; fall through to brace extraction.
        }

        var start = trimmed.IndexOf('{');
        var end = trimmed.LastIndexOf('}');
        if (start < 0 || end <= start) return null;
        return trimmed.Substring(start, end - start + 1);
    }

    private static string GetString(JsonElement root, string name)
        => root.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String ? el.GetString() ?? "" : "";

    private AiAnalysisResult AnalyzeLocally(string input, string? originalTitle = null)
    {
        var text = input.Trim();
        var category = GuessCategory(text);
        var tags = ExtractTags(text, category);
        var stars = GuessStars(text);
        var summary = BuildSummary(text);
        var dueDate = GuessDueDate(text);

        return new AiAnalysisResult
        {
            Title = string.IsNullOrWhiteSpace(originalTitle) ? BuildTitle(text) : originalTitle.Trim(),
            Category = category,
            Tags = tags,
            Stars = stars,
            Summary = summary,
            DueDate = dueDate,
            UsedLocalFallback = true,
        };
    }

    private static string GuessCategory(string text)
    {
        if (ContainsAny(text, "工作", "项目", "会议", "邮件", "客户", "需求", "汇报", "周报", "代码", "任务"))
            return "工作";
        if (ContainsAny(text, "学习", "书", "课程", "教程", "阅读", "考试", "笔记", "教程", "技能"))
            return "学习";
        if (ContainsAny(text, "买", "购物", "淘宝", "京东", "价格", "清单"))
            return "购物";
        if (ContainsAny(text, "医院", "牙医", "药", "跑步", "健身", "运动", "健康", "体检"))
            return "健康";
        if (ContainsAny(text, "旅行", "机票", "飞机", "酒店", "景点", "行程", "旅游"))
            return "旅行";
        if (ContainsAny(text, "灵感", "创意", "想法", "点子"))
            return "灵感";
        return "其他";
    }

    private static List<string> ExtractTags(string text, string category)
    {
        var tags = new List<string>();
        foreach (Match m in HashTagRegex().Matches(text))
        {
            var tag = m.Groups[1].Value.Trim();
            if (!string.IsNullOrWhiteSpace(tag) && !tags.Contains(tag)) tags.Add(tag);
        }

        if (tags.Count < 3 && !tags.Contains(category))
            tags.Add(category);
        return tags.Take(6).ToList();
    }

    private static int GuessStars(string text)
    {
        if (ContainsAny(text, "紧急", "必须", "截止", "尽快", "重要")) return 5;
        if (ContainsAny(text, "关键", "重点", "优先")) return 4;
        if (ContainsAny(text, "随手", "备忘", "简单", "琐事")) return 2;
        return 3;
    }

    private static string BuildSummary(string text)
    {
        var oneLine = Regex.Replace(text.Replace("\r", " ").Replace("\n", " "), @"\s+", " ").Trim();
        if (oneLine.Length <= 42) return oneLine;
        return oneLine[..42].TrimEnd() + "…";
    }

    private static string BuildTitle(string text)
    {
        var oneLine = Regex.Replace(text.Replace("\r", " ").Replace("\n", " "), @"\s+", " ").Trim();
        return oneLine.Length <= 20 ? oneLine : oneLine[..20].TrimEnd() + "…";
    }

    private static DateTime? ParseDueDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Trim().Equals("null", StringComparison.OrdinalIgnoreCase))
            return null;

        var text = value.Trim();
        var formats = new[]
        {
            "yyyy-MM-dd HH:mm",
            "yyyy-MM-dd HH:mm:ss",
            "yyyy-MM-ddTHH:mm:ss",
            "yyyy-MM-dd",
            "yyyy/MM/dd HH:mm",
            "yyyy/M/d H:mm"
        };
        if (DateTime.TryParseExact(text, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var exact))
            return exact;
        return DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed) ? parsed : null;
    }

    private static DateTime? GuessDueDate(string text)
    {
        var normalized = NormalizeChineseDigits(text);
        var now = DateTime.Now;
        DateTime? day = null;

        if (ContainsAny(normalized, "大后天"))
            day = now.Date.AddDays(3);
        else if (ContainsAny(normalized, "后天"))
            day = now.Date.AddDays(2);
        else if (ContainsAny(normalized, "明晚", "明天晚上", "明晚上"))
            day = now.Date.AddDays(1);
        else if (ContainsAny(normalized, "明天", "明日", "明早", "明晨"))
            day = now.Date.AddDays(1);
        else if (ContainsAny(normalized, "今晚", "今天晚上"))
            day = now.Date;
        else if (ContainsAny(normalized, "今天", "今日"))
            day = now.Date;
        else if (ContainsAny(normalized, "昨晚"))
            day = now.Date.AddDays(-1);

        if (day is null && TryParseWeekday(normalized, out var weekday, out var nextWeek))
        {
            var offset = ((int)weekday - (int)now.DayOfWeek + 7) % 7;
            if (offset == 0 && !nextWeek && !ContainsAny(normalized, "本", "这"))
                offset = 7;
            if (nextWeek)
                offset += 7;
            day = now.Date.AddDays(offset);
        }

        day ??= TryParseAbsoluteDate(normalized);
        if (day is null)
            return null;

        var time = ExtractDueTime(normalized);
        return time.HasValue ? day.Value.Date.Add(time.Value) : day.Value.Date;
    }

    private static bool TryParseWeekday(string text, out DayOfWeek day, out bool nextWeek)
    {
        day = DayOfWeek.Monday;
        nextWeek = false;
        var match = Regex.Match(text, @"(下)?(?:周|星期|礼拜)([一二三四五六日天])");
        if (!match.Success)
            return false;

        nextWeek = match.Groups[1].Success;
        day = match.Groups[2].Value switch
        {
            "一" => DayOfWeek.Monday,
            "二" => DayOfWeek.Tuesday,
            "三" => DayOfWeek.Wednesday,
            "四" => DayOfWeek.Thursday,
            "五" => DayOfWeek.Friday,
            "六" => DayOfWeek.Saturday,
            _ => DayOfWeek.Sunday
        };
        return true;
    }

    private static DateTime? TryParseAbsoluteDate(string text)
    {
        var match = Regex.Match(text, @"(20\d{2})[年/-](\d{1,2})[-/月](\d{1,2})日?");
        if (match.Success)
        {
            return TryCreateDate(
                int.Parse(match.Groups[1].Value),
                int.Parse(match.Groups[2].Value),
                int.Parse(match.Groups[3].Value));
        }

        match = Regex.Match(text, @"(\d{1,2})月(\d{1,2})日");
        if (!match.Success)
            return null;

        return TryCreateDate(
            DateTime.Now.Year,
            int.Parse(match.Groups[1].Value),
            int.Parse(match.Groups[2].Value));
    }

    private static DateTime? TryCreateDate(int year, int month, int day)
    {
        try
        {
            return new DateTime(year, month, day);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private static TimeSpan? ExtractDueTime(string text)
    {
        var colonMatch = Regex.Match(text, @"(\d{1,2})[:：](\d{1,2})");
        if (colonMatch.Success)
        {
            var hour = int.Parse(colonMatch.Groups[1].Value);
            var minute = int.Parse(colonMatch.Groups[2].Value);
            if (hour > 23 || minute > 59)
                return null;
            return NormalizeTimePeriod(text[..colonMatch.Index], hour, minute);
        }

        var match = Regex.Match(text, @"(\d{1,2})点(半|一刻|三刻|(\d{1,2}))?(?:分)?");
        if (!match.Success)
            return null;

        var hour2 = int.Parse(match.Groups[1].Value);
        var minuteText = match.Groups[2].Value;
        var minute2 = minuteText switch
        {
            "半" => 30,
            "一刻" => 15,
            "三刻" => 45,
            "" => 0,
            _ => int.TryParse(minuteText, out var minuteValue) ? minuteValue : 0
        };
        if (hour2 > 23 || minute2 > 59)
            return null;
        return NormalizeTimePeriod(text[..match.Index], hour2, minute2);
    }

    private static TimeSpan NormalizeTimePeriod(string prefix, int hour, int minute)
    {
        if (ContainsAny(prefix, "下午", "晚上", "晚", "夜里") && hour < 12)
            hour += 12;
        else if (ContainsAny(prefix, "上午", "早上", "早晨", "清晨") && hour == 12)
            hour = 0;
        else if (ContainsAny(prefix, "中午") && hour < 12)
            hour = 12;
        return new TimeSpan(hour, minute, 0);
    }

    private static string NormalizeChineseDigits(string text)
    {
        var normalized = text
            .Replace("十二点", "12点")
            .Replace("十一点", "11点")
            .Replace("十点", "10点");
        var builder = new StringBuilder(normalized);
        builder
            .Replace("零", "0")
            .Replace("〇", "0")
            .Replace("一", "1")
            .Replace("二", "2")
            .Replace("两", "2")
            .Replace("三", "3")
            .Replace("四", "4")
            .Replace("五", "5")
            .Replace("六", "6")
            .Replace("七", "7")
            .Replace("八", "8")
            .Replace("九", "9");
        return builder.ToString();
    }

    private static bool ContainsAny(string text, params string[] keywords)
        => keywords.Any(text.Contains);

    [GeneratedRegex(@"#([\p{L}\p{N}_\-]+)")]
    private static partial Regex HashTagRegex();
}
