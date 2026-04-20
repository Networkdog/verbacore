using System.Net.Http;
using System.Text;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Markdig;
using Markdig.Wpf;
using VerbaCore.Helpers;
using VerbaCore.Models;
using VerbaCore.Services;

namespace VerbaCore;

public partial class OverlayWindow : Window
{
    private readonly IOpenAiService _openAiService;
    private readonly SettingsService _settingsService;
    private readonly HistoryService _historyService;
    private readonly CapsLockService _capsLockService;
    private readonly CursorTextService _cursorTextService;

    private readonly DispatcherTimer _cursorBlinkTimer;
    private readonly DispatcherTimer _autoHideTimer;
    private CancellationTokenSource? _cts;
    private DateTime _lastRenderTime = DateTime.MinValue;
    private const int RenderThrottleMs = 200;

    private LookupMode _currentMode = LookupMode.Dictionary;
    private readonly LookupMode[] _modes = [LookupMode.Dictionary, LookupMode.Translate];
    private int _modeIndex;

    /// <summary>Whether the overlay is in persistent (quick-tap) mode.</summary>
    private bool _persistentMode;
    /// <summary>Whether the overlay is currently visible.</summary>
    private bool _isShown;
    /// <summary>Set when CapsLock down opens a fresh overlay, cleared on release.</summary>
    private bool _justOpened;
    /// <summary>Selected text grabbed from the focused app when CapsLock was pressed.</summary>
    private string? _grabbedSelectedText;
    /// <summary>True while an API lookup is actively streaming.</summary>
    private bool _isLookupInProgress;
    /// <summary>Guard flag: suppresses Deactivated during show/activate sequences to prevent flicker.</summary>
    private bool _isActivating;

    private static readonly MarkdownPipeline MarkdownPipeline = new MarkdownPipelineBuilder()
        .UseSupportedExtensions()
        .Build();

    // Cached frozen brushes to avoid GC pressure
    private static readonly SolidColorBrush TextBrush = CreateFrozenBrush(0xE0, 0xFF, 0xFF, 0xFF);
    private static readonly SolidColorBrush WhiteBrush = CreateFrozenBrush(0xFF, 0xFF, 0xFF, 0xFF);
    private static readonly SolidColorBrush ItalicBrush = CreateFrozenBrush(0xD0, 0xCC, 0xDD, 0xFF);
    private static readonly SolidColorBrush CodeFgBrush = CreateFrozenBrush(0xFF, 0xA0, 0xE0, 0xFF);
    private static readonly SolidColorBrush CodeBgBrush = CreateFrozenBrush(0x30, 0xFF, 0xFF, 0xFF);
    private static readonly FontFamily AppFontFamily = (FontFamily)Application.Current.FindResource("AppContentFont");

