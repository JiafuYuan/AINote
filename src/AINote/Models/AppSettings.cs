namespace AINote.Models;

public sealed class AppSettings
{
    public bool AiEnabled { get; set; }
    public bool AiAutoAnalyze { get; set; } = true;
    public string ApiBaseUrl { get; set; } = "https://api.openai.com/v1";
    public string ApiKey { get; set; } = "";
    public string Model { get; set; } = "gpt-4o-mini";
    public double Temperature { get; set; } = 0.2;
}
