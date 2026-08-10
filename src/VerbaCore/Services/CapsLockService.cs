using System.Diagnostics;
using System.Runtime.InteropServices;
using VerbaCore.Helpers;

namespace VerbaCore.Services;

/// <summary>
/// Intercepts CapsLock key globally with two modes:
/// - Quick tap (&lt;0.5s, no typing): toggles persistent search overlay
/// - Long press (≥0.5s or typing while held): Enso-style, overlay hides on release
/// </summary>
/// <remarks>
/// The hook runs on its own dedicated thread with a private message pump. Windows
/// dispatches a WH_KEYBOARD_LL callback on the thread that installed it and lets the key
/// through unhooked if the callback does not return within LowLevelHooksTimeout (300 ms
/// by default) — which is what happens when the hook lives on a busy or paged-out WPF UI
/// thread. Handlers of the events raised here therefore MUST NOT block; marshal to the UI
/// thread with <c>Dispatcher.BeginInvoke</c>.
/// </remarks>
public sealed class CapsLockService : IDisposable
{
    private const int LongPressThresholdMs = 500;

    /// <summary>Private message posted to the hook thread to re-arm the hook.</summary>
    private const uint WM_APP_REARM = 0x8000 + 1;

    /// <summary>
    /// Interval at which the hook is torn down and reinstalled. Guards against the hook
    /// being silently dropped by Windows (timeout, session/desktop switch, UAC prompt) and
    /// keeps the callback's code path resident in the working set while the app sits idle.
    /// </summary>
    private static readonly TimeSpan RearmInterval = TimeSpan.FromSeconds(45);

    private IntPtr _hookId = IntPtr.Zero;
    private NativeMethods.LowLevelKeyboardProc? _hookProc;
    private Thread? _hookThread;
    private uint _hookThreadId;
    private System.Threading.Timer? _rearmTimer;
    private volatile bool _disposed;

    private bool _capsDown;
    private string _buffer = string.Empty;
    private long _capsDownTimestamp;
    private bool _typedWhileHeld;
    /// <summary>Set when Esc cancels a CapsLock hold, to suppress the subsequent KeyUp event.</summary>
    private bool _escapeCancelledHold;

    /// <summary>
    /// Foreground window captured the instant CapsLock went down, before the overlay steals
    /// focus. Scopes the selected-text lookup to the app the user was actually working in.
    /// </summary>
    public IntPtr ForegroundWindowAtPress { get; private set; }

    /// <summary>Fired when CapsLock is pressed down. Handlers must not block.</summary>
    public event EventHandler? CapsLockPressed;

    /// <summary>
    /// Fired on quick tap release (held &lt; 0.5s with no typing).
    /// The overlay should toggle open/close in persistent mode.
    /// </summary>
    public event EventHandler? QuickTapReleased;

    /// <summary>
    /// Fired on long-press release (held ≥ 0.5s or typed while held).
    /// The overlay should perform lookup and then auto-hide.
    /// </summary>
    public event EventHandler? LongPressReleased;

    public event EventHandler<string>? BufferChanged;
    public event EventHandler<char>? CharTyped;

    /// <summary>Fired when Tab is pressed, requesting a lookup-mode switch.</summary>
    public event EventHandler? ModeSwitchRequested;

    public bool IsCapsDown => Volatile.Read(ref _capsDown);
    public string Buffer => Volatile.Read(ref _buffer);
    public bool TypedWhileHeld => Volatile.Read(ref _typedWhileHeld);

    /// <summary>
    /// Set by OverlayWindow when persistent (quick-tap) mode is active.
    /// When true, keys are captured even without CapsLock held.
    /// </summary>
    public bool PersistentModeActive { get; set; }

    /// <summary>Fired when Enter is pressed in persistent mode.</summary>
    public event EventHandler? EnterPressed;

    public void Install()
    {
        if (_hookThread is not null) return;

        // Not disposed: the hook thread signals it, and disposing after a Wait timeout
        // would make that Set() throw and kill the thread.
        var ready = new ManualResetEventSlim(false);
        _hookThread = new Thread(() => HookThreadMain(ready))
        {
            IsBackground = true,
            Name = "VerbaCore.KeyboardHook",
            // Overrunning the 300 ms hook timeout is what makes CapsLock fall through to
            // the OS, so this thread must not be starved by UI or background work.
            Priority = ThreadPriority.AboveNormal
        };
        _hookThread.SetApartmentState(ApartmentState.STA);
        _hookThread.Start();
        ready.Wait(TimeSpan.FromSeconds(5));

        _rearmTimer = new System.Threading.Timer(_ => RequestRearm(), null, RearmInterval, RearmInterval);
    }

