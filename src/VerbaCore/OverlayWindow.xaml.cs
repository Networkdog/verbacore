using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Interop;
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
    private readonly LookupCacheService _cacheService;

    private readonly DispatcherTimer _autoHideTimer;
    private CancellationTokenSource? _cts;
    private DateTime _lastRenderTime = DateTime.MinValue;
    private const int RenderThrottleMs = 200;

    // Cached FlowDocument/Run for plain-text streaming ??avoids creating new objects every 200ms
    private Run? _streamingRun;
    private FlowDocument? _streamingDoc;

    private LookupMode _currentMode = LookupMode.Dictionary;
    private readonly LookupMode[] _modes = [LookupMode.Dictionary, LookupMode.Translate, LookupMode.Assist];
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
    /// <summary>The most recent input that was looked up, so Tab can re-run with a new mode.</summary>
    private string? _lastLookupInput;
    // Global mouse hook to detect clicks outside the overlay
    private IntPtr _mouseHookId = IntPtr.Zero;
    private NativeMethods.LowLevelMouseProc? _mouseHookProc;

    private static readonly MarkdownPipeline MarkdownPipeline = new MarkdownPipelineBuilder()
        .UseSupportedExtensions()
        .Build();

    // Cached frozen brushes to avoid GC pressure
    private static readonly SolidColorBrush TextBrush = CreateFrozenBrush(0xE0, 0xFF, 0xFF, 0xFF);

    // GPU-accelerated cursor blink animation (replaces DispatcherTimer that fired 25x/sec on UI thread)
    private Storyboard? _cursorBlinkStoryboard;
    private static readonly SolidColorBrush WhiteBrush = CreateFrozenBrush(0xFF, 0xFF, 0xFF, 0xFF);
    private static readonly SolidColorBrush ItalicBrush = CreateFrozenBrush(0xD0, 0xCC, 0xDD, 0xFF);
    private static readonly SolidColorBrush CodeFgBrush = CreateFrozenBrush(0xFF, 0xF0, 0xF4, 0xFF);
    private static readonly SolidColorBrush CodeBgBrush = CreateFrozenBrush(0x55, 0xFF, 0xFF, 0xFF);
    private static readonly SolidColorBrush CompactInputBrush = CreateFrozenBrush(0x90, 0xFF, 0xFF, 0xFF);
    private static readonly SolidColorBrush HeadingBrush = CreateFrozenBrush(0xFF, 0x60, 0xCD, 0xFF);
    private static readonly SolidColorBrush HeadingSubBrush = CreateFrozenBrush(0xFF, 0x90, 0xD0, 0xF0);
    private static readonly SolidColorBrush BlockquoteBgBrush = CreateFrozenBrush(0x18, 0x80, 0xC0, 0xFF);
    private static readonly SolidColorBrush BlockquoteBorderBrush = CreateFrozenBrush(0x60, 0x60, 0xCD, 0xFF);
    private static readonly SolidColorBrush HrBrush = CreateFrozenBrush(0x30, 0xFF, 0xFF, 0xFF);
    private static readonly FontFamily AppFontFamily = (FontFamily)Application.Current.FindResource("AppContentFont");
    private static readonly FontFamily CodeFontFamily = (FontFamily)Application.Current.FindResource("AppCodeFont");

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
        CursorTextService cursorTextService,
        LookupCacheService cacheService)
    {
        _openAiService = openAiService;
        _settingsService = settingsService;
        _historyService = historyService;
        _capsLockService = capsLockService;
        _cursorTextService = cursorTextService;
        _cacheService = cacheService;

        InitializeComponent();

        // Cursor blink: GPU-accelerated WPF animation instead of DispatcherTimer
        // This avoids 25 UI-thread callbacks/sec; WPF animations run on the composition thread.
        _cursorBlinkStoryboard = new Storyboard { RepeatBehavior = RepeatBehavior.Forever };
        var cursorAnim = new DoubleAnimation(0.3, 0.8, TimeSpan.FromMilliseconds(800))
        {
            AutoReverse = true,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        };
        Storyboard.SetTarget(cursorAnim, BlinkingCursor);
        Storyboard.SetTargetProperty(cursorAnim, new PropertyPath(OpacityProperty));
        _cursorBlinkStoryboard.Children.Add(cursorAnim);

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
                AnimateModeLabelSwitch();
                // Re-run the last lookup with the newly selected mode
                RerunLastLookupOnModeChange();
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
                        StatusLabel.Text = Loc("Overlay_StatusCopied");
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
                _cursorBlinkStoryboard?.Stop();
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
            AnimateModeLabelSwitch();
            // Re-run the last lookup with the newly selected mode
            RerunLastLookupOnModeChange();
        }
    }

    /// <summary>
    /// If a previous lookup result is on screen, re-run it with the current mode
    /// so switching mode via Tab actually updates the result.
    /// </summary>
    private void RerunLastLookupOnModeChange()
    {
        if (string.IsNullOrEmpty(_lastLookupInput)) return;
        if (_isLookupInProgress)
        {
            _cts?.Cancel();
            _isLookupInProgress = false;
        }
        SafePerformLookup(_lastLookupInput);
    }

    private void OnCapsLockPressed(object? sender, EventArgs e)
    {
        // BeginInvoke (not Invoke): the hook now runs on a dedicated thread, so this
        // must not block it. Returning immediately lets the hook suppress CapsLock
        // within the OS timeout regardless of how long the UI work below takes.
        Dispatcher.BeginInvoke(() =>
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
            InputDisplay.Foreground = WhiteBrush;
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
            StatusLabel.Text = Loc("Overlay_StatusDefault");

            UpdateModeLabel();
            UpdateCursorPosition();

            // Show overlay (Enso-style hold mode for now ??may become persistent on quick tap)
            if (!_isShown)
            {
                ShowOverlay();
            }
            _cursorBlinkStoryboard?.Begin();
        });
    }

    /// <summary>
    /// Quick tap: CapsLock pressed and released quickly with no typing.
    /// Toggle persistent search overlay on/off.
    /// </summary>
    private void OnQuickTapReleased(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (_isShown && !_justOpened)
            {
                // Overlay was already showing before this CapsLock press ??close it
                _capsLockService.PersistentModeActive = false;
                _persistentMode = false;
                _cursorBlinkStoryboard?.Stop();
                HideOverlay();
            }
            else
            {
                _justOpened = false;
                // First quick tap ??enter persistent mode with IME TextBox
                _persistentMode = true;
                _capsLockService.PersistentModeActive = false; // Don't intercept keys ??let TextBox handle them

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

                // If we grabbed selected text, auto-trigger lookup immediately
                if (!string.IsNullOrWhiteSpace(_grabbedSelectedText))
                {
                    HintLabel.Visibility = Visibility.Collapsed;
                    SafePerformLookup(_grabbedSelectedText.Trim());
                }
                else
                {
                    StatusLabel.Text = Loc("Overlay_StatusInputPrompt");

                    // Focus the TextBox for IME input ??use ForceActivate to steal focus from other apps
                    ForceActivate();
                    Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, () =>
                    {
                        ForceActivate();
                        InputTextBox.Focus();
                        System.Windows.Input.Keyboard.Focus(InputTextBox);
                    });
                }
            }
        });
    }

    /// <summary>
    /// Long press: CapsLock held ??.5s or typed while held.
    /// Perform lookup (if there's input) and auto-hide.
    /// </summary>
    private void OnLongPressReleased(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            _persistentMode = false;
            _capsLockService.PersistentModeActive = false;
            _cursorBlinkStoryboard?.Stop();
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
    /// Enter pressed in persistent mode (from keyboard hook) ??trigger lookup.
    /// </summary>
    private void OnEnterPressed(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
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

            _cursorBlinkStoryboard?.Stop();
            BlinkingCursor.Visibility = Visibility.Collapsed;
            HintLabel.Visibility = Visibility.Collapsed;

            var bufferInput = _capsLockService.Buffer.Trim();
            if (string.IsNullOrEmpty(bufferInput)) return;

            SafePerformLookup(bufferInput);
        });
    }

    private void OnBufferChanged(object? sender, string buffer)
    {
        Dispatcher.BeginInvoke(() =>
        {
            // Check for Tab (mode switch) ??handle before display
            if (buffer.EndsWith('\t'))
            {
                _modeIndex = (_modeIndex + 1) % _modes.Length;
                _currentMode = _modes[_modeIndex];
                _userExplicitlySetMode = true;
                UpdateModeLabel();
                AnimateModeLabelSwitch();
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

    private static string Loc(string key) =>
        Application.Current.TryFindResource(key) as string ?? key;

    private void UpdateModeLabel()
    {
        ModeLabel.Text = _currentMode switch
        {
            LookupMode.Dictionary => Loc("Overlay_ModeDict"),
            LookupMode.Translate => Loc("Overlay_ModeTrans"),
            LookupMode.Assist => Loc("Overlay_ModeAssist"),
            _ => Loc("Overlay_ModeDict")
        };
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
    /// When lookup begins, hides the input area entirely so the result area gets
    /// maximum space. (Previously showed a truncated single-line summary with
    /// ellipsis, but the original text is already echoed in the result for
    /// Translate mode and is unnecessary for Dictionary/Assist modes.)
    /// </summary>
    private void CompactInputDisplay(string input)
    {
        _ = input;
        InputDisplay.Text = string.Empty;
        InputDisplay.Visibility = Visibility.Collapsed;
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
            StatusLabel.Text = "??API Key가 ?�정?��? ?�았?�니?? ?�레???�이�????�정";
            _autoHideTimer.Start();
            return;
        }

        _lastLookupInput = input;

        // Auto-select mode based on input length if user hasn't explicitly switched
        if (!_userExplicitlySetMode)
        {
            _currentMode = PromptBuilder.AutoSelectMode(input);
            _modeIndex = _currentMode switch
            {
                LookupMode.Dictionary => 0,
                LookupMode.Translate => 1,
                LookupMode.Assist => 2,
                _ => 0
            };
            UpdateModeLabel();
        }

        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        _isLookupInProgress = true;

        // Reset streaming cache so RenderPlainText creates a fresh document
        _streamingRun = null;
        _streamingDoc = null;

        // Keep the input text visible during the loading phase so it never disappears
        // mid-lookup. The input is compacted (for Translate/Assist modes) only once the
        // result is ready to render (see the cache-hit and first-chunk paths below).

        // Show loading indicator until first chunk arrives
        ResultViewer.Visibility = Visibility.Collapsed;
        LoadingPanel.Visibility = Visibility.Visible;
        var spinnerStoryboard = (Storyboard)LoadingPanel.FindResource("SpinnerStoryboard");
        spinnerStoryboard.Begin(LoadingPanel, true);
        StatusLabel.Text = "";

        var src = _settingsService.Current.NativeLanguage;
        var tgt = _settingsService.Current.ForeignLanguage;

        // Cache lookup — short-circuit before consuming LLM tokens
        var cacheKey = LookupCacheService.MakeKey(
            _settingsService.Current.Provider.ToString(),
            _settingsService.Current.Model,
            _currentMode, src, tgt, input);
        if (_settingsService.Current.EnableLookupCache && _cacheService.TryGet(cacheKey, out var cachedResponse))
        {
            StopLoadingSpinner();
            if (_currentMode is LookupMode.Translate or LookupMode.Assist)
                CompactInputDisplay(input);
            ShowResultViewer();
            RenderMarkdown(cachedResponse);
            StatusLabel.Text = Loc("Overlay_StatusCached");
            await _historyService.AddAsync(new LookupHistoryItem
            {
                Input = input
            });
            _autoHideTimer.Start();
            _isLookupInProgress = false;
            return;
        }

        try
        {
            var sb = new StringBuilder(1024); // Pre-allocate for typical response size
            var firstChunk = true;
            await foreach (var chunk in _openAiService.StreamCompletionAsync(
                input, _currentMode, src, tgt, ct))
            {
                if (firstChunk)
                {
                    firstChunk = false;
                    StopLoadingSpinner();
                    if (_currentMode is LookupMode.Translate or LookupMode.Assist)
                        CompactInputDisplay(input);
                    ShowResultViewer();
                }
                sb.Append(chunk);

                // Throttled rendering during streaming
                var now = DateTime.UtcNow;
                if ((now - _lastRenderTime).TotalMilliseconds >= RenderThrottleMs)
                {
                    _lastRenderTime = now;
                    // Render formatted Markdown live during streaming (not raw source).
                    RenderMarkdown(sb.ToString());
                    await Task.Yield(); // Release UI thread to paint
                }
            }

            // Final render with full Markdown formatting
            RenderMarkdown(sb.ToString());

            StatusLabel.Text = Loc("Overlay_StatusDone");

            // Cache successful response (skip empty)
            if (_settingsService.Current.EnableLookupCache && sb.Length > 0)
            {
                _cacheService.Put(cacheKey, sb.ToString());
            }

            // Save to history
            await _historyService.AddAsync(new LookupHistoryItem
            {
                Input = input
            });

            // Auto-hide after delay
            _autoHideTimer.Start();
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            StopLoadingSpinner();
            ShowResultViewer();
            RenderMarkdown(Loc("Overlay_TimeoutMarkdown"));
            StatusLabel.Text = Loc("Overlay_StatusTimeout");
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
            ShowResultViewer();
            RenderMarkdown(string.Format(Loc("Overlay_ErrorMarkdown"), ex.Message));
            StatusLabel.Text = Loc("Overlay_StatusError");
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

    /// <summary>
    /// Shows the ResultViewer with a quick fade-in animation.
    /// </summary>
    private void ShowResultViewer()
    {
        if (ResultViewer.Visibility == Visibility.Visible) return;
        ResultViewer.Visibility = Visibility.Visible;
        ResultViewer.BeginAnimation(OpacityProperty, null);
        ResultViewer.Opacity = 0;
        var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.Stop
        };
        fade.Completed += (_, _) => ResultViewer.Opacity = 1;
        ResultViewer.BeginAnimation(OpacityProperty, fade);
    }

    /// <summary>
    /// Animates the mode label with a brief flash when mode changes.
    /// </summary>
    private void AnimateModeLabelSwitch()
    {
        var flash = new DoubleAnimation(0.3, 1, TimeSpan.FromMilliseconds(250))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.Stop
        };
        flash.Completed += (_, _) => ModeLabel.Opacity = 1;
        ModeLabel.BeginAnimation(OpacityProperty, flash);
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
    /// </summary>
    private void ForceActivate()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
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

    /// <summary>
    /// Forces WPF to build and render the overlay's visual tree once, off-screen and
    /// without activation, so the first real <see cref="ShowOverlay"/> appears instantly
    /// instead of paying the one-time layout/render cost on the critical path.
    /// Called once at startup during idle time.
    /// </summary>
    public void PrimeRender()
    {
        if (_isShown || IsVisible) return;
        try
        {
            ShowActivated = false;   // don't steal focus from the user's current app
            Left = -10000;
            Top = -10000;
            Opacity = 0;
            Show();
            UpdateLayout();          // force a synchronous measure/arrange pass
            Hide();
            Opacity = 0;
            Left = -9999;
            Top = -9999;
            ShowActivated = true;    // restore normal activation for real shows
        }
        catch
        {
            // Priming is best-effort; a failure here must never affect startup.
        }
    }

    private void ShowOverlay()
    {
        var workArea = SystemParameters.WorkArea;

        // Window size strictly follows the OverlaySize setting; do not auto-expand by mode/text length.
        var sizePreset = _settingsService.Current.OverlaySize;
        var (baseW, baseH) = sizePreset switch
        {
            OverlaySize.Small  => (540.0, 440.0),
            OverlaySize.Large  => (900.0, 740.0),
            _                  => (700.0, 600.0), // Medium
        };

        Width = Math.Min(baseW, workArea.Width * 0.9);
        Height = Math.Min(baseH, workArea.Height * 0.9);

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

        // Reset animation state
        BeginAnimation(OpacityProperty, null);
        Opacity = 0;
        ContentScale.ScaleX = 0.92;
        ContentScale.ScaleY = 0.92;
        ContentTranslate.Y = 18;

        // Always Hide+Show so the window moves to the current virtual desktop
        if (IsVisible)
        {
            Hide();
        }
        Show();

        ForceActivate();
        InstallMouseHook();

        // Entrance animation: fade + scale up + slide up
        var duration = TimeSpan.FromMilliseconds(220);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        var fadeIn = new DoubleAnimation(0, 1, duration) { EasingFunction = ease, FillBehavior = FillBehavior.Stop };
        fadeIn.Completed += (_, _) => Opacity = 1;

        var scaleXIn = new DoubleAnimation(0.92, 1, duration) { EasingFunction = ease, FillBehavior = FillBehavior.Stop };
        scaleXIn.Completed += (_, _) => ContentScale.ScaleX = 1;

        var scaleYIn = new DoubleAnimation(0.92, 1, duration) { EasingFunction = ease, FillBehavior = FillBehavior.Stop };
        scaleYIn.Completed += (_, _) => ContentScale.ScaleY = 1;

        var slideIn = new DoubleAnimation(18, 0, duration) { EasingFunction = ease, FillBehavior = FillBehavior.Stop };
        slideIn.Completed += (_, _) => ContentTranslate.Y = 0;

        BeginAnimation(OpacityProperty, fadeIn);
        ContentScale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleXIn);
        ContentScale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleYIn);
        ContentTranslate.BeginAnimation(TranslateTransform.YProperty, slideIn);
    }

    public void HideOverlay()
    {
        if (!_isShown) return;

        _autoHideTimer.Stop();
        _cursorBlinkStoryboard?.Stop();
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        _isShown = false;
        _persistentMode = false;
        _userExplicitlySetMode = false;
        _lastLookupInput = null;
        _capsLockService.PersistentModeActive = false;
        UninstallMouseHook();

        // Reset TextBox/TextBlock visibility
        InputTextBox.Visibility = Visibility.Collapsed;
        InputDisplay.Visibility = Visibility.Visible;

        // Exit animation: fade + scale down + slide down
        var duration = TimeSpan.FromMilliseconds(180);
        var ease = new CubicEase { EasingMode = EasingMode.EaseIn };

        var fadeOut = new DoubleAnimation(1, 0, duration) { EasingFunction = ease, FillBehavior = FillBehavior.Stop };
        fadeOut.Completed += (_, _) =>
        {
            BeginAnimation(OpacityProperty, null);
            Opacity = 0;
            // Reset transforms
            ContentScale.ScaleX = 1;
            ContentScale.ScaleY = 1;
            ContentTranslate.Y = 0;

            // Only move off-screen if overlay hasn't been re-shown during fade
            if (!_isShown)
            {
                Left = -9999;
                Top = -9999;
            }
        };

        var scaleXOut = new DoubleAnimation(1, 0.95, duration) { EasingFunction = ease, FillBehavior = FillBehavior.Stop };
        scaleXOut.Completed += (_, _) => ContentScale.ScaleX = 0.95;

        var scaleYOut = new DoubleAnimation(1, 0.95, duration) { EasingFunction = ease, FillBehavior = FillBehavior.Stop };
        scaleYOut.Completed += (_, _) => ContentScale.ScaleY = 0.95;

        var slideOut = new DoubleAnimation(0, 10, duration) { EasingFunction = ease, FillBehavior = FillBehavior.Stop };
        slideOut.Completed += (_, _) => ContentTranslate.Y = 10;

        BeginAnimation(OpacityProperty, fadeOut);
        ContentScale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleXOut);
        ContentScale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleYOut);
        ContentTranslate.BeginAnimation(TranslateTransform.YProperty, slideOut);
    }

    /// <summary>
    /// Closes the overlay for app shutdown, bypassing hide/fade logic.
    /// </summary>
    public void CloseForShutdown()
    {
        _isShown = false;
        _autoHideTimer.Stop();
        _cursorBlinkStoryboard?.Stop();
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        UninstallMouseHook();

        // Unsubscribe to avoid Deactivated handler firing during close
        _capsLockService.CapsLockPressed -= OnCapsLockPressed;
        _capsLockService.QuickTapReleased -= OnQuickTapReleased;
        _capsLockService.LongPressReleased -= OnLongPressReleased;
        _capsLockService.BufferChanged -= OnBufferChanged;
        _capsLockService.EnterPressed -= OnEnterPressed;

        Close();
    }

    #region Global Mouse Hook ??click-outside detection

    private void InstallMouseHook()
    {
        if (_mouseHookId != IntPtr.Zero) return;
        _mouseHookProc = MouseHookCallback;
        _mouseHookId = NativeMethods.SetWindowsHookEx(
            NativeMethods.WH_MOUSE_LL, _mouseHookProc,
            NativeMethods.CachedModuleHandle, 0);
    }

    private void UninstallMouseHook()
    {
        if (_mouseHookId == IntPtr.Zero) return;
        NativeMethods.UnhookWindowsHookEx(_mouseHookId);
        _mouseHookId = IntPtr.Zero;
        _mouseHookProc = null;
    }

    private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && _isShown)
        {
            var msg = (int)wParam;
            if (msg is NativeMethods.WM_LBUTTONDOWN or NativeMethods.WM_RBUTTONDOWN
                    or NativeMethods.WM_MBUTTONDOWN or NativeMethods.WM_NCLBUTTONDOWN)
            {
                var hookData = System.Runtime.InteropServices.Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam);
                var hwnd = new WindowInteropHelper(this).Handle;
                if (hwnd != IntPtr.Zero
                    && NativeMethods.GetWindowRect(hwnd, out var rect)
                    && !PtInRect(rect, hookData.pt))
                {
                    // Click was outside the overlay ??hide it on the UI thread
                    Dispatcher.BeginInvoke(() =>
                    {
                        if (_isShown)
                            HideOverlay();
                    });
                }
            }
        }
        return NativeMethods.CallNextHookEx(_mouseHookId, nCode, wParam, lParam);
    }

    private static bool PtInRect(NativeMethods.RECT rect, NativeMethods.POINT pt)
        => pt.X >= rect.Left && pt.X < rect.Right && pt.Y >= rect.Top && pt.Y < rect.Bottom;

    #endregion

    private void RenderMarkdown(string markdown)
    {
        // Release streaming cache before building final Markdown document
        _streamingRun = null;
        _streamingDoc = null;

        try
        {
            var doc = Markdig.Wpf.Markdown.ToFlowDocument(NormalizeMarkdown(markdown), MarkdownPipeline);
            ApplyDarkThemeToDocument(doc);
            ResultViewer.Document = doc;
        }
        catch
        {
            RenderPlainText(markdown);
        }
    }

    /// <summary>
    /// Some models (e.g. gpt-5.x) wrap their entire answer in a fenced code block
    /// (```markdown ... ``` or even a bare ``` ... ```), which Markdig renders as a
    /// literal code block — so the user sees raw source instead of a formatted document.
    /// If the whole response parses to a single code block, unwrap it. Real language
    /// blocks (```python, ```json, ...) are left intact so Assist-mode code still renders
    /// as code. Works mid-stream too: an unclosed fence parses as a code block to end.
    /// </summary>
    private static string NormalizeMarkdown(string markdown)
    {
        if (string.IsNullOrEmpty(markdown)) return markdown;

        try
        {
            var parsed = Markdig.Markdown.Parse(markdown, MarkdownPipeline);
            if (parsed.Count == 1 && parsed[0] is Markdig.Syntax.CodeBlock cb)
            {
                var info = (cb as Markdig.Syntax.FencedCodeBlock)?.Info;
                if (string.IsNullOrEmpty(info)
                    || info.Equals("markdown", StringComparison.OrdinalIgnoreCase)
                    || info.Equals("md", StringComparison.OrdinalIgnoreCase))
                {
                    return cb.Lines.ToString();
                }
            }
        }
        catch
        {
            // On any parse issue, fall through and render the original markdown as-is.
        }

        return markdown;
    }

    /// <summary>
    /// Fast plain-text rendering used during streaming (no Markdown parsing overhead).
    /// Reuses the same FlowDocument and Run to avoid GC pressure from creating
    /// new WPF visual tree objects every 200ms.
    /// </summary>
    private void RenderPlainText(string text)
    {
        if (_streamingRun != null && _streamingDoc != null)
        {
            _streamingRun.Text = text;
            return;
        }

        _streamingRun = new Run(text);
        var para = new Paragraph(_streamingRun);
        _streamingDoc = new FlowDocument(para)
        {
            Foreground = TextBrush,
            FontSize = 16,
            FontFamily = AppFontFamily,
            PagePadding = new Thickness(0)
        };
        ResultViewer.Document = _streamingDoc;
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
        block.Foreground = TextBrush;

        if (block is Paragraph p)
        {
            var pFontSize = p.FontSize;
            bool isHeading = !double.IsNaN(pFontSize) && pFontSize != baseFontSize;

            if (isHeading)
            {
                double ratio = pFontSize / 12.0;
                p.FontSize = baseFontSize * Math.Max(ratio, 1.2);
                p.Foreground = ratio > 1.3 ? HeadingBrush : HeadingSubBrush;
                p.FontWeight = System.Windows.FontWeights.Bold;
                p.Margin = new Thickness(0, 10, 0, 4);
            }

            // Detect horizontal rule: empty paragraph
            if (p.Inlines.Count == 0)
            {
                p.BorderBrush = HrBrush;
                p.BorderThickness = new Thickness(0, 0, 0, 1);
                p.Margin = new Thickness(0, 12, 0, 12);
            }

            foreach (var inline in p.Inlines)
            {
                ApplyDarkThemeToInline(inline);
            }
        }
        else if (block is System.Windows.Documents.List list)
        {
            list.Margin = new Thickness(16, 4, 0, 4);
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
            // Blockquotes ??left accent border + subtle background
            section.Background = BlockquoteBgBrush;
            section.BorderBrush = BlockquoteBorderBrush;
            section.BorderThickness = new Thickness(3, 0, 0, 0);
            section.Padding = new Thickness(14, 10, 14, 10);
            section.Margin = new Thickness(0, 8, 0, 8);

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
            run.FontFamily = CodeFontFamily;
        }
        else if (inline is System.Windows.Documents.Span span)
        {
            foreach (var child in span.Inlines)
                ApplyDarkThemeToInline(child);
        }
    }
}
