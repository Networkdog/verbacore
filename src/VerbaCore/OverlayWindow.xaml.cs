using System.Text;
using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using VerbaCore.Models;
using VerbaCore.Services;

namespace VerbaCore;

public partial class OverlayWindow : Window
{
    private readonly IOpenAiService _openAiService;
    private readonly SettingsService _settingsService;
    private readonly HistoryService _historyService;
    private readonly CapsLockService _capsLockService;

    private readonly DispatcherTimer _cursorBlinkTimer;
    private readonly DispatcherTimer _autoHideTimer;
    private CancellationTokenSource? _cts;

    private LookupMode _currentMode = LookupMode.Dictionary;
    private readonly LookupMode[] _modes = [LookupMode.Dictionary, LookupMode.Translate, LookupMode.Analyze];
    private int _modeIndex;

    /// <summary>Whether the overlay is in persistent (quick-tap) mode.</summary>
    private bool _persistentMode;
    /// <summary>Whether the overlay is currently visible.</summary>
    private bool _isShown;

    public OverlayWindow(
        IOpenAiService openAiService,
        SettingsService settingsService,
        HistoryService historyService,
        CapsLockService capsLockService)
    {
        _openAiService = openAiService;
        _settingsService = settingsService;
        _historyService = historyService;
        _capsLockService = capsLockService;

        InitializeComponent();

        // Cursor blink timer
        _cursorBlinkTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(530) };
        _cursorBlinkTimer.Tick += (_, _) =>
            BlinkingCursor.Opacity = BlinkingCursor.Opacity > 0.1 ? 0 : 0.8;

        // Auto-hide timer (hide result after delay)
        _autoHideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
        _autoHideTimer.Tick += (_, _) =>
        {
            _autoHideTimer.Stop();
            HideOverlay();
        };

        // CapsLock events
        _capsLockService.CapsLockPressed += OnCapsLockPressed;
        _capsLockService.QuickTapReleased += OnQuickTapReleased;
        _capsLockService.LongPressReleased += OnLongPressReleased;
        _capsLockService.BufferChanged += OnBufferChanged;
        _capsLockService.EnterPressed += OnEnterPressed;

