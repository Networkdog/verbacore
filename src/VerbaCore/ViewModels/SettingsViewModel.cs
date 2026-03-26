using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VerbaCore.Models;
using VerbaCore.Services;

namespace VerbaCore.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly SettingsService _settingsService;

    private static readonly Dictionary<string, string[]> ModelCatalog = new()
    {
        ["OpenAI"] =
        [
            "gpt-4o-mini", "gpt-4o", "gpt-4.1-mini", "gpt-4.1", "gpt-4.1-nano",
            "o1-mini", "o1", "o3-mini", "o3", "o4-mini",
            "gpt-4-turbo", "gpt-3.5-turbo"
        ],
        ["Anthropic"] =
        [
            "claude-sonnet-4-20250514", "claude-3-7-sonnet-20250219",
            "claude-3-5-haiku-20241022",
            "claude-3-5-sonnet-20241022", "claude-3-opus-20240229"
        ],
        ["Google Gemini"] =
        [
            "gemini-2.5-pro", "gemini-2.5-flash",
            "gemini-2.0-flash", "gemini-2.0-flash-lite",
            "gemini-1.5-pro", "gemini-1.5-flash"
        ],
        ["OpenRouter"] =
        [
            "openai/gpt-4o-mini", "openai/gpt-4o",
            "anthropic/claude-sonnet-4-20250514", "anthropic/claude-3.5-sonnet",
            "google/gemini-2.5-pro", "google/gemini-2.5-flash",
            "meta-llama/llama-4-maverick",
            "deepseek/deepseek-r1", "deepseek/deepseek-chat-v3-0324",
            "qwen/qwen3-235b-a22b"
        ],
        ["Azure OpenAI"] = [],
        ["Custom"] = []
    };

    [ObservableProperty]
    private string _selectedProvider = "OpenAI";

    [ObservableProperty]
    private string _apiKey = string.Empty;

    [ObservableProperty]
    private string _selectedModel = "gpt-4o-mini";

    [ObservableProperty]
    private string _reasoningEffort = "none";

    [ObservableProperty]
    private string _azureEndpoint = string.Empty;

    [ObservableProperty]
    private string _azureDeploymentName = string.Empty;

    [ObservableProperty]
    private string _azureApiVersion = "2024-10-21";

    [ObservableProperty]
    private string _customEndpoint = string.Empty;

    [ObservableProperty]
    private bool _isAzure;

    [ObservableProperty]
    private bool _isCustom;

    [ObservableProperty]
    private bool _hasModelCatalog = true;

    [ObservableProperty]
    private bool _startWithWindows;

    [ObservableProperty]
    private string _selectedTheme = "System";

    [ObservableProperty]
    private string _popupPosition = "가운데";

    [ObservableProperty]
    private string _globalHotkey = "Ctrl+Alt+V";

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    public ObservableCollection<string> AvailableModels { get; } = new();
    public string[] AvailableProviders { get; } = ["OpenAI", "Anthropic", "Google Gemini", "OpenRouter", "Azure OpenAI", "Custom"];
    public string[] AvailableReasoningEfforts { get; } = ["none", "minimal", "low", "medium", "high", "xhigh"];
    public string[] AvailableThemes { get; } = ["System", "Light", "Dark"];
    public string[] AvailablePositions { get; } =
    [
        "왼쪽 위", "위 가운데", "오른쪽 위",
        "왼쪽 가운데", "가운데", "오른쪽 가운데",
        "왼쪽 아래", "아래 가운데", "오른쪽 아래"
    ];

    public SettingsViewModel(SettingsService settingsService)
    {
        _settingsService = settingsService;
        LoadFromSettings();
    }

    private static string ProviderToString(ApiProvider p) => p switch
    {
        ApiProvider.AzureOpenAI => "Azure OpenAI",
        ApiProvider.Anthropic => "Anthropic",
        ApiProvider.Google => "Google Gemini",
        ApiProvider.OpenRouter => "OpenRouter",
        ApiProvider.Custom => "Custom",
        _ => "OpenAI"
    };

    private static ApiProvider StringToProvider(string s) => s switch
    {
        "Azure OpenAI" => ApiProvider.AzureOpenAI,
        "Anthropic" => ApiProvider.Anthropic,
        "Google Gemini" => ApiProvider.Google,
        "OpenRouter" => ApiProvider.OpenRouter,
        "Custom" => ApiProvider.Custom,
        _ => ApiProvider.OpenAI
    };

    private void LoadFromSettings()
    {
        var s = _settingsService.Current;
        SelectedProvider = ProviderToString(s.Provider);
        IsAzure = s.Provider == ApiProvider.AzureOpenAI;
        IsCustom = s.Provider == ApiProvider.Custom;
        ApiKey = s.ApiKey;
        SelectedModel = s.Model;
        ReasoningEffort = s.ReasoningEffort;
        AzureEndpoint = s.AzureEndpoint;
        AzureDeploymentName = s.AzureDeploymentName;
        AzureApiVersion = s.AzureApiVersion;
        CustomEndpoint = s.CustomEndpoint;
        StartWithWindows = s.StartWithWindows;
        GlobalHotkey = s.GlobalHotkey;
        SelectedTheme = s.Theme switch
        {
            ThemeMode.Light => "Light",
            ThemeMode.Dark => "Dark",
            _ => "System"
        };
        PopupPosition = s.PopupPosition switch
        {
            OverlayPosition.TopLeft => "왼쪽 위",
            OverlayPosition.TopCenter => "위 가운데",
            OverlayPosition.TopRight => "오른쪽 위",
            OverlayPosition.CenterLeft => "왼쪽 가운데",
            OverlayPosition.CenterRight => "오른쪽 가운데",
            OverlayPosition.BottomLeft => "왼쪽 아래",
            OverlayPosition.BottomCenter => "아래 가운데",
            OverlayPosition.BottomRight => "오른쪽 아래",
            _ => "가운데"
        };
        UpdateAvailableModels();
    }

    partial void OnSelectedProviderChanged(string value)
    {
        IsAzure = value == "Azure OpenAI";
        IsCustom = value == "Custom";
        HasModelCatalog = ModelCatalog.TryGetValue(value, out var models) && models.Length > 0;
        UpdateAvailableModels();
    }

    private void UpdateAvailableModels()
    {
        AvailableModels.Clear();
        if (ModelCatalog.TryGetValue(SelectedProvider, out var models))
        {
            foreach (var m in models) AvailableModels.Add(m);
        }
        // Keep current model if it's in the list, or if it's a free-text entry (Azure/Custom)
        if (AvailableModels.Count > 0 && !AvailableModels.Contains(SelectedModel))
        {
            SelectedModel = AvailableModels[0];
        }
    }

    private CancellationTokenSource? _statusClearCts;

    [RelayCommand]
    private async Task SaveSettingsAsync()
    {
        var s = _settingsService.Current;
        s.Provider = StringToProvider(SelectedProvider);
        s.ApiKey = ApiKey;
        s.Model = SelectedModel;
        s.ReasoningEffort = ReasoningEffort;
        s.AzureEndpoint = AzureEndpoint;
        s.AzureDeploymentName = AzureDeploymentName;
        s.AzureApiVersion = AzureApiVersion;
        s.CustomEndpoint = CustomEndpoint;
        s.StartWithWindows = StartWithWindows;
        s.GlobalHotkey = GlobalHotkey;
        s.Theme = SelectedTheme switch
        {
            "Light" => ThemeMode.Light,
            "Dark" => ThemeMode.Dark,
            _ => ThemeMode.System
        };
        s.PopupPosition = PopupPosition switch
        {
            "왼쪽 위" => OverlayPosition.TopLeft,
            "위 가운데" => OverlayPosition.TopCenter,
            "오른쪽 위" => OverlayPosition.TopRight,
            "왼쪽 가운데" => OverlayPosition.CenterLeft,
            "오른쪽 가운데" => OverlayPosition.CenterRight,
            "왼쪽 아래" => OverlayPosition.BottomLeft,
            "아래 가운데" => OverlayPosition.BottomCenter,
            "오른쪽 아래" => OverlayPosition.BottomRight,
            _ => OverlayPosition.CenterCenter
        };

        await _settingsService.SaveAsync();

        // Apply changes live
        App.ApplyTheme(s.Theme);
        App.ApplyStartWithWindows(s.StartWithWindows);

        // Re-register hotkey if changed
        try
        {
            var hotkeyService = App.GetService<HotkeyService>();
            hotkeyService.Register(s.GlobalHotkey);
        }
        catch { /* hotkey may fail if already in use */ }

        StatusMessage = "설정이 저장되었습니다.";

        // Auto-clear status (cancel any previous clear timer)
        _statusClearCts?.Cancel();
        _statusClearCts = new CancellationTokenSource();
        try
        {
            await Task.Delay(2000, _statusClearCts.Token);
            StatusMessage = string.Empty;
        }
        catch (OperationCanceledException) { }
    }
}
