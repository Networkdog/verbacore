using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VerbaCore.Models;
using VerbaCore.Services;

namespace VerbaCore.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly SettingsService _settingsService;
    private readonly LocalizationService _localizationService;

    private static string Loc(string key) =>
        Application.Current.TryFindResource(key) as string ?? key;

    private static readonly (OverlayPosition Enum, string Key)[] PositionEntries =
    [
        (OverlayPosition.TopLeft, "Position_TopLeft"),
        (OverlayPosition.TopCenter, "Position_TopCenter"),
        (OverlayPosition.TopRight, "Position_TopRight"),
        (OverlayPosition.CenterLeft, "Position_CenterLeft"),
        (OverlayPosition.CenterCenter, "Position_Center"),
        (OverlayPosition.CenterRight, "Position_CenterRight"),
        (OverlayPosition.BottomLeft, "Position_BottomLeft"),
        (OverlayPosition.BottomCenter, "Position_BottomCenter"),
        (OverlayPosition.BottomRight, "Position_BottomRight"),
    ];

    private static readonly (OverlaySize Enum, string Key)[] SizeEntries =
    [
        (Models.OverlaySize.Small, "Size_Small"),
        (Models.OverlaySize.Medium, "Size_Medium"),
        (Models.OverlaySize.Large, "Size_Large"),
    ];

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
    private string _popupPosition = string.Empty;

    [ObservableProperty]
    private string _overlaySize = string.Empty;

    [ObservableProperty]
    private string _selectedUiLanguage = "한국어";

    [ObservableProperty]
    private string _globalHotkey = "Ctrl+Alt+V";

    [ObservableProperty]
    private string _nativeLanguage = "Korean";

    [ObservableProperty]
    private string _foreignLanguage = "English";

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    public ObservableCollection<string> AvailableModels { get; } = new();
    public ObservableCollection<string> AvailablePositions { get; } = new();
    public ObservableCollection<string> AvailableSizes { get; } = new();
    public string[] AvailableProviders { get; } = ["OpenAI", "Anthropic", "Google Gemini", "OpenRouter", "Azure OpenAI", "Custom"];
    public string[] AvailableReasoningEfforts { get; } = ["none", "minimal", "low", "medium", "high", "xhigh"];
    public string[] AvailableThemes { get; } = ["System", "Light", "Dark"];
    public string[] AvailableUiLanguages { get; } = ["한국어", "English", "中文", "日本語"];
    public string[] AvailableLanguages { get; } =
    [
        "Korean", "English", "Japanese", "Chinese", "Spanish", "French",
        "German", "Portuguese", "Russian", "Arabic", "Italian", "Dutch",
        "Vietnamese", "Thai", "Indonesian", "Hindi", "Turkish", "Polish",
        "Swedish", "Czech"
    ];

    public SettingsViewModel(SettingsService settingsService, LocalizationService localizationService)
    {
        _settingsService = settingsService;
        _localizationService = localizationService;
        _localizationService.LanguageChanged += OnLanguageChanged;
        LoadFromSettings();
    }

    private void OnLanguageChanged()
    {
        RefreshLocalizedCollections();
    }

    private void RefreshLocalizedCollections()
    {
        var currentPosEnum = PositionEntries
            .FirstOrDefault(e => e.Key == PositionEntries
                .FirstOrDefault(p => Loc(p.Key) == PopupPosition).Key).Enum;
        var currentSizeEnum = SizeEntries
            .FirstOrDefault(e => e.Key == SizeEntries
                .FirstOrDefault(s => Loc(s.Key) == OverlaySize).Key).Enum;

        // Rebuild with new language
        AvailablePositions.Clear();
        foreach (var e in PositionEntries) AvailablePositions.Add(Loc(e.Key));

        AvailableSizes.Clear();
        foreach (var e in SizeEntries) AvailableSizes.Add(Loc(e.Key));

        // Re-select with new display names
        PopupPosition = Loc(PositionEntries.First(e => e.Enum == currentPosEnum).Key);
        OverlaySize = Loc(SizeEntries.First(e => e.Enum == currentSizeEnum).Key);
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
        AzureApiVersion = s.AzureApiVersion;
        CustomEndpoint = s.CustomEndpoint;
        StartWithWindows = s.StartWithWindows;
        GlobalHotkey = s.GlobalHotkey;
        NativeLanguage = s.NativeLanguage;
        ForeignLanguage = s.ForeignLanguage;
        SelectedTheme = s.Theme switch
        {
            ThemeMode.Light => "Light",
            ThemeMode.Dark => "Dark",
            _ => "System"
        };
        PopupPosition = Loc(PositionEntries.First(e => e.Enum == s.PopupPosition).Key);
        OverlaySize = Loc(SizeEntries.First(e => e.Enum == s.OverlaySize).Key);
        SelectedUiLanguage = s.UiLanguage switch
        {
            Models.UiLanguage.English => "English",
            Models.UiLanguage.Chinese => "中文",
            Models.UiLanguage.Japanese => "日本語",
            _ => "한국어"
        };

        // Populate localized collections
        AvailablePositions.Clear();
        foreach (var e in PositionEntries) AvailablePositions.Add(Loc(e.Key));
        AvailableSizes.Clear();
        foreach (var e in SizeEntries) AvailableSizes.Add(Loc(e.Key));

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
        s.AzureApiVersion = AzureApiVersion;
        s.CustomEndpoint = CustomEndpoint;
        s.StartWithWindows = StartWithWindows;
        s.GlobalHotkey = GlobalHotkey;
        s.NativeLanguage = NativeLanguage;
        s.ForeignLanguage = ForeignLanguage;
        s.Theme = SelectedTheme switch
        {
            "Light" => ThemeMode.Light,
            "Dark" => ThemeMode.Dark,
            _ => ThemeMode.System
        };
        s.PopupPosition = PositionEntries
            .FirstOrDefault(e => Loc(e.Key) == PopupPosition).Enum;
        s.OverlaySize = SizeEntries
            .FirstOrDefault(e => Loc(e.Key) == OverlaySize).Enum;
        s.UiLanguage = SelectedUiLanguage switch
        {
            "English" => Models.UiLanguage.English,
            "中文" => Models.UiLanguage.Chinese,
            "日本語" => Models.UiLanguage.Japanese,
            _ => Models.UiLanguage.Korean
        };

        await _settingsService.SaveAsync();

        // Apply changes live
        App.ApplyTheme(s.Theme);
        App.ApplyStartWithWindows(s.StartWithWindows);
        _localizationService.Apply(s.UiLanguage);

        // Re-register hotkey if changed
        try
        {
            var hotkeyService = App.GetService<HotkeyService>();
            hotkeyService.Register(s.GlobalHotkey);
        }
        catch { /* hotkey may fail if already in use */ }

        StatusMessage = Loc("Settings_SavedMessage");

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
