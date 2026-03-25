using System.IO;
using System.Text.Json;
using VerbaCore.Models;

namespace VerbaCore.Services;

public sealed class HistoryService
{
    private static readonly string HistoryDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VerbaCore");
    private static readonly string HistoryPath = Path.Combine(HistoryDir, "history.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private const int MaxItems = 200;
    private LookupHistory _history = new();

    public IReadOnlyList<LookupHistoryItem> Items => _history.Items.AsReadOnly();

    public async Task LoadAsync()
    {
        if (!File.Exists(HistoryPath))
        {
            _history = new LookupHistory();
            return;
        }

        var json = await File.ReadAllTextAsync(HistoryPath);
        _history = JsonSerializer.Deserialize<LookupHistory>(json, JsonOptions) ?? new LookupHistory();
    }

    public async Task AddAsync(LookupHistoryItem item)
    {
        _history.Items.Insert(0, item);

        // Trim to max
        if (_history.Items.Count > MaxItems)
        {
            _history.Items.RemoveRange(MaxItems, _history.Items.Count - MaxItems);
        }

        await SaveAsync();
    }

    public async Task ClearAsync()
    {
        _history.Items.Clear();
        await SaveAsync();
    }

    public async Task DeleteAsync(LookupHistoryItem item)
    {
        _history.Items.Remove(item);
        await SaveAsync();
    }

    private async Task SaveAsync()
    {
        Directory.CreateDirectory(HistoryDir);
        var json = JsonSerializer.Serialize(_history, JsonOptions);
        await File.WriteAllTextAsync(HistoryPath, json);
    }
}
