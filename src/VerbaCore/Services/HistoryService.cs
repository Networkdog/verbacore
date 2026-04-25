using System.IO;
using System.Text.Json;
using VerbaCore.Models;

namespace VerbaCore.Services;

public sealed class HistoryService
{
    private static readonly string HistoryDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VerbaCore");
    private static readonly string HistoryPath = Path.Combine(HistoryDir, "history.json");

    private const int MaxItems = 200;
    private LookupHistory _history = new();
    private CancellationTokenSource? _saveDebounceCts;

    public IReadOnlyList<LookupHistoryItem> Items => _history.Items.AsReadOnly();

    public async Task LoadAsync()
    {
        if (!File.Exists(HistoryPath))
        {
            _history = new LookupHistory();
            return;
        }

        try
        {
            var json = await File.ReadAllTextAsync(HistoryPath);
            _history = JsonSerializer.Deserialize(json, HistoryJsonContext.Default.LookupHistory) ?? new LookupHistory();
        }
        catch (System.Text.Json.JsonException)
        {
            // Corrupted history file — start fresh
            _history = new LookupHistory();
        }
    }

    public Task AddAsync(LookupHistoryItem item)
    {
        _history.Items.Insert(0, item);

        // Trim to max
        if (_history.Items.Count > MaxItems)
        {
            _history.Items.RemoveRange(MaxItems, _history.Items.Count - MaxItems);
        }

        // Debounced save — coalesces rapid successive lookups into a single write after 500ms
        QueueSave();
        return Task.CompletedTask;
    }

    public async Task ClearAsync()
    {
        _history.Items.Clear();
        await SaveNowAsync();
    }

    public async Task DeleteAsync(LookupHistoryItem item)
    {
        _history.Items.Remove(item);
        await SaveNowAsync();
    }

    /// <summary>
    /// Debounced save — coalesces rapid successive calls into a single write after 500ms.
    /// Avoids redundant full-file rewrites when multiple lookups happen in quick succession.
    /// </summary>
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
        Directory.CreateDirectory(HistoryDir);
        var json = JsonSerializer.Serialize(_history, HistoryJsonContext.Default.LookupHistory);
        await File.WriteAllTextAsync(HistoryPath, json);
    }
}
