using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VerbaCore.Models;
using VerbaCore.Services;

namespace VerbaCore.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly SettingsService _settingsService;

    [ObservableProperty]
    private string _selectedProvider = "OpenAI";

    [ObservableProperty]
    private string _apiKey = string.Empty;

    [ObservableProperty]
    private string _selectedModel = "gpt-4o-mini";

    [ObservableProperty]
    private string _azureEndpoint = string.Empty;

    [ObservableProperty]
    private string _azureDeploymentName = string.Empty;

    [ObservableProperty]
    private string _azureApiVersion = "2024-10-21";

    [ObservableProperty]
    private bool _isAzure;

    [ObservableProperty]
    private bool _clipboardMonitorEnabled = true;

    [ObservableProperty]
    private bool _startWithWindows;

    [ObservableProperty]
    private string _selectedTheme = "System";

    [ObservableProperty]
    private string _globalHotkey = "Ctrl+Alt+V";

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    public string[] AvailableProviders { get; } = ["OpenAI", "Azure OpenAI"];
    public string[] AvailableModels { get; } = ["gpt-4o-mini", "gpt-4o", "gpt-4-turbo"];
    public string[] AvailableThemes { get; } = ["System", "Light", "Dark"];

    public SettingsViewModel(SettingsService settingsService)
    {
        _settingsService = settingsService;
        LoadFromSettings();
    }

    private void LoadFromSettings()
    {
        var s = _settingsService.Current;
        SelectedProvider = s.Provider == ApiProvider.AzureOpenAI ? "Azure OpenAI" : "OpenAI";
        IsAzure = s.Provider == ApiProvider.AzureOpenAI;
        ApiKey = s.ApiKey;
        SelectedModel = s.Model;
        AzureEndpoint = s.AzureEndpoint;
        AzureDeploymentName = s.AzureDeploymentName;
        AzureApiVersion = s.AzureApiVersion;
        ClipboardMonitorEnabled = s.ClipboardMonitorEnabled;
        StartWithWindows = s.StartWithWindows;
        GlobalHotkey = s.GlobalHotkey;
        SelectedTheme = s.Theme switch
        {
            ThemeMode.Light => "Light",
            ThemeMode.Dark => "Dark",
            _ => "System"
        };
    }

    partial void OnSelectedProviderChanged(string value)
    {
        IsAzure = value == "Azure OpenAI";
    }

    [RelayCommand]
    private async Task SaveSettingsAsync()
    {
        var s = _settingsService.Current;
        s.Provider = SelectedProvider == "Azure OpenAI" ? ApiProvider.AzureOpenAI : ApiProvider.OpenAI;
        s.ApiKey = ApiKey;
        s.Model = SelectedModel;
        s.AzureEndpoint = AzureEndpoint;
        s.AzureDeploymentName = AzureDeploymentName;
        s.AzureApiVersion = AzureApiVersion;
        s.ClipboardMonitorEnabled = ClipboardMonitorEnabled;
        s.StartWithWindows = StartWithWindows;
        s.GlobalHotkey = GlobalHotkey;
        s.Theme = SelectedTheme switch
        {
            "Light" => ThemeMode.Light,
            "Dark" => ThemeMode.Dark,
            _ => ThemeMode.System
        };

        await _settingsService.SaveAsync();
        StatusMessage = "설정이 저장되었습니다.";

        // Auto-clear status
        await Task.Delay(2000);
        StatusMessage = string.Empty;
    }
}
