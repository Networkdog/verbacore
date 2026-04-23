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
    private CancellationTokenSource? _debounceCts;

    public AppSettings Current => _current;

    /// <summary>True if no settings file existed when LoadAsync was called (first run).</summary>
    public bool IsFirstRun { get; private set; }

    public async Task LoadAsync()
    {
        if (!File.Exists(SettingsPath))
        {
            IsFirstRun = true;
            _current = new AppSettings();
            return;
        }

        try
        {
            var json = await File.ReadAllTextAsync(SettingsPath);
            _current = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
        }
        catch (JsonException)
        {
            // Corrupted settings file — start fresh
            _current = new AppSettings();
            return;
        }

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
            CustomEndpoint = _current.CustomEndpoint,
            SourceLanguage = _current.SourceLanguage,
            TargetLanguage = _current.TargetLanguage,
            GlobalHotkey = _current.GlobalHotkey,
            StartWithWindows = _current.StartWithWindows,
            Theme = _current.Theme,
            WindowWidth = _current.WindowWidth,
            WindowHeight = _current.WindowHeight,
            PopupPosition = _current.PopupPosition,
            OverlaySize = _current.OverlaySize
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

    /// <summary>
    /// Debounced save — coalesces rapid successive calls into a single write after 300ms.
    /// </summary>
    public void QueueSave()
    {
        _debounceCts?.Cancel();
        _debounceCts = new CancellationTokenSource();
        var ct = _debounceCts.Token;
        _ = Task.Delay(300, ct).ContinueWith(async _ =>
        {
            if (!ct.IsCancellationRequested)
                await SaveAsync();
        }, ct, TaskContinuationOptions.OnlyOnRanToCompletion, TaskScheduler.Default);
    }
}
