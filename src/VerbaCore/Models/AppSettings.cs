namespace VerbaCore.Models;

public sealed class AppSettings
{
    public ApiProvider Provider { get; set; } = ApiProvider.OpenAI;
    public string ApiKey { get; set; } = string.Empty;
    public string ApiKeyProtected { get; set; } = string.Empty;
    public string Model { get; set; } = "gpt-4o-mini";

    // Azure OpenAI specific
    public string AzureEndpoint { get; set; } = string.Empty;
    public string AzureDeploymentName { get; set; } = string.Empty;
    public string AzureApiVersion { get; set; } = "2024-10-21";

    public string SourceLanguage { get; set; } = "English";
    public string TargetLanguage { get; set; } = "Korean";
    public string GlobalHotkey { get; set; } = "Ctrl+Alt+V";
    public bool ClipboardMonitorEnabled { get; set; } = true;
    public bool StartWithWindows { get; set; } = false;
    public ThemeMode Theme { get; set; } = ThemeMode.System;
    public double WindowWidth { get; set; } = 520;
    public double WindowHeight { get; set; } = 680;
}

public enum ApiProvider
{
    OpenAI,
    AzureOpenAI
}

public enum ThemeMode
{
    System,
    Light,
    Dark
}