    private void HookThreadMain(ManualResetEventSlim ready)
    {
        _hookThreadId = NativeMethods.GetCurrentThreadId();
        _hookProc = HookCallback;
        InstallHookCore();

        // Force CapsLock OFF after hook is installed
        // The hook filters injected keys, so this simulated press will pass through
        NativeMethods.ToggleCapsLockOff();

        ready.Set();

        // Private message pump — WH_KEYBOARD_LL callbacks are delivered through it.
        while (NativeMethods.GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            if (msg.hwnd == IntPtr.Zero && msg.message == WM_APP_REARM)
            {
                RearmHookCore();
                continue;
            }

            NativeMethods.TranslateMessage(ref msg);
            NativeMethods.DispatchMessage(ref msg);
        }

        UninstallHookCore();
    }

    private void InstallHookCore()
    {
        _hookId = NativeMethods.SetWindowsHookEx(
            NativeMethods.WH_KEYBOARD_LL,
            _hookProc!,
            NativeMethods.CachedModuleHandle,
            0);
    }

    private void UninstallHookCore()
    {
        if (_hookId == IntPtr.Zero) return;
        NativeMethods.UnhookWindowsHookEx(_hookId);
        _hookId = IntPtr.Zero;
    }

    /// <summary>Hook-thread only. Skipped mid-keystroke so no event is lost.</summary>
    private void RearmHookCore()
    {
        if (_disposed || _capsDown) return;
        UninstallHookCore();
        InstallHookCore();
    }

    private void RequestRearm()
    {
        if (_disposed || _hookThreadId == 0) return;
        NativeMethods.PostThreadMessage(_hookThreadId, WM_APP_REARM, IntPtr.Zero, IntPtr.Zero);
    }

    public void ClearBuffer()
    {
        _buffer = string.Empty;
        BufferChanged?.Invoke(this, _buffer);
    }

