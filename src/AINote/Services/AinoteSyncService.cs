using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AINote.Models;

namespace AINote.Services;

public sealed class AinoteSyncService
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(25) };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public async Task<List<NoteItem>> GetNotesAsync(string baseUrl, CancellationToken ct = default)
    {
        using var response = await _http.GetAsync(BuildUrl(baseUrl, "notes"), ct);
        return await ReadDataAsync<List<NoteItem>>(response, ct) ?? new List<NoteItem>();
    }

    public async Task UpsertAsync(string baseUrl, NoteItem note, CancellationToken ct = default)
    {
        using var content = new StringContent(
            JsonSerializer.Serialize(ToPayload(note), JsonOptions),
            Encoding.UTF8,
            "application/json");
        using var response = await _http.PostAsync(BuildUrl(baseUrl, "notes"), content, ct);
        await ReadDataAsync<object>(response, ct);
    }

    public async Task<AiAnalysisResult> AnalyzeAsync(
        string baseUrl,
        string title,
        string content,
        CancellationToken ct = default)
    {
        var payload = new { title = title ?? string.Empty, content = content ?? string.Empty };
        using var requestContent = new StringContent(
            JsonSerializer.Serialize(payload, JsonOptions),
            Encoding.UTF8,
            "application/json");
        using var response = await _http.PostAsync(BuildUrl(baseUrl, "analyze"), requestContent, ct);
        var result = await ReadDataAsync<ServerAnalyzeResult>(response, ct);

        return new AiAnalysisResult
        {
            Title = result?.Title ?? string.Empty,
            Category = string.IsNullOrWhiteSpace(result?.Category) ? "其他" : result.Category,
            Tags = result?.Tags ?? new List<string>(),
            Stars = Math.Clamp(result?.Stars ?? 3, 1, 5),
            Summary = result?.Summary ?? string.Empty,
            DueDate = result?.DueDate,
            UsedLocalFallback = result?.UsedLocalFallback ?? true
        };
    }

    public async Task BatchUpsertAsync(string baseUrl, IEnumerable<NoteItem> notes, CancellationToken ct = default)
    {
        var payload = notes.Select(ToPayload).ToList();
        using var content = new StringContent(
            JsonSerializer.Serialize(payload, JsonOptions),
            Encoding.UTF8,
            "application/json");
        using var response = await _http.PostAsync(BuildUrl(baseUrl, "notes/batch"), content, ct);
        await ReadDataAsync<object>(response, ct);
    }

    public async Task DeleteAsync(string baseUrl, string id, CancellationToken ct = default)
    {
        using var response = await _http.DeleteAsync(BuildUrl(baseUrl, $"notes/{id}"), ct);
        await ReadDataAsync<object>(response, ct);
    }

    private static string BuildUrl(string baseUrl, string suffix)
    {
        var root = (baseUrl ?? string.Empty).Trim().TrimEnd('/');
        return $"{root}/api/app/ainote/{suffix}";
    }

    private static AiNotePayload ToPayload(NoteItem note)
    {
        return new AiNotePayload
        {
            Id = note.Id,
            Title = note.Title,
            Content = note.Content,
            Summary = note.Summary,
            Category = string.IsNullOrWhiteSpace(note.Category) ? "未分类" : note.Category,
            Tags = note.Tags,
            Stars = Math.Clamp(note.Stars, 0, 5),
            DueDate = note.DueDate,
            CreatedAt = ToUtc(note.CreatedAt),
            UpdatedAt = ToUtc(note.UpdatedAt)
        };
    }

    private static async Task<T?> ReadDataAsync<T>(HttpResponseMessage response, CancellationToken ct)
        where T : class
    {
        var text = await response.Content.ReadAsStringAsync(ct);
        ApiResult<object>? errorResult = null;
        try
        {
            errorResult = JsonSerializer.Deserialize<ApiResult<object>>(text, JsonOptions);
        }
        catch (JsonException)
        {
            // Non-JSON error body; the status code below is enough.
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(GetErrorText(errorResult, (int)response.StatusCode));
        }

        // 尝试解析包装格式 { "succeeded": true, "data": {...} }
        try
        {
            var result = JsonSerializer.Deserialize<ApiResult<T>>(text, JsonOptions);
            if (result is not null && result.Succeeded && result.Data is not null)
            {
                return result.Data;
            }
        }
        catch (JsonException)
        {
            // Not a valid ApiResult wrapper, fall through to raw format.
        }

        // 尝试解析原始格式，后端可能直接返回数据对象
        try
        {
            var raw = JsonSerializer.Deserialize<T>(text, JsonOptions);
            if (raw is not null)
            {
                return raw;
            }
        }
        catch (JsonException)
        {
            // Not a valid T either.
        }

        throw new InvalidOperationException("后台返回的数据格式不正确");
    }

    private static string GetErrorText(ApiResult<object>? result, int statusCode)
    {
        if (result?.Errors is { Count: > 0 })
        {
            return string.Join("；", result.Errors);
        }

        if (!string.IsNullOrWhiteSpace(result?.Message))
        {
            return result.Message;
        }

        return $"服务器返回 {statusCode}";
    }

    private static DateTime ToUtc(DateTime value)
    {
        return value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Local).ToUniversalTime()
        };
    }

    private sealed class AiNotePayload
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("title")]
        public string Title { get; set; } = string.Empty;

        [JsonPropertyName("content")]
        public string Content { get; set; } = string.Empty;

        [JsonPropertyName("summary")]
        public string Summary { get; set; } = string.Empty;

        [JsonPropertyName("category")]
        public string Category { get; set; } = "未分类";

        [JsonPropertyName("tags")]
        public List<string> Tags { get; set; } = new();

        [JsonPropertyName("stars")]
        public int Stars { get; set; }

        [JsonPropertyName("dueDate")]
        public DateTime? DueDate { get; set; }

        [JsonPropertyName("createdAt")]
        public DateTime CreatedAt { get; set; }

        [JsonPropertyName("updatedAt")]
        public DateTime UpdatedAt { get; set; }
    }

    private sealed class ServerAnalyzeResult
    {
        [JsonPropertyName("title")]
        public string Title { get; set; } = string.Empty;

        [JsonPropertyName("category")]
        public string Category { get; set; } = string.Empty;

        [JsonPropertyName("tags")]
        public List<string> Tags { get; set; } = new();

        [JsonPropertyName("stars")]
        public int Stars { get; set; } = 3;

        [JsonPropertyName("summary")]
        public string Summary { get; set; } = string.Empty;

        [JsonPropertyName("dueDate")]
        public DateTime? DueDate { get; set; }

        [JsonPropertyName("usedLocalFallback")]
        public bool UsedLocalFallback { get; set; }
    }

    private sealed class ApiResult<T>
    {
        [JsonPropertyName("statusCode")]
        public int StatusCode { get; set; }

        [JsonPropertyName("data")]
        public T? Data { get; set; }

        [JsonPropertyName("succeeded")]
        public bool Succeeded { get; set; }

        [JsonPropertyName("errors")]
        public List<string>? Errors { get; set; }

        [JsonPropertyName("message")]
        public string? Message { get; set; }

        [JsonPropertyName("timestamp")]
        public DateTime Timestamp { get; set; }
    }
}
