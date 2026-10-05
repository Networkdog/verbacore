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
    private readonly LookupCacheService _cacheService;
    private bool _loadingSettings = true;

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
        ["Microsoft Foundry"] = [],
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
    private InferenceProtocol _protocol;

    [ObservableProperty]
    private ReasoningMode _selectedReasoningMode;

    [ObservableProperty]
    private InstructionRole _selectedInstructionRole;

    [ObservableProperty]
    private OutputTokenParameter _tokenLimitParameter;

    [ObservableProperty]
    private int _maxOutputTokens = 8192;

    [ObservableProperty]
    private int _thinkingBudgetTokens = 4096;

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

    [ObservableProperty]
    private bool _autoCheckUpdate = true;

    [ObservableProperty]
    private bool _isCheckingUpdate;

    [ObservableProperty]
    private bool _enableLookupCache = true;

    [ObservableProperty]
    private int _cacheTtlDays = 7;

    [ObservableProperty]
    private int _cacheEntryCount;

    public string CurrentVersion => UpdateService.CurrentVersion;

    public ObservableCollection<string> AvailableModels { get; } = new();
    public ObservableCollection<string> AvailablePositions { get; } = new();
    public ObservableCollection<string> AvailableSizes { get; } = new();
    public string[] AvailableProviders { get; } = ["OpenAI", "Anthropic", "Google Gemini", "OpenRouter", "Azure OpenAI", "Microsoft Foundry", "Custom"];
    public IReadOnlyDictionary<InferenceProtocol, string> AvailableProtocols { get; } = new Dictionary<InferenceProtocol, string>
    {
        [InferenceProtocol.ChatCompletions] = "OpenAI Chat Completions",
        [InferenceProtocol.AnthropicMessages] = "Anthropic Messages"
    };
    public IReadOnlyDictionary<InstructionRole, string> AvailableInstructionRoles { get; } = new Dictionary<InstructionRole, string>
    {
        [InstructionRole.System] = "system",
        [InstructionRole.Developer] = "developer",
        [InstructionRole.User] = "user"
    };
    public IReadOnlyDictionary<OutputTokenParameter, string> AvailableTokenParameters => new Dictionary<OutputTokenParameter, string>
    {
        [OutputTokenParameter.ModelDefault] = Loc("Settings_ModelDefaults"),
        [OutputTokenParameter.MaxCompletionTokens] = "max_completion_tokens",
        [OutputTokenParameter.MaxTokens] = "max_tokens"
    };
    public IReadOnlyDictionary<ReasoningMode, string> AvailableReasoningModes => IsMessages
        ? new Dictionary<ReasoningMode, string>
        {
            [ReasoningMode.ModelDefault] = Loc("Settings_ModelDefaults"),
            [ReasoningMode.ThinkingDisabled] = "thinking: disabled",
            [ReasoningMode.AnthropicAdaptive] = "Claude: adaptive thinking",
            [ReasoningMode.AnthropicBudgeted] = "Claude: budgeted thinking"
        }
        : new Dictionary<ReasoningMode, string>
        {
            [ReasoningMode.ModelDefault] = Loc("Settings_ModelDefaults"),
            [ReasoningMode.OpenAiEffort] = "reasoning_effort",
            [ReasoningMode.ThinkingEnabled] = "thinking: enabled",
            [ReasoningMode.ThinkingDisabled] = "thinking: disabled"
        };
    public IReadOnlyDictionary<string, string> AvailableReasoningEfforts
    {
        get
        {
            var values = new Dictionary<string, string> { ["default"] = Loc("Settings_ModelDefaults") };
            foreach (var value in SelectedReasoningMode == ReasoningMode.OpenAiEffort
                ? new[] { "none", "minimal", "low", "medium", "high", "xhigh", "max" }
                : new[] { "low", "medium", "high", "xhigh", "max" }) values.Add(value, value);
            return values;
        }
    }
    public bool IsLegacyAzure => SelectedProvider == "Azure OpenAI";
    public bool IsFoundry => SelectedProvider == "Microsoft Foundry";
    public bool ShowProtocol => IsFoundry || IsCustom;
    public bool IsMessages => SelectedProvider == "Anthropic" || (ShowProtocol && Protocol == InferenceProtocol.AnthropicMessages);
    public bool ShowReasoningEffort => SelectedReasoningMode is ReasoningMode.OpenAiEffort or ReasoningMode.AnthropicAdaptive;
    public bool ShowThinkingBudget => SelectedReasoningMode == ReasoningMode.AnthropicBudgeted;
    public bool ShowTokenLimit => IsMessages || TokenLimitParameter != OutputTokenParameter.ModelDefault;
    public string AzureEndpointPlaceholder => IsFoundry ? "https://your-resource.services.ai.azure.com" : "https://your-resource.openai.azure.com";
    public string[] AvailableThemes { get; } = ["System", "Light", "Dark"];
    public string[] AvailableUiLanguages { get; } = ["한국어", "English", "中文", "日本語"];
    public string[] AvailableLanguages { get; } =
    [
        "Korean", "English", "Japanese", "Chinese", "Spanish", "French",
        "German", "Portuguese", "Russian", "Arabic", "Italian", "Dutch",
        "Vietnamese", "Thai", "Indonesian", "Hindi", "Turkish", "Polish",
        "Swedish", "Czech"
    ];

    public SettingsViewModel(SettingsService settingsService, LocalizationService localizationService, LookupCacheService cacheService)
    {
        _settingsService = settingsService;
        _localizationService = localizationService;
        _cacheService = cacheService;
        _localizationService.LanguageChanged += OnLanguageChanged;
        LoadFromSettings();
        _loadingSettings = false;
    }

    private void OnLanguageChanged()
    {
        RefreshLocalizedCollections();
        RefreshApiOptions();
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
        ApiProvider.Foundry => "Microsoft Foundry",
        ApiProvider.Anthropic => "Anthropic",
        ApiProvider.Google => "Google Gemini",
        ApiProvider.OpenRouter => "OpenRouter",
        ApiProvider.Custom => "Custom",
        _ => "OpenAI"
    };

    private static ApiProvider StringToProvider(string s) => s switch
    {
        "Azure OpenAI" => ApiProvider.AzureOpenAI,
        "Microsoft Foundry" => ApiProvider.Foundry,
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
        IsAzure = s.Provider is ApiProvider.AzureOpenAI or ApiProvider.Foundry;
        IsCustom = s.Provider == ApiProvider.Custom;
        ApiKey = s.ApiKey;
        SelectedModel = s.Model;
        Protocol = s.Protocol;
        SelectedReasoningMode = OpenAiService.GetReasoningMode(s);
        ReasoningEffort = SelectedReasoningMode == ReasoningMode.ModelDefault ? "default" : s.ReasoningEffort;
        SelectedInstructionRole = s.InstructionRole;
        TokenLimitParameter = s.TokenLimitParameter;
        MaxOutputTokens = s.MaxOutputTokens;
        ThinkingBudgetTokens = s.ThinkingBudgetTokens;
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
        AutoCheckUpdate = s.AutoCheckUpdate;
        EnableLookupCache = s.EnableLookupCache;
        CacheTtlDays = s.CacheTtlDays;
        CacheEntryCount = _cacheService.Count;

        // Populate localized collections
        AvailablePositions.Clear();
        foreach (var e in PositionEntries) AvailablePositions.Add(Loc(e.Key));
        AvailableSizes.Clear();
        foreach (var e in SizeEntries) AvailableSizes.Add(Loc(e.Key));

        UpdateAvailableModels();
    }

    partial void OnSelectedProviderChanged(string value)
    {
        IsAzure = value is "Azure OpenAI" or "Microsoft Foundry";
        IsCustom = value == "Custom";
        HasModelCatalog = ModelCatalog.TryGetValue(value, out var models) && models.Length > 0;
        UpdateAvailableModels();
        if (!_loadingSettings)
        {
            SelectedModel = AvailableModels.FirstOrDefault() ?? string.Empty;
            Protocol = InferenceProtocol.ChatCompletions;
            ResetRequestOptions();
        }
        RefreshApiOptions();
    }

    partial void OnProtocolChanged(InferenceProtocol value)
    {
        if (!_loadingSettings) ResetRequestOptions();
        RefreshApiOptions();
    }

    partial void OnSelectedModelChanged(string value)
    {
        if (!_loadingSettings) ResetRequestOptions();
    }

    partial void OnSelectedReasoningModeChanged(ReasoningMode value)
    {
        if (!_loadingSettings) ReasoningEffort = "default";
        RefreshApiOptions();
    }

    partial void OnTokenLimitParameterChanged(OutputTokenParameter value) => OnPropertyChanged(nameof(ShowTokenLimit));

    private void ResetRequestOptions()
    {
        SelectedReasoningMode = ReasoningMode.ModelDefault;
        ReasoningEffort = "default";
        SelectedInstructionRole = InstructionRole.System;
        TokenLimitParameter = OutputTokenParameter.ModelDefault;
    }

    private void RefreshApiOptions()
    {
        OnPropertyChanged(nameof(IsLegacyAzure));
        OnPropertyChanged(nameof(IsFoundry));
        OnPropertyChanged(nameof(ShowProtocol));
        OnPropertyChanged(nameof(IsMessages));
        OnPropertyChanged(nameof(ShowReasoningEffort));
        OnPropertyChanged(nameof(ShowThinkingBudget));
        OnPropertyChanged(nameof(ShowTokenLimit));
        OnPropertyChanged(nameof(AzureEndpointPlaceholder));
        OnPropertyChanged(nameof(AvailableReasoningModes));
        OnPropertyChanged(nameof(AvailableReasoningEfforts));
        OnPropertyChanged(nameof(AvailableTokenParameters));
    }

    private void UpdateAvailableModels()
    {
        AvailableModels.Clear();
        if (ModelCatalog.TryGetValue(SelectedProvider, out var models))
        {
            foreach (var m in models) AvailableModels.Add(m);
        }
    }

    private CancellationTokenSource? _statusClearCts;

    private void ApplyRequestSettings(AppSettings target)
    {
        target.Provider = StringToProvider(SelectedProvider);
        target.ApiKey = ApiKey;
        target.Model = SelectedModel;
        target.ReasoningEffort = ReasoningEffort;
        target.Protocol = Protocol;
        target.ReasoningMode = SelectedReasoningMode;
        target.InstructionRole = SelectedInstructionRole;
        target.TokenLimitParameter = TokenLimitParameter;
        target.MaxOutputTokens = MaxOutputTokens;
        target.ThinkingBudgetTokens = ThinkingBudgetTokens;
        target.AzureEndpoint = AzureEndpoint;
        target.AzureApiVersion = AzureApiVersion;
        target.CustomEndpoint = CustomEndpoint;
    }

    [RelayCommand]
    private async Task SaveSettingsAsync()
    {
        var candidate = new AppSettings();
        ApplyRequestSettings(candidate);
        try { OpenAiService.ValidateConfiguration(candidate); }
        catch (InvalidOperationException exception)
        {
            StatusMessage = exception.Message;
            return;
        }
        var s = _settingsService.Current;
        ApplyRequestSettings(s);
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
        s.AutoCheckUpdate = AutoCheckUpdate;
        s.EnableLookupCache = EnableLookupCache;
        s.CacheTtlDays = CacheTtlDays;

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

    [RelayCommand]
    private async Task CheckForUpdatesAsync()
    {
        if (IsCheckingUpdate) return;
        IsCheckingUpdate = true;
        try
        {
            StatusMessage = Loc("Update_Checking");
            await ((App)Application.Current).CheckForUpdatesAsync(silentIfNone: false);
            StatusMessage = string.Empty;
        }
        finally
        {
            IsCheckingUpdate = false;
        }
    }

    [RelayCommand]
    private async Task ClearCacheAsync()
    {
        await _cacheService.ClearAsync();
        CacheEntryCount = _cacheService.Count;
    }
}