    public void SetBuffer(string value)
    {
        _buffer = value;
        BufferChanged?.Invoke(this, _buffer);
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            return HookCallbackCore(nCode, wParam, lParam);
        }
        catch (Exception ex)
        {
            // Never let exceptions propagate out of the hook callback —
            // doing so would cause Windows to silently remove the hook,
            // breaking CapsLock interception and potentially crashing the app.
            System.Diagnostics.Debug.WriteLine($"[CapsLockService] Hook exception: {ex}");
            return NativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);
        }
    }

    private IntPtr HookCallbackCore(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var hookStruct = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);
            var vkCode = (int)hookStruct.vkCode;
            var msg = wParam.ToInt32();
            var isInjected = (hookStruct.flags & NativeMethods.LLKHF_INJECTED) != 0;

            // CapsLock key down
            if (vkCode == NativeMethods.VK_CAPITAL)
            {
                // If this is a simulated keypress (from our ToggleCapsLockOff),
                // let it pass through to the OS so it actually toggles CapsLock off.
                if (isInjected)
                    return NativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);

                if (msg is NativeMethods.WM_KEYDOWN or NativeMethods.WM_SYSKEYDOWN)
                {
                    if (!_capsDown)
                    {
                        _capsDown = true;
                        _buffer = string.Empty;
                        _typedWhileHeld = false;
                        _capsDownTimestamp = Stopwatch.GetTimestamp();
                        ForegroundWindowAtPress = NativeMethods.GetForegroundWindow();
                        CapsLockPressed?.Invoke(this, EventArgs.Empty);
                    }
                    return (IntPtr)1;
                }

                if (msg is NativeMethods.WM_KEYUP or NativeMethods.WM_SYSKEYUP)
                {
                    // If Esc already cancelled the hold, just consume the stale KeyUp
                    if (_escapeCancelledHold)
                    {
                        _escapeCancelledHold = false;
                        NativeMethods.ToggleCapsLockOff();
                        return (IntPtr)1;
                    }

                    if (_capsDown)
                    {
                        _capsDown = false;
                        var elapsedMs = Stopwatch.GetElapsedTime(_capsDownTimestamp).TotalMilliseconds;

                        if (!_typedWhileHeld && elapsedMs < LongPressThresholdMs)
                        {
                            // Quick tap — toggle persistent overlay
                            QuickTapReleased?.Invoke(this, EventArgs.Empty);
                        }
                        else
                        {
                            // Long press or typed — Enso-style release
                            LongPressReleased?.Invoke(this, EventArgs.Empty);
                        }
                    }
                    // Always ensure CapsLock stays OFF
                    NativeMethods.ToggleCapsLockOff();
                    return (IntPtr)1;
                }
            }

            // While CapsLock is held, capture typed keys
            if (_capsDown && msg is NativeMethods.WM_KEYDOWN or NativeMethods.WM_SYSKEYDOWN)
            {
                var ch = VkCodeToChar(vkCode);
                if (ch.HasValue)
                {
                    _typedWhileHeld = true;
                    _buffer += ch.Value;
                    CharTyped?.Invoke(this, ch.Value);
                    BufferChanged?.Invoke(this, _buffer);
                    return (IntPtr)1; // Suppress the key
                }

                // Handle Backspace
                if (vkCode == 0x08) // VK_BACK
                {
                    if (_buffer.Length > 0)
                    {
                        _buffer = _buffer[..^1];
                        BufferChanged?.Invoke(this, _buffer);
                    }
                    // Only mark typed if there was actual content to delete
                    if (_buffer.Length > 0 || _typedWhileHeld)
                        _typedWhileHeld = true;
                    return (IntPtr)1;
                }

                // Handle Tab — mode switch signal
                if (vkCode == 0x09) // VK_TAB
                {
                    ModeSwitchRequested?.Invoke(this, EventArgs.Empty);
                    return (IntPtr)1;
                }

                // Handle Escape — cancel
                if (vkCode == 0x1B) // VK_ESCAPE
                {
                    _buffer = string.Empty;
                    _capsDown = false;
                    _escapeCancelledHold = true;
                    BufferChanged?.Invoke(this, _buffer);
                    LongPressReleased?.Invoke(this, EventArgs.Empty);
                    return (IntPtr)1;
                }

                // Handle Space
                if (vkCode == 0x20) // VK_SPACE
                {
                    _typedWhileHeld = true;
                    _buffer += ' ';
                    BufferChanged?.Invoke(this, _buffer);
                    return (IntPtr)1;
                }

                // Suppress all other keys while CapsLock is held
                return (IntPtr)1;
            }

            // Persistent mode: capture keys even without CapsLock held
            if (PersistentModeActive && !_capsDown && msg is NativeMethods.WM_KEYDOWN or NativeMethods.WM_SYSKEYDOWN)
            {
                // Enter — trigger lookup
                if (vkCode == 0x0D) // VK_RETURN
                {
                    EnterPressed?.Invoke(this, EventArgs.Empty);
                    return (IntPtr)1;
                }

                // Escape — close overlay
                if (vkCode == 0x1B) // VK_ESCAPE
                {
                    _buffer = string.Empty;
                    BufferChanged?.Invoke(this, _buffer);
                    QuickTapReleased?.Invoke(this, EventArgs.Empty); // toggle off
                    return (IntPtr)1;
                }

                // Tab — mode switch
                if (vkCode == 0x09)
                {
                    ModeSwitchRequested?.Invoke(this, EventArgs.Empty);
                    return (IntPtr)1;
                }

                // Backspace
                if (vkCode == 0x08)
                {
                    if (_buffer.Length > 0)
                    {
                        _buffer = _buffer[..^1];
                        BufferChanged?.Invoke(this, _buffer);
                    }
                    return (IntPtr)1;
                }

                // Space
                if (vkCode == 0x20)
                {
                    _buffer += ' ';
                    BufferChanged?.Invoke(this, _buffer);
                    return (IntPtr)1;
                }

                // Regular character
                var pch = VkCodeToChar(vkCode);
                if (pch.HasValue)
                {
                    _buffer += pch.Value;
                    CharTyped?.Invoke(this, pch.Value);
                    BufferChanged?.Invoke(this, _buffer);
                    return (IntPtr)1;
                }
            }
        }

        return NativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    private static char? VkCodeToChar(int vkCode)
    {
        // A-Z
        if (vkCode is >= 0x41 and <= 0x5A)
            return (char)('a' + (vkCode - 0x41));

        // 0-9
        if (vkCode is >= 0x30 and <= 0x39)
            return (char)('0' + (vkCode - 0x30));

        // Numpad 0-9
        if (vkCode is >= 0x60 and <= 0x69)
            return (char)('0' + (vkCode - 0x60));

        // Common punctuation
        return vkCode switch
        {
            0xBD => '-',  // OEM_MINUS
            0xBB => '=',  // OEM_PLUS (unshifted)
            0xDB => '[',
            0xDD => ']',
            0xDC => '\\',
            0xBA => ';',
            0xDE => '\'',
            0xBC => ',',
            0xBE => '.',
            0xBF => '/',
            _ => null
        };
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _rearmTimer?.Dispose();
        _rearmTimer = null;

        if (_hookThreadId != 0)
        {
            NativeMethods.PostThreadMessage(_hookThreadId, NativeMethods.WM_QUIT, IntPtr.Zero, IntPtr.Zero);
            _hookThread?.Join(TimeSpan.FromSeconds(2));
            _hookThreadId = 0;
        }

        _hookThread = null;
        _hookProc = null;
    }
}
