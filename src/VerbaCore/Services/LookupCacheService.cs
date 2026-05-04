using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using VerbaCore.Models;

namespace VerbaCore.Services;

/// <summary>
/// 2-tier (in-memory + JSON) cache for LLM lookup results. Mirrors HistoryService persistence pattern.
/// - Lazy TTL expiration on read
/// - LRU eviction by LastAccessUtc when MaxEntries exceeded
/// - 500ms debounced save to %AppData%\VerbaCore\cache.json
/// </summary>
public sealed class LookupCacheService
{
    private static readonly string CacheDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VerbaCore");
    private static readonly string CachePath = Path.Combine(CacheDir, "cache.json");

    private const int MaxEntries = 500;

    private readonly SettingsService _settingsService;
    private LookupCache _cache = new();
    private CancellationTokenSource? _saveDebounceCts;
    private readonly object _lock = new();

    public LookupCacheService(SettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    public int Count
    {
        get { lock (_lock) { return _cache.Entries.Count; } }
    }

    public async Task LoadAsync()
    {
        if (!File.Exists(CachePath))
        {
            _cache = new LookupCache();
            return;
        }
        try
        {
            var json = await File.ReadAllTextAsync(CachePath);
            _cache = JsonSerializer.Deserialize(json, CacheJsonContext.Default.LookupCache) ?? new LookupCache();
        }
        catch (JsonException)
        {
            _cache = new LookupCache();
        }
    }

    public bool TryGet(string key, out string response)
    {
        response = string.Empty;
        lock (_lock)
        {
            if (!_cache.Entries.TryGetValue(key, out var entry))
                return false;

            // Lazy TTL expiration
            var ttlDays = Math.Max(1, _settingsService.Current.CacheTtlDays);
            if (DateTime.UtcNow - entry.CreatedUtc > TimeSpan.FromDays(ttlDays))
            {
                _cache.Entries.Remove(key);
                QueueSave();
                return false;
            }

            entry.LastAccessUtc = DateTime.UtcNow;
            entry.HitCount++;
            response = entry.Response;
            QueueSave();
            return true;
        }
    }

    public void Put(string key, string response)
    {
        if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(response))
            return;

        lock (_lock)
        {
            var now = DateTime.UtcNow;
            _cache.Entries[key] = new CacheEntry
            {
                Response = response,
                CreatedUtc = now,
                LastAccessUtc = now,
                HitCount = 0
            };
            EvictIfNeeded();
            QueueSave();
        }
    }

    public async Task ClearAsync()
    {
        lock (_lock)
        {
            _cache.Entries.Clear();
        }
        await SaveNowAsync();
    }

    /// <summary>
    /// LRU eviction: remove oldest LastAccessUtc entries until under MaxEntries.
    /// Also purges expired entries opportunistically.
    /// </summary>
    private void EvictIfNeeded()
    {
        var ttlDays = Math.Max(1, _settingsService.Current.CacheTtlDays);
        var now = DateTime.UtcNow;
        var ttlSpan = TimeSpan.FromDays(ttlDays);

        // Purge expired
        var expiredKeys = _cache.Entries
            .Where(kv => now - kv.Value.CreatedUtc > ttlSpan)
            .Select(kv => kv.Key)
            .ToList();
        foreach (var k in expiredKeys) _cache.Entries.Remove(k);

        // LRU prune
        if (_cache.Entries.Count <= MaxEntries) return;
        var excess = _cache.Entries.Count - MaxEntries;
        var toRemove = _cache.Entries
            .OrderBy(kv => kv.Value.LastAccessUtc)
            .Take(excess)
            .Select(kv => kv.Key)
            .ToList();
        foreach (var k in toRemove) _cache.Entries.Remove(k);
    }

    private void QueueSave()
    {
        _saveDebounceCts?.Cancel();
        _saveDebounceCts = new CancellationTokenSource();
        var ct = _saveDebounceCts.Token;
        _ = Task.Delay(500, ct).ContinueWith(async _ =>
        {
            if (!ct.IsCancellationRequested)
                await SaveNowAsync();
        }, ct, TaskContinuationOptions.OnlyOnRanToCompletion, TaskScheduler.Default);
    }

    private async Task SaveNowAsync()
    {
        Directory.CreateDirectory(CacheDir);
        string json;
        lock (_lock)
        {
            json = JsonSerializer.Serialize(_cache, CacheJsonContext.Default.LookupCache);
        }
        await File.WriteAllTextAsync(CachePath, json);
    }

    /// <summary>
    /// Build a stable cache key from request parameters. Dictionary mode is case-insensitive
    /// (single-word lookups should hit regardless of capitalization); other modes preserve case
    /// because translations/assists can be sensitive to it.
    /// </summary>
    public static string MakeKey(string provider, string model, LookupMode mode, string src, string tgt, string input)
    {
        var normalized = (input ?? string.Empty).Trim();
        if (mode == LookupMode.Dictionary)
            normalized = normalized.ToLowerInvariant();

        var raw = $"{provider}|{model}|{mode}|{src}|{tgt}|{normalized}";
        var bytes = Encoding.UTF8.GetBytes(raw);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }
}
