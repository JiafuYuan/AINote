namespace AINote.Models;

public sealed class AiAnalysisResult
{
    public string Category { get; set; } = "未分类";
    public List<string> Tags { get; set; } = new();
    public int Stars { get; set; } = 3;
    public string Summary { get; set; } = "";
    public bool UsedLocalFallback { get; set; }
}