        // Handle Tab key for mode switching (in the keyboard hook)
        PreviewKeyDown += (_, e) => e.Handled = true;
    }

    private void OnCapsLockPressed(object? sender, EventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            // If in persistent mode and overlay is showing, CapsLock press is
            // part of a quick-tap to close — don't reset the UI yet.
            if (_persistentMode && _isShown)
                return;

            _cts?.Cancel();
            _autoHideTimer.Stop();

            // Reset UI for new input
            InputDisplay.Text = "";
            ResultDisplay.Text = "";
            ResultScroller.Visibility = Visibility.Collapsed;
            LoadingPanel.Visibility = Visibility.Collapsed;
            BlinkingCursor.Visibility = Visibility.Visible;
            HintLabel.Visibility = Visibility.Visible;
            StatusLabel.Text = "CapsLock을 누른 채로 단어를 입력하세요";

            UpdateModeLabel();
            UpdateCursorPosition();

            // Show overlay (Enso-style hold mode for now — may become persistent on quick tap)
            if (!_isShown)
            {
                ShowOverlay();
            }
            _cursorBlinkTimer.Start();
        });
    }

    /// <summary>
    /// Quick tap: CapsLock pressed and released quickly with no typing.
    /// Toggle persistent search overlay on/off.
    /// </summary>
    private void OnQuickTapReleased(object? sender, EventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            if (_persistentMode && _isShown)
            {
                // Second quick tap — close the overlay
                _capsLockService.PersistentModeActive = false;
                _persistentMode = false;
                _cursorBlinkTimer.Stop();
                HideOverlay();
            }
            else
            {
                // First quick tap — enter persistent mode
                _persistentMode = true;
                _capsLockService.PersistentModeActive = true;
                _cursorBlinkTimer.Start();
                BlinkingCursor.Visibility = Visibility.Visible;
                StatusLabel.Text = "단어를 입력하세요 — Enter: 조회, CapsLock: 닫기";

                if (!_isShown)
                {
                    InputDisplay.Text = "";
                    ResultDisplay.Text = "";
                    ResultScroller.Visibility = Visibility.Collapsed;
                    LoadingPanel.Visibility = Visibility.Collapsed;
                    HintLabel.Visibility = Visibility.Visible;
                    UpdateModeLabel();
                    ShowOverlay();
                }
            }
        });
    }

    /// <summary>
    /// Long press: CapsLock held ≥0.5s or typed while held.
    /// Perform lookup (if there's input) and auto-hide.
    /// </summary>
    private void OnLongPressReleased(object? sender, EventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            _persistentMode = false;
            _capsLockService.PersistentModeActive = false;
            _cursorBlinkTimer.Stop();
            BlinkingCursor.Visibility = Visibility.Collapsed;
            HintLabel.Visibility = Visibility.Collapsed;

            var input = _capsLockService.Buffer.Trim();
            if (string.IsNullOrEmpty(input))
            {
                HideOverlay();
                return;
            }

            // Trigger lookup
            _ = PerformLookupAsync(input);
        });
    }

    /// <summary>
    /// Enter pressed in persistent mode — trigger lookup.
    /// </summary>
    private void OnEnterPressed(object? sender, EventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            _cursorBlinkTimer.Stop();
            BlinkingCursor.Visibility = Visibility.Collapsed;
            HintLabel.Visibility = Visibility.Collapsed;

            var input = _capsLockService.Buffer.Trim();
            if (string.IsNullOrEmpty(input)) return;

            // Stay in persistent mode — show result on overlay
            _ = PerformLookupAsync(input);
        });
    }

    private void OnBufferChanged(object? sender, string buffer)
    {
        Dispatcher.Invoke(() =>
        {
            // Check for Tab (mode switch) — handle before display
            if (buffer.EndsWith('\t'))
            {
                _modeIndex = (_modeIndex + 1) % _modes.Length;
                _currentMode = _modes[_modeIndex];
                UpdateModeLabel();
                // Remove tab from buffer by clearing and re-setting
                _capsLockService.ClearBuffer();
                return;
            }

            InputDisplay.Text = buffer;
            UpdateCursorPosition();
        });
    }

    private void UpdateModeLabel()
    {
        ModeLabel.Text = _currentMode switch
        {
            LookupMode.Dictionary => "📖 사전",
            LookupMode.Translate => "🔄 번역",
            LookupMode.Analyze => "📝 분석",
            _ => "📖 사전"
        };

        var src = _settingsService.Current.SourceLanguage;
        var tgt = _settingsService.Current.TargetLanguage;
        LangLabel.Text = $"{LangCode(src)} → {LangCode(tgt)}";
    }

    private void UpdateCursorPosition()
    {
        // Position cursor after text
        InputDisplay.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var textWidth = InputDisplay.DesiredSize.Width;
        BlinkingCursor.Margin = new Thickness(Math.Min(textWidth + 2, ActualWidth - 100), 0, 0, 8);
    }

    private async Task PerformLookupAsync(string input)
    {
        if (string.IsNullOrEmpty(_settingsService.Current.ApiKey))
        {
            StatusLabel.Text = "⚠ API Key가 설정되지 않았습니다. 트레이 아이콘 → 설정";
            _autoHideTimer.Start();
            return;
        }

        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        LoadingPanel.Visibility = Visibility.Visible;
        StatusLabel.Text = "조회 중...";

        var src = _settingsService.Current.SourceLanguage;
        var tgt = _settingsService.Current.TargetLanguage;

        try
        {
            var sb = new StringBuilder();
            await foreach (var chunk in _openAiService.StreamCompletionAsync(
                input, _currentMode, src, tgt, ct))
            {
                sb.Append(chunk);
                LoadingPanel.Visibility = Visibility.Collapsed;
                ResultScroller.Visibility = Visibility.Visible;
                ResultDisplay.Text = sb.ToString();
            }

            StatusLabel.Text = "완료 — 아무 키를 누르면 닫힙니다";

            // Save to history
            await _historyService.AddAsync(new LookupHistoryItem
            {
                Input = input,
                Mode = _currentMode,
                Response = sb.ToString(),
                SourceLanguage = src,
                TargetLanguage = tgt
            });

            // Auto-hide after delay
            _autoHideTimer.Start();
        }
        catch (OperationCanceledException)
        {
            // Cancelled by new CapsLock press
        }
        catch (Exception ex)
        {
            LoadingPanel.Visibility = Visibility.Collapsed;
            ResultScroller.Visibility = Visibility.Visible;
            ResultDisplay.Text = $"오류: {ex.Message}\n\nAPI Key와 네트워크를 확인해주세요.";
            StatusLabel.Text = "오류 발생";
            _autoHideTimer.Start();
        }
    }

    private void ShowOverlay()
    {
        // Size to screen
        var screen = SystemParameters.PrimaryScreenWidth;
        Width = Math.Min(700, screen * 0.5);
        Left = (screen - Width) / 2;
        Top = SystemParameters.PrimaryScreenHeight * 0.25;

        _isShown = true;
        Show();

        var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(120));
        RootGrid.BeginAnimation(OpacityProperty, fadeIn);
    }

    public void HideOverlay()
    {
        _autoHideTimer.Stop();
        _cursorBlinkTimer.Stop();
        _isShown = false;
        _persistentMode = false;
        _capsLockService.PersistentModeActive = false;

        var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(150));
        fadeOut.Completed += (_, _) => Hide();
        RootGrid.BeginAnimation(OpacityProperty, fadeOut);
    }

    private static string LangCode(string lang) => lang switch
    {
        "English" => "EN",
        "Korean" => "KO",
        "Japanese" => "JA",
        "Chinese" => "ZH",
        "Spanish" => "ES",
        "French" => "FR",
        "German" => "DE",
        _ => lang[..2].ToUpperInvariant()
    };
}
