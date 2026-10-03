using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Xml.Linq;
using VerbaCore.Models;
using VerbaCore.Services;

namespace VerbaCore.PopupTests;

internal static class Program
{
    internal const uint BlockMessage = 0x8007;

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Contains("--input-hooks")) return RunInputHookTests();

        if (args.Length == 2 && args[0] == "--host")
        {
            using var blocked = EventWaitHandle.OpenExisting(args[1] + ".blocked");
            using var release = EventWaitHandle.OpenExisting(args[1] + ".release");
            var host = new Window
            {
                Title = "VerbaCore popup test foreground",
                Width = 360,
                Height = 180,
                Content = new TextBox()
            };
            host.SourceInitialized += (_, _) =>
            {
                var source = (HwndSource)PresentationSource.FromVisual(host);
                source.AddHook((IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled) =>
                {
                    if (message == BlockMessage)
                    {
                        blocked.Set();
                        release.WaitOne(TimeSpan.FromSeconds(3));
                        handled = true;
                    }
                    return IntPtr.Zero;
                });
            };
            host.Loaded += (_, _) => Console.WriteLine(new WindowInteropHelper(host).Handle.ToInt64());
            return new Application().Run(host);
        }

        var app = new PopupTestApplication(args.Contains("--offscreen"));
        app.LoadResources();
        return app.Run();
    }

    private static int RunInputHookTests()
    {
        const BindingFlags privateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        using var capsLock = new CapsLockService();
        var hook = typeof(CapsLockService).GetMethod("HookCallbackCore", privateInstance)!;
        var data = Marshal.AllocHGlobal(32);
        try
        {
            Marshal.Copy(new byte[32], 0, data, 32);
            Marshal.WriteInt32(data, 0x14);
            var pressed = 0;
            var released = 0;
            var cancelled = 0;
            capsLock.CapsLockPressed += (_, _) => pressed++;
            capsLock.QuickTapReleased += (_, _) => released++;
            capsLock.LongPressReleased += (_, _) => released++;
            capsLock.HoldCancelled += (_, _) => cancelled++;
            for (var repeat = 0; repeat < 200; repeat++)
                hook.Invoke(capsLock, [0, new IntPtr(0x0100), data]);
            if (pressed != 1 || !capsLock.IsCapsDown)
                throw new InvalidOperationException("CapsLock auto-repeat reopened the popup.");
            hook.Invoke(capsLock, [0, new IntPtr(0x0101), data]);
            if (released != 1 || capsLock.IsCapsDown)
                throw new InvalidOperationException("CapsLock release was not handled exactly once.");

            hook.Invoke(capsLock, [0, new IntPtr(0x0100), data]);
            Marshal.WriteInt32(data, 0x1B);
            hook.Invoke(capsLock, [0, new IntPtr(0x0100), data]);
            Marshal.WriteInt32(data, 0x14);
            for (var repeat = 0; repeat < 200; repeat++)
                hook.Invoke(capsLock, [0, new IntPtr(0x0100), data]);
            hook.Invoke(capsLock, [0, new IntPtr(0x0101), data]);
            if (pressed != 2 || released != 1 || cancelled != 1 || capsLock.IsCapsDown)
                throw new InvalidOperationException("A cancelled hold reopened or released twice during key repeat.");

            hook.Invoke(capsLock, [0, new IntPtr(0x0100), data]);
            capsLock.TextInputWindow = Native.GetForegroundWindow();
            if (capsLock.TextInputWindow == IntPtr.Zero)
                throw new InvalidOperationException("Foreground window is unavailable for the text input routing check.");
            foreach (var key in new[] { 0x12, 0xA4, 0xA5, 0x15, 0x41, 0xE5 })
            {
                Marshal.WriteInt32(data, key);
                var handled = (IntPtr)hook.Invoke(capsLock, [0, new IntPtr(0x0100), data])!;
                if (handled != IntPtr.Zero || capsLock.Buffer.Length != 0)
                    throw new InvalidOperationException($"IME key {key:X2} was intercepted instead of reaching the focused TextBox.");
            }
            capsLock.UpdateTextInput("\uD55C\uAE00");
            if (capsLock.Buffer != "\uD55C\uAE00" || !capsLock.TypedWhileHeld)
                throw new InvalidOperationException("Composed text was not retained for hold release.");
            Marshal.WriteInt32(data, 0x14);
            hook.Invoke(capsLock, [0, new IntPtr(0x0101), data]);
            capsLock.TextInputWindow = IntPtr.Zero;

            capsLock.Install();
            var hookThread = (Thread)typeof(CapsLockService).GetField("_hookThread", privateInstance)!.GetValue(capsLock)!;
            if (hookThread.ManagedThreadId == Environment.CurrentManagedThreadId)
                throw new InvalidOperationException("Hooks run on the caller's UI thread.");
            var mouseHandle = typeof(CapsLockService).GetField("_mouseHookId", privateInstance)!;
            using var mouseProbe = new MouseInputProbe();
            mouseProbe.CheckMovement();
            var started = Stopwatch.GetTimestamp();
            capsLock.SetMouseMonitoring(true);
            if (!SpinWait.SpinUntil(() => (IntPtr)mouseHandle.GetValue(capsLock)! != IntPtr.Zero, 2000))
                throw new InvalidOperationException("Mouse hook installation needed the blocked UI thread.");
            mouseProbe.CheckMovement();
            capsLock.SetMouseMonitoring(false);
            if (!SpinWait.SpinUntil(() => (IntPtr)mouseHandle.GetValue(capsLock)! == IntPtr.Zero, 2000))
                throw new InvalidOperationException("Mouse hook removal needed the blocked UI thread.");
            Console.WriteLine($"PASS: 400 CapsLock repeats, cancellation latch, and mouse hook lifecycle without a UI message pump: {Stopwatch.GetElapsedTime(started).TotalMilliseconds:F1} ms");
            Console.WriteLine("PASS: Alt, Hangul, and IME keys use the native text input path while held");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
        finally
        {
            Marshal.FreeHGlobal(data);
        }
    }
}

