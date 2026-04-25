namespace VerbaCore.Models;

public sealed class AppSettings
{
    public ApiProvider Provider { get; set; } = ApiProvider.OpenAI;
    public string ApiKey { get; set; } = string.Empty;
    public string ApiKeyProtected { get; set; } = string.Empty;
    public string Model { get; set; } = "gpt-4o-mini";
    public string ReasoningEffort { get; set; } = "none";

    // Azure OpenAI specific
    public string AzureEndpoint { get; set; } = string.Empty;
    public string AzureDeploymentName { get; set; } = string.Empty;
    public string AzureApiVersion { get; set; } = "2024-10-21";

    // Custom/OpenAI-compatible endpoint (for OpenRouter, local LLMs, etc.)
    public string CustomEndpoint { get; set; } = string.Empty;

    public string NativeLanguage { get; set; } = "Korean";
    public string ForeignLanguage { get; set; } = "English";
    public string GlobalHotkey { get; set; } = "Ctrl+Alt+V";
    public bool StartWithWindows { get; set; } = false;
    public ThemeMode Theme { get; set; } = ThemeMode.System;
    public double WindowWidth { get; set; } = 520;
    public double WindowHeight { get; set; } = 680;
    public OverlayPosition PopupPosition { get; set; } = OverlayPosition.CenterCenter;
    public OverlaySize OverlaySize { get; set; } = OverlaySize.Medium;
}

public enum ApiProvider
{
    OpenAI,
    AzureOpenAI,
    Anthropic,
    Google,
    OpenRouter,
    Custom
}

public enum OverlayPosition
{
    TopLeft,
    TopCenter,
    TopRight,
    CenterLeft,
    CenterCenter,
    CenterRight,
    BottomLeft,
    BottomCenter,
    BottomRight
}

public enum ThemeMode
{
    System,
    Light,
    Dark
}

public enum OverlaySize
{
    Small,
    Medium,
    Large
}
