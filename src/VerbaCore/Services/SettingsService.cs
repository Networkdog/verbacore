using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using VerbaCore.Models;

namespace VerbaCore.Services;

public sealed class SettingsService
{
    private static readonly string SettingsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VerbaCore");
    private static readonly string SettingsPath = Path.Combine(SettingsDir, "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private AppSettings _current = new();

    public AppSettings Current => _current;

    public async Task LoadAsync()
    {
        if (!File.Exists(SettingsPath))
        {
            _current = new AppSettings();
            return;
        }

        var json = await File.ReadAllTextAsync(SettingsPath);
        _current = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();

        // Decrypt API key
        if (!string.IsNullOrEmpty(_current.ApiKeyProtected))
        {
            _current.ApiKey = UnprotectString(_current.ApiKeyProtected);
        }
    }

    public async Task SaveAsync()
    {
        Directory.CreateDirectory(SettingsDir);

        // Encrypt API key before saving
        if (!string.IsNullOrEmpty(_current.ApiKey))
        {
            _current.ApiKeyProtected = ProtectString(_current.ApiKey);
        }

        // Don't persist the plaintext key
        var toSave = new AppSettings
        {
            Provider = _current.Provider,
            ApiKeyProtected = _current.ApiKeyProtected,
            Model = _current.Model,
            ReasoningEffort = _current.ReasoningEffort,
            AzureEndpoint = _current.AzureEndpoint,
            AzureDeploymentName = _current.AzureDeploymentName,
            AzureApiVersion = _current.AzureApiVersion,
            SourceLanguage = _current.SourceLanguage,
            TargetLanguage = _current.TargetLanguage,
            GlobalHotkey = _current.GlobalHotkey,
            ClipboardMonitorEnabled = _current.ClipboardMonitorEnabled,
            StartWithWindows = _current.StartWithWindows,
            Theme = _current.Theme,
            WindowWidth = _current.WindowWidth,
            WindowHeight = _current.WindowHeight
        };

        var json = JsonSerializer.Serialize(toSave, JsonOptions);
        await File.WriteAllTextAsync(SettingsPath, json);
    }

    private static string ProtectString(string plainText)
    {
        var bytes = Encoding.UTF8.GetBytes(plainText);
        var encrypted = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(encrypted);
    }

    private static string UnprotectString(string protectedText)
    {
        try
        {
            var encrypted = Convert.FromBase64String(protectedText);
            var bytes = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }
        catch
        {
            return string.Empty;
        }
    }
}