    private static SolidColorBrush CreateFrozenBrush(byte a, byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromArgb(a, r, g, b));
        brush.Freeze();
        return brush;
    }

    public OverlayWindow(
        IOpenAiService openAiService,
        SettingsService settingsService,
        HistoryService historyService,
        CapsLockService capsLockService,
        CursorTextService cursorTextService)
    {
        _openAiService = openAiService;
        _settingsService = settingsService;
        _historyService = historyService;
        _capsLockService = capsLockService;
        _cursorTextService = cursorTextService;

        InitializeComponent();

        // Cursor blink timer
        _cursorBlinkTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(530) };
        _cursorBlinkTimer.Tick += (_, _) =>
            BlinkingCursor.Opacity = BlinkingCursor.Opacity > 0.1 ? 0 : 0.8;

        // Auto-hide timer (hide result after delay)
        _autoHideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(120) };
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
        // Don't auto-close during an active API lookup — user can still Esc/CapsLock to close
        Deactivated += (_, _) =>
        {
            if (_isShown && !_isLookupInProgress)
                HideOverlay();
        };

        // Handle Tab key for mode switching (in the keyboard hook)
        PreviewKeyDown += (_, e) =>
        {
            // Tab always switches mode regardless of state
            if (e.Key == System.Windows.Input.Key.Tab)
            {
                _modeIndex = (_modeIndex + 1) % _modes.Length;
                _currentMode = _modes[_modeIndex];
                _userExplicitlySetMode = true;
                UpdateModeLabel();
                e.Handled = true;
                return;
            }

            // In persistent mode, let the TextBox handle input
            if (_persistentMode)
                return;

            // While viewing results, allow Escape to close but don't eat other keys
            if (ResultViewer.Visibility == Visibility.Visible)
            {
                if (e.Key == System.Windows.Input.Key.Escape)
                {
                    HideOverlay();
                    e.Handled = true;
                }
                // Ctrl+C: copy result to clipboard
                else if (e.Key == System.Windows.Input.Key.C &&
                         System.Windows.Input.Keyboard.Modifiers == System.Windows.Input.ModifierKeys.Control)
                {
                    var range = new TextRange(ResultViewer.Document.ContentStart, ResultViewer.Document.ContentEnd);
                    var text = range.Text.Trim();
                    if (!string.IsNullOrEmpty(text))
                    {
                        try { System.Windows.Clipboard.SetText(text); }
                        catch (System.Runtime.InteropServices.ExternalException) { }
                        StatusLabel.Text = "결과가 클립보드에 복사되었습니다";
                    }
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

        // Adjust font size dynamically as user types in persistent mode
        InputTextBox.TextChanged += (_, _) => AdjustInputFontSize(InputTextBox.Text);
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
                SafePerformLookup(input);
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
            _userExplicitlySetMode = true;
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

            // Grab selected text from the previously focused app BEFORE we steal focus
            _grabbedSelectedText = _cursorTextService.GetSelectedText();

            // Reset UI for new input
            InputDisplay.Text = _grabbedSelectedText ?? "";
            InputDisplay.FontWeight = FontWeights.Light;
            InputDisplay.Foreground = new SolidColorBrush(Colors.White);
            InputDisplay.TextWrapping = TextWrapping.Wrap;
            InputDisplay.TextTrimming = TextTrimming.CharacterEllipsis;
            InputDisplay.MaxHeight = 220;
            AdjustInputFontSize(InputDisplay.Text);
            InputDisplay.Visibility = Visibility.Visible;
            InputTextBox.Text = "";
            InputTextBox.Visibility = Visibility.Collapsed;
            ResultViewer.Document = new FlowDocument();
            ResultViewer.Visibility = Visibility.Collapsed;
            StopLoadingSpinner();
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
                InputTextBox.Text = _grabbedSelectedText ?? "";
                AdjustInputFontSize(InputTextBox.Text);
                InputTextBox.Visibility = Visibility.Visible;
                BlinkingCursor.Visibility = Visibility.Collapsed;
                ResultViewer.Document = new FlowDocument();
                ResultViewer.Visibility = Visibility.Collapsed;
                StopLoadingSpinner();
                HintLabel.Visibility = Visibility.Visible;
                UpdateModeLabel();

                if (!_isShown)
                {
                    ShowOverlay();
                }

                // Focus the TextBox for IME input — use ForceActivate to steal focus from other apps
                ForceActivate();
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, () =>
                {
                    ForceActivate();
                    InputTextBox.Focus();
                    System.Windows.Input.Keyboard.Focus(InputTextBox);
                    // Select all so typing replaces the pre-filled text
                    if (!string.IsNullOrEmpty(InputTextBox.Text))
                        InputTextBox.SelectAll();
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
            // If no typed input, fall back to grabbed selected text
            if (string.IsNullOrEmpty(input))
                input = _grabbedSelectedText?.Trim() ?? string.Empty;

            if (string.IsNullOrEmpty(input))
            {
                HideOverlay();
                return;
            }

            // Trigger lookup
            SafePerformLookup(input);
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
                    SafePerformLookup(input);
                }
                return;
            }

            _cursorBlinkTimer.Stop();
            BlinkingCursor.Visibility = Visibility.Collapsed;
            HintLabel.Visibility = Visibility.Collapsed;

            var bufferInput = _capsLockService.Buffer.Trim();
            if (string.IsNullOrEmpty(bufferInput)) return;

            SafePerformLookup(bufferInput);
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
                _userExplicitlySetMode = true;
                UpdateModeLabel();
                // Remove tab from buffer, preserving any text typed before it
                var cleaned = buffer.TrimEnd('\t');
                _capsLockService.SetBuffer(cleaned);
                return;
            }

            InputDisplay.Text = buffer;
            AdjustInputFontSize(buffer);
            UpdateCursorPosition();
        });
    }

    private void UpdateModeLabel()
    {
        ModeLabel.Text = _currentMode switch
        {
            LookupMode.Dictionary => "📖 사전",
            LookupMode.Translate => "🔄 번역",
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

    /// <summary>
    /// Dynamically adjusts input font size and cursor height based on text length.
    /// Short text (single word) keeps the large display font; longer text scales down
    /// progressively so that sentences and paragraphs fit within the overlay.
    /// </summary>
    private void AdjustInputFontSize(string text)
    {
        var length = text.Length;
        var sizePreset = _settingsService.Current.OverlaySize;

        // Font scale factor per overlay size (Small shrinks, Large grows)
        var fontScale = sizePreset switch
        {
            OverlaySize.Small => 0.75,
            OverlaySize.Large => 1.15,
            _ => 1.0,
        };

        var (displayFont, textBoxFont, cursorH) = length switch
        {
            <= 20 => (72.0, 64.0, 80.0),
            <= 60 => (40.0, 36.0, 46.0),
            <= 150 => (28.0, 26.0, 32.0),
            _ => (20.0, 18.0, 24.0),
        };

        InputDisplay.FontSize = displayFont * fontScale;
        InputTextBox.FontSize = textBoxFont * fontScale;
        BlinkingCursor.Height = cursorH * fontScale;
    }

    /// <summary>
    /// When lookup begins, shrinks the input area to a compact single-line summary
    /// with ellipsis so that the result area gets maximum space.
    /// </summary>
    private void CompactInputDisplay(string input)
    {
        const int maxDisplayChars = 50;
        var displayText = input.Length > maxDisplayChars
            ? string.Concat(input.AsSpan(0, maxDisplayChars), "…")
            : input;

        var sizeScale = _settingsService.Current.OverlaySize switch
        {
            OverlaySize.Small => 0.75,
            OverlaySize.Large => 1.15,
            _ => 1.0,
        };

        InputDisplay.Text = displayText;
        InputDisplay.FontSize = 18 * sizeScale;
        InputDisplay.FontWeight = FontWeights.Normal;
        InputDisplay.Foreground = new SolidColorBrush(Color.FromArgb(0x90, 0xFF, 0xFF, 0xFF));
        InputDisplay.TextWrapping = TextWrapping.NoWrap;
        InputDisplay.TextTrimming = TextTrimming.CharacterEllipsis;
        InputDisplay.MaxHeight = 30 * sizeScale;
        InputDisplay.Visibility = Visibility.Visible;

        InputTextBox.Visibility = Visibility.Collapsed;
        BlinkingCursor.Visibility = Visibility.Collapsed;
    }

    private bool _userExplicitlySetMode;

    /// <summary>
    /// Fire-and-forget wrapper that catches and logs exceptions from PerformLookupAsync,
    /// preventing unobserved task exceptions from crashing the app.
    /// </summary>
    private async void SafePerformLookup(string input)
    {
        try
        {
            await PerformLookupAsync(input);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[OverlayWindow] Lookup failed: {ex}");
        }
    }

    private async Task PerformLookupAsync(string input)
    {
        if (_isLookupInProgress) return;

        if (string.IsNullOrEmpty(_settingsService.Current.ApiKey))
        {
            StatusLabel.Text = "⚠ API Key가 설정되지 않았습니다. 트레이 아이콘 → 설정";
            _autoHideTimer.Start();
            return;
        }

        // Auto-select mode based on input length if user hasn't explicitly switched
        if (!_userExplicitlySetMode)
        {
            _currentMode = PromptBuilder.AutoSelectMode(input);
            _modeIndex = _currentMode == LookupMode.Dictionary ? 0 : 1;
            UpdateModeLabel();
        }

        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        _isLookupInProgress = true;

        // Compact input display only for Translate mode (long text → shrink to summary)
        // Dictionary mode keeps the original large word display
        if (_currentMode == LookupMode.Translate)
            CompactInputDisplay(input);

        // Show loading indicator until first chunk arrives
        ResultViewer.Visibility = Visibility.Collapsed;
        LoadingPanel.Visibility = Visibility.Visible;
        var spinnerStoryboard = (Storyboard)LoadingPanel.FindResource("SpinnerStoryboard");
        spinnerStoryboard.Begin(LoadingPanel, true);
        StatusLabel.Text = "";

        var src = _settingsService.Current.SourceLanguage;
        var tgt = _settingsService.Current.TargetLanguage;

        try
        {
            var sb = new StringBuilder();
            var firstChunk = true;
            await foreach (var chunk in _openAiService.StreamCompletionAsync(
                input, _currentMode, src, tgt, ct))
            {
                if (firstChunk)
                {
                    firstChunk = false;
                    StopLoadingSpinner();
                    ResultViewer.Visibility = Visibility.Visible;
                }
                sb.Append(chunk);

                // Throttled rendering during streaming
                var now = DateTime.UtcNow;
                if ((now - _lastRenderTime).TotalMilliseconds >= RenderThrottleMs)
                {
                    _lastRenderTime = now;
                    RenderPlainText(sb.ToString());
                    await Task.Yield(); // Release UI thread to paint
                }
            }

            // Final render with full Markdown formatting
            RenderMarkdown(sb.ToString());

            StatusLabel.Text = "완료 — Ctrl+C: 복사 · Esc: 닫기";

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
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            StopLoadingSpinner();
            ResultViewer.Visibility = Visibility.Visible;
            RenderMarkdown("## \u23f0 시간 초과\n\n응답 시간이 초과되었습니다. 다시 시도해주세요.");
            StatusLabel.Text = "시간 초과";
            _autoHideTimer.Start();
        }
        catch (OperationCanceledException)
        {
            // Cancelled by new CapsLock press
            StopLoadingSpinner();
        }
        catch (Exception ex)
        {
            StopLoadingSpinner();
            ResultViewer.Visibility = Visibility.Visible;
            RenderMarkdown($"## \u274c 오류\n\n{ex.Message}\n\nAPI Key와 설정을 확인해주세요.");
            StatusLabel.Text = "오류 발생";
            _autoHideTimer.Start();
        }
        finally
        {
            _isLookupInProgress = false;
        }
    }

    private void StopLoadingSpinner()
    {
        LoadingPanel.Visibility = Visibility.Collapsed;
        if (LoadingPanel.FindResource("SpinnerStoryboard") is Storyboard sb)
            sb.Stop(LoadingPanel);
    }

    private void ResetAutoHideTimer()
    {
        if (_autoHideTimer.IsEnabled)
        {
            _autoHideTimer.Stop();
            _autoHideTimer.Start();
        }
    }

    private double GetDpiScale()
    {
        var source = PresentationSource.FromVisual(this);
        return source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
    }

    /// <summary>
    /// Forces the overlay to the foreground using Win32 AttachThreadInput trick.
    /// WPF's Activate() alone may fail when another app holds foreground lock.
    /// Sets <see cref="_isActivating"/> to suppress Deactivated during the process.
    /// </summary>
    private void ForceActivate()
    {
        _isActivating = true;
        try
        {
            var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;

            var foregroundHwnd = NativeMethods.GetForegroundWindow();
            if (foregroundHwnd != IntPtr.Zero && foregroundHwnd != hwnd)
            {
                var foregroundThread = NativeMethods.GetWindowThreadProcessId(foregroundHwnd, out _);
                var currentThread = NativeMethods.GetCurrentThreadId();

                if (foregroundThread != currentThread)
                {
                    NativeMethods.AttachThreadInput(foregroundThread, currentThread, true);
                    NativeMethods.SetForegroundWindow(hwnd);
                    NativeMethods.AttachThreadInput(foregroundThread, currentThread, false);
                }
                else
                {
                    NativeMethods.SetForegroundWindow(hwnd);
                }
            }

            Activate();
        }
        finally
        {
            // Clear the guard after a short delay so any queued Deactivated events are suppressed
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, () => _isActivating = false);
        }
    }

    private void ShowOverlay()
    {
        var workArea = SystemParameters.WorkArea;
        var dpiScale = GetDpiScale();
        var textLength = (_grabbedSelectedText ?? "").Length;

        // Base sizes per OverlaySize setting (in DIPs)
        var sizePreset = _settingsService.Current.OverlaySize;
        var (baseW, baseH, longW, longH) = sizePreset switch
        {
            OverlaySize.Small  => (540.0, 440.0, 680.0, 560.0),
            OverlaySize.Large  => (900.0, 740.0, 1100.0, 900.0),
            _                  => (700.0, 600.0, 900.0, 750.0), // Medium
        };

        // Expand overlay for longer text (e.g. grabbed paragraphs, translation input)
        if (textLength > 100)
        {
            Width = Math.Min(longW, workArea.Width * 0.65);
            Height = Math.Min(longH, workArea.Height * 0.85);
        }
        else
        {
            Width = Math.Min(baseW, workArea.Width * 0.5);
            Height = Math.Min(baseH, workArea.Height * 0.7);
        }

        var pos = _settingsService.Current.PopupPosition;
        var margin = 20.0;

        Left = pos switch
        {
            OverlayPosition.TopLeft or OverlayPosition.CenterLeft or OverlayPosition.BottomLeft => workArea.Left + margin,
            OverlayPosition.TopRight or OverlayPosition.CenterRight or OverlayPosition.BottomRight => workArea.Right - Width - margin,
            _ => workArea.Left + (workArea.Width - Width) / 2
        };

        Top = pos switch
        {
            OverlayPosition.TopLeft or OverlayPosition.TopCenter or OverlayPosition.TopRight => workArea.Top + margin,
            OverlayPosition.BottomLeft or OverlayPosition.BottomCenter or OverlayPosition.BottomRight => workArea.Bottom - Height - margin,
            _ => workArea.Top + (workArea.Height - Height) / 2
        };

        _isShown = true;
        _isActivating = true;

        // Use Window.Opacity for fade — immune to WPF-UI theme changes
        BeginAnimation(OpacityProperty, null);
        Opacity = 0;

        // Always Hide+Show so the window moves to the current virtual desktop
        if (IsVisible)
        {
            Hide();
        }
        Show();

        ForceActivate();

        var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(120));
        fadeIn.FillBehavior = FillBehavior.Stop;
        fadeIn.Completed += (_, _) => Opacity = 1;
        BeginAnimation(OpacityProperty, fadeIn);
    }

    public void HideOverlay()
    {
        if (!_isShown) return;

        _autoHideTimer.Stop();
        _cursorBlinkTimer.Stop();
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        _isShown = false;
        _persistentMode = false;
        _userExplicitlySetMode = false;
        _capsLockService.PersistentModeActive = false;

        // Reset TextBox/TextBlock visibility
        InputTextBox.Visibility = Visibility.Collapsed;
        InputDisplay.Visibility = Visibility.Visible;

        var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(150));
        fadeOut.FillBehavior = FillBehavior.Stop;
        fadeOut.Completed += (_, _) =>
        {
            BeginAnimation(OpacityProperty, null);
            Opacity = 0;

            // Only move off-screen if overlay hasn't been re-shown during fade
            if (!_isShown)
            {
                Left = -9999;
                Top = -9999;
            }
        };
        BeginAnimation(OpacityProperty, fadeOut);
    }

    /// <summary>
    /// Closes the overlay for app shutdown, bypassing hide/fade logic.
    /// </summary>
    public void CloseForShutdown()
    {
        _isShown = false;
        _autoHideTimer.Stop();
        _cursorBlinkTimer.Stop();
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;

        // Unsubscribe to avoid Deactivated handler firing during close
        _capsLockService.CapsLockPressed -= OnCapsLockPressed;
        _capsLockService.QuickTapReleased -= OnQuickTapReleased;
        _capsLockService.LongPressReleased -= OnLongPressReleased;
        _capsLockService.BufferChanged -= OnBufferChanged;
        _capsLockService.EnterPressed -= OnEnterPressed;

        Close();
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
        "Portuguese" => "PT",
        "Russian" => "RU",
        "Arabic" => "AR",
        "Italian" => "IT",
        "Dutch" => "NL",
        "Vietnamese" => "VI",
        "Thai" => "TH",
        "Indonesian" => "ID",
        _ => lang.Length >= 2 ? lang[..2].ToUpperInvariant() : lang.ToUpperInvariant()
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
        var run = new Run(text);
        var para = new Paragraph(run);
        var doc = new FlowDocument(para)
        {
            Foreground = TextBrush,
            FontSize = 16,
            FontFamily = AppFontFamily,
            PagePadding = new Thickness(0)
        };
        ResultViewer.Document = doc;
    }

    /// <summary>
    /// Recursively apply bright colors to all elements for dark overlay background.
    /// </summary>
    private static void ApplyDarkThemeToDocument(FlowDocument doc)
    {
        var baseFontSize = 16.0;
        doc.Foreground = TextBrush;
        doc.FontSize = baseFontSize;
        doc.FontFamily = AppFontFamily;
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
            table.Foreground = TextBrush;
        }
    }

    private static void ApplyDarkThemeToInline(System.Windows.Documents.Inline inline)
    {
        inline.Foreground = TextBrush;

        if (inline is System.Windows.Documents.Bold bold)
        {
            bold.Foreground = WhiteBrush;
            foreach (var child in bold.Inlines)
                ApplyDarkThemeToInline(child);
        }
        else if (inline is System.Windows.Documents.Italic italic)
        {
            italic.Foreground = ItalicBrush;
            foreach (var child in italic.Inlines)
                ApplyDarkThemeToInline(child);
        }
        else if (inline is System.Windows.Documents.Run run && run.Background != null)
        {
            run.Foreground = CodeFgBrush;
            run.Background = CodeBgBrush;
        }
        else if (inline is System.Windows.Documents.Span span)
        {
            foreach (var child in span.Inlines)
                ApplyDarkThemeToInline(child);
        }
    }
}
