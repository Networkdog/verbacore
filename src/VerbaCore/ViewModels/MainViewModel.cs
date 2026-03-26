using System.Collections.ObjectModel;
using System.Net.Http;
using System.Text;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VerbaCore.Models;
using VerbaCore.Services;

namespace VerbaCore.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IOpenAiService _openAiService;
    private readonly SettingsService _settingsService;
    private readonly HistoryService _historyService;
    private readonly SpeechInputService _speechService;
    private readonly CursorTextService _cursorTextService;

    private CancellationTokenSource? _cts;

    [ObservableProperty]
    private string _inputText = string.Empty;

    [ObservableProperty]
    private string _resultText = string.Empty;

    [ObservableProperty]
    private LookupMode _currentMode = LookupMode.Dictionary;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _isListening;

    [ObservableProperty]
    private string _statusMessage = "Ready";

    [ObservableProperty]
    private string _sourceLanguage = "English";

    [ObservableProperty]
    private string _targetLanguage = "Korean";

    public ObservableCollection<string> SupportedLanguages { get; } =
    [
        "English", "Korean", "Japanese", "Chinese", "Spanish",
        "French", "German", "Portuguese", "Russian", "Arabic",
        "Italian", "Dutch", "Vietnamese", "Thai", "Indonesian"
    ];

    public MainViewModel(
        IOpenAiService openAiService,
        SettingsService settingsService,
        HistoryService historyService,
        SpeechInputService speechService,
        CursorTextService cursorTextService)
    {
        _openAiService = openAiService;
        _settingsService = settingsService;
        _historyService = historyService;
        _speechService = speechService;
        _cursorTextService = cursorTextService;

        _sourceLanguage = settingsService.Current.SourceLanguage;
        _targetLanguage = settingsService.Current.TargetLanguage;

        _speechService.SpeechRecognized += (_, text) =>
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                InputText = text;
                _ = LookupAsync();
            });
        };

        _speechService.ListeningStateChanged += (_, listening) =>
        {
            Application.Current.Dispatcher.Invoke(() => IsListening = listening);
        };
    }

    [RelayCommand]
    private async Task LookupAsync()
    {
        var input = InputText?.Trim();
        if (string.IsNullOrEmpty(input)) return;

        if (string.IsNullOrEmpty(_settingsService.Current.ApiKey))
        {
            StatusMessage = "API Key가 설정되지 않았습니다. 설정에서 입력해주세요.";
            return;
        }

        // Auto-select mode based on input length
        CurrentMode = PromptBuilder.AutoSelectMode(input);

        // Cancel any previous request
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        IsLoading = true;
        ResultText = string.Empty;
        StatusMessage = "조회 중...";

        try
        {
            var sb = new StringBuilder();
            await foreach (var chunk in _openAiService.StreamCompletionAsync(
                input, CurrentMode, SourceLanguage, TargetLanguage, ct))
            {
                sb.Append(chunk);
                ResultText = sb.ToString();
            }

            StatusMessage = "완료";

            // Save to history
            await _historyService.AddAsync(new LookupHistoryItem
            {
                Input = input,
                Mode = CurrentMode,
                Response = ResultText,
                SourceLanguage = SourceLanguage,
                TargetLanguage = TargetLanguage
            });
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "취소됨";
        }
        catch (HttpRequestException ex)
        {
            StatusMessage = $"API 오류: {ex.Message}";
            ResultText = $"오류가 발생했습니다.\n\n{ex.Message}\n\nAPI Key와 네트워크 연결을 확인해주세요.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"오류: {ex.Message}";
            ResultText = $"예기치 않은 오류가 발생했습니다.\n\n{ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void SetMode(string mode)
    {
        CurrentMode = mode switch
        {
            "Dictionary" => LookupMode.Dictionary,
            "Translate" => LookupMode.Translate,
            _ => LookupMode.Dictionary
        };
    }

    [RelayCommand]
    private void ToggleSpeech()
    {
        var culture = SourceLanguage switch
        {
            "Korean" => "ko-KR",
            "Japanese" => "ja-JP",
            "Chinese" => "zh-CN",
            "Spanish" => "es-ES",
            "French" => "fr-FR",
            "German" => "de-DE",
            _ => "en-US"
        };
        _speechService.ToggleListening(culture);
    }

    [RelayCommand]
    private void GrabCursorText()
    {
        var text = _cursorTextService.GetTextUnderCursor();
        if (!string.IsNullOrEmpty(text))
        {
            InputText = text;
            _ = LookupAsync();
        }
        else
        {
            StatusMessage = "커서 위치에서 텍스트를 찾을 수 없습니다.";
        }
    }

    [RelayCommand]
    private void CancelLookup()
    {
        _cts?.Cancel();
    }

    [RelayCommand]
    private void SwapLanguages()
    {
        (SourceLanguage, TargetLanguage) = (TargetLanguage, SourceLanguage);
    }

    partial void OnSourceLanguageChanged(string value)
    {
        _settingsService.Current.SourceLanguage = value;
        _settingsService.QueueSave();
    }

    partial void OnTargetLanguageChanged(string value)
    {
        _settingsService.Current.TargetLanguage = value;
        _settingsService.QueueSave();
    }
}