internal sealed class PopupTestApplication(bool offscreen) : Application
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private readonly bool _offscreen = offscreen;
    private readonly List<double> _latencies = [];

    internal void LoadResources()
    {
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        Assembly.Load("Wpf.Ui");
        Assembly.Load("Markdig.Wpf");
        var definition = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "AppResources.xaml")).Root!;
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var dictionary = definition.Element(presentation + "Application.Resources")!.Elements().Single();
        foreach (var attribute in definition.Attributes().Where(attribute => attribute.IsNamespaceDeclaration))
        {
            var value = attribute.Value.StartsWith("clr-namespace:VerbaCore.", StringComparison.Ordinal)
                ? attribute.Value + ";assembly=VerbaCore"
                : attribute.Value;
            dictionary.SetAttributeValue(attribute.Name, value);
        }
        foreach (var element in dictionary.DescendantsAndSelf()
                     .Where(element => element.Name.NamespaceName.StartsWith("clr-namespace:VerbaCore.", StringComparison.Ordinal)))
        {
            element.Name = XName.Get(element.Name.LocalName, element.Name.NamespaceName + ";assembly=VerbaCore");
        }
        using var reader = dictionary.CreateReader();
        Resources = (ResourceDictionary)XamlReader.Load(reader);
    }

    protected override async void OnStartup(StartupEventArgs args)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        try
        {
            await RunTestsAsync(timeout.Token);
            Console.WriteLine(_offscreen
                ? "PASS: offscreen rendering and working-set eviction; desktop focus, input, and presentation NOT tested"
                : "PASS: popup rendering, foreground focus, input, and reopen checks");
            Shutdown(0);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            Shutdown(1);
        }
    }

    private async Task RunTestsAsync(CancellationToken ct)
    {
        var eventName = "Local\\VerbaCore.PopupTests." + Guid.NewGuid().ToString("N");
        using var blocked = new EventWaitHandle(false, EventResetMode.ManualReset, eventName + ".blocked");
        using var release = new EventWaitHandle(false, EventResetMode.ManualReset, eventName + ".release");
        using var host = Process.Start(new ProcessStartInfo(Environment.ProcessPath!)
        {
            Arguments = "--host " + eventName,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            CreateNoWindow = true
        })!;
        OverlayWindow? overlay = null;
        try
        {
            var handleText = await host.StandardOutput.ReadLineAsync(ct);
            var hostHandle = new IntPtr(long.Parse(handleText!, CultureInfo.InvariantCulture));
            Native.SetForegroundWindow(hostHandle);
            if (!_offscreen)
                await WaitUntilAsync(() => Native.GetForegroundWindow() == hostHandle, ct);

            Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("/VerbaCore;component/Resources/Strings.ko.xaml", UriKind.Relative)
            });
            var settings = new SettingsService();
            var history = new HistoryService();
            using var capsLock = new CapsLockService();
            var cursorText = new CursorTextService();
            var ai = new NoNetworkService();
            overlay = new OverlayWindow(ai, settings, history, capsLock,
                cursorText, new LookupCacheService(settings));
            typeof(CapsLockService).GetProperty(nameof(CapsLockService.ForegroundWindowAtPress))!
                .SetValue(capsLock, hostHandle);

            overlay.PreWarm();
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle, ct);
            if (!_offscreen)
                Require(Native.GetForegroundWindow() == hostHandle, "Pre-warm stole foreground focus.");

            await MeasurePopupAsync(overlay, capsLock, "first-after-prewarm", ct);
            var holdInput = (TextBox)overlay.FindName("InputTextBox");
            Require(holdInput.Visibility == Visibility.Visible && InputMethod.GetIsInputMethodEnabled(holdInput),
                "Hold mode did not expose the IME-enabled TextBox.");
            holdInput.Text = "\uD55C\uAE00";
            Require(capsLock.Buffer == holdInput.Text, "Hold-mode composed text was not synchronized.");
            Raise(capsLock, nameof(CapsLockService.QuickTapReleased));
            var input = (TextBox)overlay.FindName("InputTextBox");
            if (!_offscreen)
            {
                await WaitUntilAsync(() => input.IsKeyboardFocused, ct);
                Require(Native.GetForegroundWindow() == new WindowInteropHelper(overlay).Handle,
                    "Popup did not become the foreground window.");
                Require(InputMethod.GetIsInputMethodEnabled(input), "IME support was disabled.");
                TextCompositionManager.StartComposition(new TextComposition(InputManager.Current, input, "\uD55C\uAE00"));
                Require(input.Text == "\uD55C\uAE00", "Korean text input was lost.");
                await WaitUntilAsync(() => overlay.Opacity >= 1, ct);
            }
            else
            {
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle, ct);
                input.Text = "\uD55C\uAE00";
                overlay.UpdateLayout();
            }
            SaveSnapshot(overlay);

            var warmup = typeof(OverlayWindow).GetMethod("WarmUpInputSurface", PrivateInstance)!;
            var visibleText = input.Text;
            warmup.Invoke(overlay, null);
            Require(input.Text == visibleText && input.Visibility == Visibility.Visible,
                "Idle preparation changed an active input session.");

            await HideAsync(overlay, ct);
            if (_offscreen)
            {
                overlay.BeginAnimation(UIElement.OpacityProperty, null);
                overlay.Opacity = 0;
                overlay.Left = -9999;
                overlay.Top = -9999;
            }
            var foregroundBeforeWarmup = Native.GetForegroundWindow();
            var positionBeforeWarmup = new Point(overlay.Left, overlay.Top);
            var warmupStarted = Stopwatch.GetTimestamp();
            warmup.Invoke(overlay, null);
            Console.WriteLine($"idle-layout: {Stopwatch.GetElapsedTime(warmupStarted).TotalMilliseconds:F1} ms");
            Require(Native.GetForegroundWindow() == foregroundBeforeWarmup
                && new Point(overlay.Left, overlay.Top) == positionBeforeWarmup,
                "Idle preparation moved or activated the popup.");

            var hiddenCount = 0;
            overlay.IsVisibleChanged += (_, change) =>
            {
                if (!(bool)change.NewValue) hiddenCount++;
            };
            for (var iteration = 0; iteration < 5; iteration++)
            {
                await HideAsync(overlay, ct);
                Native.SetForegroundWindow(hostHandle);
                if (!_offscreen)
                    await WaitUntilAsync(() => Native.GetForegroundWindow() == hostHandle, ct);
                Require(Native.EmptyWorkingSet(Process.GetCurrentProcess().Handle),
                    "Could not simulate working-set eviction.");
                await MeasurePopupAsync(overlay, capsLock, $"trimmed-{iteration + 1}", ct);
            }
            Require(hiddenCount == 0, "Ordinary popup reuse hid the native window.");

            if (!_offscreen)
            {
                await HideAsync(overlay, ct);
                Native.SetForegroundWindow(hostHandle);
                await WaitUntilAsync(() => Native.GetForegroundWindow() == hostHandle, ct);
                Require(Native.PostMessage(hostHandle, Program.BlockMessage, IntPtr.Zero, IntPtr.Zero),
                    "Could not block the foreground fixture.");
                Require(blocked.WaitOne(TimeSpan.FromSeconds(2)), "Foreground fixture did not block.");
                try
                {
                    await MeasurePopupAsync(overlay, capsLock, "unresponsive-foreground", ct);
                }
                finally
                {
                    release.Set();
                }
            }

            overlay.HideOverlay();
            await MeasurePopupAsync(overlay, capsLock, "reopen-during-fade", ct);
            if (!_offscreen)
                await WaitUntilAsync(() => overlay.Opacity >= 1, ct);
            Require(overlay.IsVisible && overlay.Left > -1000, "An old hide completion hid the reopened popup.");
            settings.Current.ApiKey = "popup-test-placeholder";
            input.Text = "previous hold";
            Raise(capsLock, nameof(CapsLockService.LongPressReleased));
            Raise(capsLock, nameof(CapsLockService.CapsLockPressed));
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle, ct);
            Require(ai.LastInput is null && input.Visibility == Visibility.Visible,
                "A queued release consumed a newer CapsLock gesture.");

            input.Text = "\uD55C\uAE00";
            Raise(capsLock, nameof(CapsLockService.LongPressReleased));
            await WaitUntilAsync(() => ai.LastInput == "\uD55C\uAE00", ct);
            Require(capsLock.TextInputWindow == IntPtr.Zero, "Hold release retained native input routing.");

            await HideAsync(overlay, ct);
            await MeasurePopupAsync(overlay, capsLock, "cancel-hold", ct);
            var previousCallCount = ai.CallCount;
            typeof(OverlayWindow).GetField("_grabbedSelectedText", PrivateInstance)!.SetValue(overlay, "selected text");
            Raise(capsLock, nameof(CapsLockService.HoldCancelled));
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle, ct);
            Require(!(bool)typeof(OverlayWindow).GetField("_isShown", PrivateInstance)!.GetValue(overlay)!
                && ai.CallCount == previousCallCount, "Cancelling a hold triggered a lookup or left the popup open.");

            await MeasurePopupAsync(overlay, capsLock, "outside-click", ct);
            var mousePressed = (EventHandler<(int X, int Y)>)typeof(CapsLockService)
                .GetField(nameof(CapsLockService.MousePressed), PrivateInstance)!.GetValue(capsLock)!;
            var center = overlay.PointToScreen(new Point(overlay.Width / 2, overlay.Height / 2));
            mousePressed(capsLock, ((int)center.X, (int)center.Y));
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle, ct);
            Require((bool)typeof(OverlayWindow).GetField("_isShown", PrivateInstance)!.GetValue(overlay)!,
                "A click inside the popup dismissed it.");
            mousePressed(capsLock, (-32000, -32000));
            Raise(capsLock, nameof(CapsLockService.CapsLockPressed));
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle, ct);
            Require((bool)typeof(OverlayWindow).GetField("_isShown", PrivateInstance)!.GetValue(overlay)!,
                "A queued outside click dismissed a newer gesture.");
            mousePressed(capsLock, (-32000, -32000));
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle, ct);
            Require(!(bool)typeof(OverlayWindow).GetField("_isShown", PrivateInstance)!.GetValue(overlay)!,
                "An outside click did not dismiss the popup.");
            Console.WriteLine("PASS: hold text submission, cancellation, stale gesture isolation, and outside-click dismissal");
            Require(history.Items.Count == 0, "Popup tests unexpectedly wrote history.");
            if (!_offscreen)
                Require(_latencies.All(latency => latency < 250), "A first-render latency exceeded 250 ms.");
            overlay.CloseForShutdown();
            var idleTimer = (DispatcherTimer)typeof(OverlayWindow).GetField("_idleWarmupTimer", PrivateInstance)!.GetValue(overlay)!;
            Require(!idleTimer.IsEnabled, "Idle preparation timer was not stopped at shutdown.");
            overlay = null;
        }
        finally
        {
            release.Set();
            overlay?.CloseForShutdown();
            if (!host.HasExited)
            {
                host.Kill();
                await host.WaitForExitAsync(CancellationToken.None);
            }
        }
    }

    private async Task MeasurePopupAsync(OverlayWindow overlay, CapsLockService capsLock,
        string scenario, CancellationToken ct)
    {
        var rendered = new TaskCompletionSource<double>(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = Stopwatch.GetTimestamp();
        void OnRendering(object? sender, EventArgs args)
        {
            if (overlay.IsVisible && overlay.Left > -1000 && overlay.Opacity > 0)
                rendered.TrySetResult(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }
        CompositionTarget.Rendering += OnRendering;
        try
        {
            await Task.Run(() => Raise(capsLock, nameof(CapsLockService.CapsLockPressed)), ct);
            double elapsed;
            if (_offscreen)
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    Console.WriteLine($"{scenario}: UI-ready={Stopwatch.GetElapsedTime(started).TotalMilliseconds:F1} ms");
                    overlay.UpdateLayout();
                    var target = new RenderTargetBitmap((int)overlay.ActualWidth, (int)overlay.ActualHeight,
                        96, 96, PixelFormats.Pbgra32);
                    target.Render((Visual)overlay.Content);
                }, DispatcherPriority.ApplicationIdle, ct);
                elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            }
            else
            {
                elapsed = await rendered.Task.WaitAsync(TimeSpan.FromSeconds(5), ct);
            }
            _latencies.Add(elapsed);
            Console.WriteLine($"{scenario}: {(_offscreen ? "offscreen-render" : "first-render")}={elapsed:F1} ms");
        }
        finally
        {
            CompositionTarget.Rendering -= OnRendering;
        }
    }

    private async Task HideAsync(OverlayWindow overlay, CancellationToken ct)
    {
        overlay.HideOverlay();
        if (!_offscreen)
            await WaitUntilAsync(() => overlay.Opacity == 0 && overlay.Left < -1000, ct);
        else
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle, ct);
    }

    private static async Task WaitUntilAsync(Func<bool> condition, CancellationToken ct)
    {
        var complete = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(16) };
        timer.Tick += (_, _) =>
        {
            if (condition()) complete.TrySetResult();
        };
        timer.Start();
        try
        {
            await complete.Task.WaitAsync(TimeSpan.FromSeconds(5), ct);
        }
        finally
        {
            timer.Stop();
        }
    }

    private static void Raise(CapsLockService capsLock, string eventName)
    {
        var handler = (EventHandler?)typeof(CapsLockService).GetField(eventName, PrivateInstance)!.GetValue(capsLock);
        handler?.Invoke(capsLock, EventArgs.Empty);
    }

    private static void SaveSnapshot(OverlayWindow overlay)
    {
        var scale = (ScaleTransform)overlay.FindName("ContentScale");
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        scale.ScaleX = scale.ScaleY = 1;
        var translate = (TranslateTransform)overlay.FindName("ContentTranslate");
        translate.BeginAnimation(TranslateTransform.YProperty, null);
        translate.Y = 0;
        var bitmap = new RenderTargetBitmap((int)overlay.ActualWidth, (int)overlay.ActualHeight,
            96, 96, PixelFormats.Pbgra32);
        bitmap.Render((Visual)overlay.Content);
        var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
        bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
        Require(pixels.Count(channel => channel != 0) > 10000, "Popup rendered a blank surface.");
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        var path = Path.Combine(Path.GetTempPath(), "VerbaCore-popup-test.png");
        using var output = File.Create(path);
        encoder.Save(output);
        Console.WriteLine("Snapshot: " + path);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}

