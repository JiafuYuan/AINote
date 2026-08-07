using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AINote.Models;

namespace AINote.Services;

public sealed partial class AiAnalyzer
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(45) };

    public async Task<AiAnalysisResult> AnalyzeAsync(string input, AppSettings settings, CancellationToken ct = default)
    {
        if (!settings.AiEnabled || string.IsNullOrWhiteSpace(settings.ApiKey) || string.IsNullOrWhiteSpace(input))
            return AnalyzeLocally(input);

        try
        {
            return await AnalyzeWithAiAsync(input, settings, ct);
        }
        catch
        {
            var local = AnalyzeLocally(input);
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
            await AnalyzeWithAiAsync("你好，请回复连接正常。", settings, ct);
            return $"连接成功，模型：{settings.Model}";
        }
        catch (Exception ex)
        {
            return $"连接失败：{ex.Message}";
        }
    }

    private async Task<AiAnalysisResult> AnalyzeWithAiAsync(string input, AppSettings settings, CancellationToken ct)
    {
        const string systemPrompt =
            "你是中文笔记整理助手。根据用户输入的笔记内容，只返回一个 JSON 对象，不要输出解释或 Markdown。" +
            "JSON 格式：{\"category\":\"分类\",\"tags\":[\"标签\"],\"stars\":1,\"summary\":\"一句话摘要\"}。" +
            "分类只能从：工作、学习、生活、健康、购物、旅行、灵感、其他 中选择。stars 是 1 到 5 的整数。";

        var payload = new
        {
            model = settings.Model,
            temperature = settings.Temperature,
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = input },
            },
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, BuildUrl(settings.ApiBaseUrl));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
        request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        using var response = await _http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(ct);
        return ParseAiJson(json) ?? AnalyzeLocally(input);
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

            return new AiAnalysisResult
            {
                Category = category,
                Tags = tags,
                Stars = stars,
                Summary = GetString(root, "summary"),
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

    private AiAnalysisResult AnalyzeLocally(string input)
    {
        var text = input.Trim();
        var category = GuessCategory(text);
        var tags = ExtractTags(text, category);
        var stars = GuessStars(text);
        var summary = BuildSummary(text);

        return new AiAnalysisResult
        {
            Category = category,
            Tags = tags,
            Stars = stars,
            Summary = summary,
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
        if (ContainsAny(text, "旅行", "机票", "酒店", "景点", "行程", "旅游"))
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

    private static bool ContainsAny(string text, params string[] keywords)
        => keywords.Any(text.Contains);

    [GeneratedRegex(@"#([\p{L}\p{N}_\-]+)")]
    private static partial Regex HashTagRegex();
}
