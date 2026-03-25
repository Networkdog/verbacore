namespace VerbaCore.Models;

public sealed class LookupResult
{
    public string Input { get; set; } = string.Empty;
    public LookupMode Mode { get; set; }
    public string Response { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public string SourceLanguage { get; set; } = string.Empty;
    public string TargetLanguage { get; set; } = string.Empty;
    public bool IsStreaming { get; set; }
}

public enum LookupMode
{
    Dictionary,
    Translate
}
