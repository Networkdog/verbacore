namespace VerbaCore.Models;

public sealed class LookupHistory
{
    public List<LookupHistoryItem> Items { get; set; } = [];
}

public sealed class LookupHistoryItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Input { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.Now;
}
