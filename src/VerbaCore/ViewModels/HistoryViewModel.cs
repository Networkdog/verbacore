using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VerbaCore.Models;
using VerbaCore.Services;

namespace VerbaCore.ViewModels;

public partial class HistoryViewModel : ObservableObject
{
    private readonly HistoryService _historyService;
    private readonly Action<string, LookupMode> _onRequery;

    [ObservableProperty]
    private ObservableCollection<LookupHistoryItem> _historyItems = [];

    [ObservableProperty]
    private string _searchFilter = string.Empty;

    public HistoryViewModel(HistoryService historyService, Action<string, LookupMode> onRequery)
    {
        _historyService = historyService;
        _onRequery = onRequery;
        RefreshItems();
    }

    public void RefreshItems()
    {
        var items = string.IsNullOrEmpty(SearchFilter)
            ? _historyService.Items
            : _historyService.Items
                .Where(i => i.Input.Contains(SearchFilter, StringComparison.OrdinalIgnoreCase))
                .ToList();

        HistoryItems = new ObservableCollection<LookupHistoryItem>(items);
    }

    [RelayCommand]
    private void Requery(LookupHistoryItem? item)
    {
        if (item == null) return;
        _onRequery(item.Input, item.Mode);
    }

    [RelayCommand]
    private async Task ClearHistoryAsync()
    {
        await _historyService.ClearAsync();
        HistoryItems.Clear();
    }

    partial void OnSearchFilterChanged(string value)
    {
        RefreshItems();
    }
}