internal sealed class NoNetworkService : IOpenAiService
{
    public string? LastInput { get; private set; }
    public int CallCount { get; private set; }

    public Task<string> GetCompletionAsync(string input, LookupMode mode, string nativeLanguage,
        string foreignLanguage, CancellationToken ct = default)
    {
        LastInput = input;
        CallCount++;
        throw new InvalidOperationException("Network calls are forbidden in popup tests.");
    }

    public IAsyncEnumerable<string> StreamCompletionAsync(string input, LookupMode mode, string nativeLanguage,
        string foreignLanguage, CancellationToken ct = default)
    {
        LastInput = input;
        CallCount++;
        throw new InvalidOperationException("Network calls are forbidden in popup tests.");
    }
}

internal sealed class MouseInputProbe : IDisposable
{
    private const uint Marker = 0x56434254;
    private readonly Thread _thread;
    private readonly ManualResetEventSlim _ready = new();
    private readonly Native.HookProc _callback;
    private Dispatcher? _dispatcher;
    private IntPtr _hook;
    private int _received;

    internal MouseInputProbe()
    {
        _callback = ObserveMovement;
        _thread = new Thread(() =>
        {
            _dispatcher = Dispatcher.CurrentDispatcher;
            _hook = Native.SetWindowsHookEx(14, _callback, Native.GetModuleHandle(null), 0);
            _ready.Set();
            try
            {
                Dispatcher.Run();
            }
            finally
            {
                if (_hook != IntPtr.Zero) Native.UnhookWindowsHookEx(_hook);
            }
        }) { IsBackground = true, Name = "VerbaCore.TestMouseProbe" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        if (!_ready.Wait(TimeSpan.FromSeconds(2)) || _hook == IntPtr.Zero)
            throw new InvalidOperationException("Could not install the independent mouse probe.");
    }

    private IntPtr ObserveMovement(int code, IntPtr message, IntPtr data)
    {
        if (code >= 0 && (int)message == 0x0200
            && unchecked((uint)Marshal.ReadIntPtr(data, IntPtr.Size == 8 ? 24 : 20).ToInt64()) == Marker)
            Interlocked.Increment(ref _received);
        return Native.CallNextHookEx(_hook, code, message, data);
    }

    internal void CheckMovement()
    {
        var started = Stopwatch.GetTimestamp();
        var expected = Volatile.Read(ref _received) + 32;
        for (var index = 0; index < 32; index++)
            Native.mouse_event(0x2001, index % 2 == 0 ? 1u : unchecked((uint)-1), 0, 0, new UIntPtr(Marker));
        if (!SpinWait.SpinUntil(() => Volatile.Read(ref _received) == expected, 2000))
            throw new InvalidOperationException($"Mouse events did not reach the independent probe: {_received}/{expected}; foreground={Native.GetForegroundWindow()}.");
        var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        if (elapsed >= 500)
            throw new InvalidOperationException($"Mouse input stalled for {elapsed:F1} ms.");
        Console.WriteLine($"PASS: 32 paired mouse moves crossed the hook chain in {elapsed:F1} ms without a UI message pump");
    }

    public void Dispose()
    {
        _dispatcher?.BeginInvokeShutdown(DispatcherPriority.Send);
        _thread.Join(TimeSpan.FromSeconds(2));
        _ready.Dispose();
    }
}

internal static class Native
{
    internal delegate IntPtr HookProc(int code, IntPtr message, IntPtr data);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern IntPtr SetWindowsHookEx(int hook, HookProc callback, IntPtr module, uint thread);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UnhookWindowsHookEx(IntPtr hook);

    [DllImport("user32.dll")]
    internal static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    internal static extern IntPtr GetModuleHandle(string? module);

    [DllImport("user32.dll")]
    internal static extern void mouse_event(uint flags, uint deltaX, uint deltaY, uint data, UIntPtr extraInfo);

    [DllImport("user32.dll")]
    internal static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetForegroundWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool PostMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("psapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EmptyWorkingSet(IntPtr process);
}