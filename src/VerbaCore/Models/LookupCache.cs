namespace VerbaCore.Models;

/// <summary>
/// Persisted cache of LLM lookup results keyed by a hash of (provider, model, mode, src, tgt, input).
/// Avoids redundant token spend on repeat lookups within the configured TTL.
/// </summary>
public sealed class LookupCache
{
    public Dictionary<string, CacheEntry> Entries { get; set; } = new();
}

public sealed class CacheEntry
{
    public string Response { get; set; } = string.Empty;
    public DateTime CreatedUtc { get; set; }
    public DateTime LastAccessUtc { get; set; }
    public int HitCount { get; set; }
}
