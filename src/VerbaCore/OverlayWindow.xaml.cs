using System.Text;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Markdig;
using Markdig.Wpf;
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
    private DateTime _lastRenderTime = DateTime.MinValue;
    private const int RenderThrottleMs = 200;

    private LookupMode _currentMode = LookupMode.Dictionary;
    private readonly LookupMode[] _modes = [LookupMode.Dictionary, LookupMode.Translate, LookupMode.Analyze];
    private int _modeIndex;

    /// <summary>Whether the overlay is in persistent (quick-tap) mode.</summary>
    private bool _persistentMode;
    /// <summary>Whether the overlay is currently visible.</summary>
    private bool _isShown;
    /// <summary>Set when CapsLock down opens a fresh overlay, cleared on release.</summary>
    private bool _justOpened;

    private static readonly MarkdownPipeline MarkdownPipeline = new MarkdownPipelineBuilder()
        .UseSupportedExtensions()
        .Build();

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
        _autoHideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(60) };
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

        // Close overlay when it loses focus (user clicked outside)
        Deactivated += (_, _) =>
        {
            if (_isShown)
                HideOverlay();
        };

        // Handle Tab key for mode switching (in the keyboard hook)
        PreviewKeyDown += (_, e) =>
        {
            // In persistent mode, let the TextBox handle input
            if (_persistentMode && e.Key != System.Windows.Input.Key.Tab)
                return;

            // While viewing results, allow Escape to close but don't eat other keys
            if (ResultViewer.Visibility == Visibility.Visible)
            {
                if (e.Key == System.Windows.Input.Key.Escape)
                {
                    HideOverlay();
                    e.Handled = true;
                }
                // Reset auto-hide timer on any key press
                if (_autoHideTimer.IsEnabled)
                {
                    _autoHideTimer.Stop();
                    _autoHideTimer.Start();
                }
                return;
            }

            e.Handled = true;
        };

        // Reset auto-hide timer on mouse interaction (scrolling)
        PreviewMouseDown += (_, _) => ResetAutoHideTimer();
        PreviewMouseWheel += (_, _) => ResetAutoHideTimer();

        // TextBox Enter key handler for persistent mode
        InputTextBox.PreviewKeyDown += OnInputTextBoxKeyDown;
    }

    private void OnInputTextBoxKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter)
        {
            e.Handled = true;
            var input = InputTextBox.Text.Trim();
            if (!string.IsNullOrEmpty(input))
            {
                _cursorBlinkTimer.Stop();
                BlinkingCursor.Visibility = Visibility.Collapsed;
                HintLabel.Visibility = Visibility.Collapsed;
                _ = PerformLookupAsync(input);
            }
        }
        else if (e.Key == System.Windows.Input.Key.Escape)
        {
            e.Handled = true;
            HideOverlay();
        }
        else if (e.Key == System.Windows.Input.Key.Tab)
        {
            e.Handled = true;
            _modeIndex = (_modeIndex + 1) % _modes.Length;
            _currentMode = _modes[_modeIndex];
            UpdateModeLabel();
        }
    }

    private void OnCapsLockPressed(object? sender, EventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            // If overlay is already showing, mark that we did NOT just open it
            // (so quick-tap release will close it)
            if (_isShown)
            {
                _justOpened = false;
                return;
            }

            // Opening a fresh overlay
            _justOpened = true;
            _cts?.Cancel();
            _autoHideTimer.Stop();

            // Reset UI for new input
            InputDisplay.Text = "";
            InputDisplay.Visibility = Visibility.Visible;
            InputTextBox.Text = "";
            InputTextBox.Visibility = Visibility.Collapsed;
            ResultViewer.Document = new FlowDocument();
            ResultViewer.Visibility = Visibility.Collapsed;
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
            if (_isShown && !_justOpened)
            {
                // Overlay was already showing before this CapsLock press — close it
                _capsLockService.PersistentModeActive = false;
                _persistentMode = false;
                _cursorBlinkTimer.Stop();
                HideOverlay();
            }
            else
            {
                _justOpened = false;
                // First quick tap — enter persistent mode with IME TextBox
                _persistentMode = true;
                _capsLockService.PersistentModeActive = false; // Don't intercept keys — let TextBox handle them
                StatusLabel.Text = "단어를 입력하세요 — Enter: 조회, CapsLock: 닫기";

                // Switch to TextBox UI
                InputDisplay.Text = "";
                InputDisplay.Visibility = Visibility.Collapsed;
                InputTextBox.Text = "";
                InputTextBox.Visibility = Visibility.Visible;
                BlinkingCursor.Visibility = Visibility.Collapsed;
                ResultViewer.Document = new FlowDocument();
                ResultViewer.Visibility = Visibility.Collapsed;
                LoadingPanel.Visibility = Visibility.Collapsed;
                HintLabel.Visibility = Visibility.Visible;
                UpdateModeLabel();

                if (!_isShown)
                {
                    ShowOverlay();
                }

                // Focus the TextBox for IME input
                Activate();
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, () =>
                {
                    InputTextBox.Focus();
                    System.Windows.Input.Keyboard.Focus(InputTextBox);
                });
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
    /// Enter pressed in persistent mode (from keyboard hook) — trigger lookup.
    /// </summary>
    private void OnEnterPressed(object? sender, EventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            // In persistent mode we use the TextBox, so read from it
            if (_persistentMode)
            {
                var input = InputTextBox.Text.Trim();
                if (!string.IsNullOrEmpty(input))
                {
                    HintLabel.Visibility = Visibility.Collapsed;
                    _ = PerformLookupAsync(input);
                }
                return;
            }

            _cursorBlinkTimer.Stop();
            BlinkingCursor.Visibility = Visibility.Collapsed;
            HintLabel.Visibility = Visibility.Collapsed;

            var bufferInput = _capsLockService.Buffer.Trim();
            if (string.IsNullOrEmpty(bufferInput)) return;

            _ = PerformLookupAsync(bufferInput);
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

        // Show result area immediately — no loading spinner
        LoadingPanel.Visibility = Visibility.Collapsed;
        ResultViewer.Visibility = Visibility.Visible;
        RenderPlainText("...");
        StatusLabel.Text = "";

        var src = _settingsService.Current.SourceLanguage;
        var tgt = _settingsService.Current.TargetLanguage;

        try
        {
            var sb = new StringBuilder();
            await foreach (var chunk in _openAiService.StreamCompletionAsync(
                input, _currentMode, src, tgt, ct))
            {
                sb.Append(chunk);

                // Throttled rendering during streaming
                var now = DateTime.UtcNow;
                if ((now - _lastRenderTime).TotalMilliseconds >= RenderThrottleMs)
                {
                    _lastRenderTime = now;
                    RenderPlainText(sb.ToString());
                    // Force UI to paint
                    await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Render);
                }
            }

            // Final render with full Markdown formatting
            RenderMarkdown(sb.ToString());

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
            ResultViewer.Visibility = Visibility.Visible;
            RenderMarkdown($"오류: {ex.Message}\n\nAPI Key와 네트워크를 확인해주세요.");
            StatusLabel.Text = "오류 발생";
            _autoHideTimer.Start();
        }
    }

    private void ResetAutoHideTimer()
    {
        if (_autoHideTimer.IsEnabled)
        {
            _autoHideTimer.Stop();
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

        // Only call Show() once — after that, use opacity to hide/show
        if (!IsVisible)
        {
            RootGrid.Opacity = 0;
            Show();
        }

        Activate();

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

        // Reset TextBox/TextBlock visibility
        InputTextBox.Visibility = Visibility.Collapsed;
        InputDisplay.Visibility = Visibility.Visible;

        var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(150));
        fadeOut.Completed += (_, _) =>
        {
            // Move off-screen instead of Hide() to avoid re-render flash
            Left = -9999;
            Top = -9999;
        };
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

    private void RenderMarkdown(string markdown)
    {
        try
        {
            var doc = Markdig.Wpf.Markdown.ToFlowDocument(markdown, MarkdownPipeline);
            ApplyDarkThemeToDocument(doc);
            ResultViewer.Document = doc;
        }
        catch
        {
            RenderPlainText(markdown);
        }
    }

    /// <summary>
    /// Fast plain-text rendering used during streaming (no Markdown parsing overhead).
    /// </summary>
    private void RenderPlainText(string text)
    {
        var doc = new FlowDocument(new Paragraph(new Run(text)))
        {
            Foreground = new SolidColorBrush(Color.FromArgb(0xE0, 0xFF, 0xFF, 0xFF)),
            FontSize = 20,
            FontFamily = new FontFamily("AppleSDGothicNeoR00, Segoe UI"),
            PagePadding = new Thickness(0)
        };
        ResultViewer.Document = doc;
    }

    /// <summary>
    /// Recursively apply bright colors to all elements for dark overlay background.
    /// </summary>
    private static void ApplyDarkThemeToDocument(FlowDocument doc)
    {
        var baseFontSize = 20.0;
        doc.Foreground = new SolidColorBrush(Color.FromArgb(0xE0, 0xFF, 0xFF, 0xFF));
        doc.FontSize = baseFontSize;
        doc.FontFamily = new FontFamily("AppleSDGothicNeoR00, Segoe UI");
        doc.PagePadding = new Thickness(0);

        foreach (var block in doc.Blocks)
        {
            ApplyDarkThemeToBlock(block, baseFontSize);
        }
    }

    private static void ApplyDarkThemeToBlock(System.Windows.Documents.Block block, double baseFontSize)
    {
        block.Foreground = new SolidColorBrush(Color.FromArgb(0xE0, 0xFF, 0xFF, 0xFF));

        if (block is Paragraph p)
        {
            // Detect headings: Markdig.Wpf sets FontSize relative to document base
            // If the paragraph has a FontSize explicitly set and it differs from base, it's a heading
            var pFontSize = p.FontSize;
            bool isHeading = !double.IsNaN(pFontSize) && pFontSize != baseFontSize;

            if (isHeading)
            {
                // Scale headings relative to base font size
                // Markdig.Wpf typically uses ratios like 2.0, 1.5, 1.17, 1.0 for h1-h4
                double ratio = pFontSize / 12.0; // Markdig default base is ~12
                p.FontSize = baseFontSize * Math.Max(ratio, 1.2);
                p.Foreground = new SolidColorBrush(Colors.White);
                p.FontWeight = System.Windows.FontWeights.Bold;
                p.Margin = new Thickness(0, 8, 0, 4);
            }

            foreach (var inline in p.Inlines)
            {
                ApplyDarkThemeToInline(inline);
            }
        }
        else if (block is System.Windows.Documents.List list)
        {
            foreach (var item in list.ListItems)
            {
                foreach (var itemBlock in item.Blocks)
                {
                    ApplyDarkThemeToBlock(itemBlock, baseFontSize);
                }
            }
        }
        else if (block is System.Windows.Documents.Section section)
        {
            foreach (var sBlock in section.Blocks)
            {
                ApplyDarkThemeToBlock(sBlock, baseFontSize);
            }
        }
        else if (block is System.Windows.Documents.Table table)
        {
            table.Foreground = new SolidColorBrush(Color.FromArgb(0xE0, 0xFF, 0xFF, 0xFF));
        }
    }

    private static void ApplyDarkThemeToInline(System.Windows.Documents.Inline inline)
    {
        inline.Foreground = new SolidColorBrush(Color.FromArgb(0xE0, 0xFF, 0xFF, 0xFF));

        // Bold: pure white
        if (inline is System.Windows.Documents.Bold bold)
        {
            bold.Foreground = new SolidColorBrush(Colors.White);
            foreach (var child in bold.Inlines)
                ApplyDarkThemeToInline(child);
        }
        // Italic: slightly tinted
        else if (inline is System.Windows.Documents.Italic italic)
        {
            italic.Foreground = new SolidColorBrush(Color.FromArgb(0xD0, 0xCC, 0xDD, 0xFF));
            foreach (var child in italic.Inlines)
                ApplyDarkThemeToInline(child);
        }
        // Inline code: light cyan
        else if (inline is System.Windows.Documents.Run run && run.Background != null)
        {
            run.Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0xA0, 0xE0, 0xFF));
            run.Background = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF));
        }
        else if (inline is System.Windows.Documents.Span span)
        {
            foreach (var child in span.Inlines)
                ApplyDarkThemeToInline(child);
        }
    }
}
