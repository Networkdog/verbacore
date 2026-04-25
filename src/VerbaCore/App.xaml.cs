using System.Drawing;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using VerbaCore.Models;
using VerbaCore.Services;
using VerbaCore.ViewModels;
using Wpf.Ui.Appearance;

namespace VerbaCore;

public partial class App : Application
{
    private static readonly ServiceProvider Services = ConfigureServices();
    private static Mutex? _singleInstanceMutex;
    private System.Windows.Forms.NotifyIcon? _trayIcon;
    private SettingsWindow? _settingsWindow;
    private OverlayWindow? _overlayWindow;
    private CapsLockService? _capsLockService;

    public static T GetService<T>() where T : notnull => Services.GetRequiredService<T>();

    private static ServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();

        // Services
        services.AddSingleton<SettingsService>();
        services.AddSingleton<HistoryService>();
        services.AddSingleton<PromptBuilder>();
        services.AddSingleton<CapsLockService>();
        services.AddSingleton<CursorTextService>();
        services.AddSingleton<HotkeyService>();
        services.AddHttpClient<IOpenAiService, OpenAiService>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(60);
            client.DefaultRequestVersion = new Version(2, 0);
            client.DefaultVersionPolicy = System.Net.Http.HttpVersionPolicy.RequestVersionOrLower;
        });

        // ViewModels
        services.AddSingleton<SettingsViewModel>();
        services.AddTransient<HistoryViewModel>(sp =>
        {
            var historyService = sp.GetRequiredService<HistoryService>();
            return new HistoryViewModel(historyService, (_, _) => { });
        });

        return services.BuildServiceProvider();
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        // Global exception handlers to prevent silent crashes
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;

        // Single-instance enforcement
        _singleInstanceMutex = new Mutex(true, "VerbaCore_B3F8A2E1-7C4D-4E5A-9B2F-1A3C5D7E9F0B", out var isNew);
        if (!isNew)
        {
            MessageBox.Show("VerbaCore가 이미 실행 중입니다.", "VerbaCore",
                MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        base.OnStartup(e);

        // Load settings & history
        var settings = GetService<SettingsService>();
        await settings.LoadAsync();

        var history = GetService<HistoryService>();
        await history.LoadAsync();

        // Apply saved settings
        ApplyTheme(settings.Current.Theme);

        if (settings.IsFirstRun)
        {
            // On first run, sync from registry so installer's choice is preserved
            settings.Current.StartWithWindows = IsStartWithWindowsInRegistry();
        }
        else
        {
            ApplyStartWithWindows(settings.Current.StartWithWindows);
        }

        // Create overlay window (hidden by default)
        _capsLockService = GetService<CapsLockService>();
        _overlayWindow = new OverlayWindow(
            GetService<IOpenAiService>(),
            settings,
            history,
            _capsLockService,
            GetService<CursorTextService>());

        // Install CapsLock hook
        _capsLockService.Install();

        // Set up system tray icon
        SetupTrayIcon();
    }

    private void SetupTrayIcon()
    {
        var contextMenu = new System.Windows.Forms.ContextMenuStrip();

        contextMenu.Items.Add("⚙ 설정", null, (_, _) => ShowSettingsWindow());
        contextMenu.Items.Add("-");
        contextMenu.Items.Add("VerbaCore 정보", null, (_, _) =>
            MessageBox.Show(
                "VerbaCore — 경량 AI 사전 & 번역\n\n" +
                "CapsLock을 누른 채로 단어를 입력하세요.\n" +
                "CapsLock을 떼면 AI가 결과를 표시합니다.\n\n" +
                "Tab: 모드 전환 (사전/번역)\n" +
                "Esc: 취소\n" +
                "Backspace: 글자 삭제",
                "VerbaCore", MessageBoxButton.OK, MessageBoxImage.Information));
        contextMenu.Items.Add("-");
        contextMenu.Items.Add("종료", null, (_, _) => ExitApp());

        _trayIcon = new System.Windows.Forms.NotifyIcon
        {
            Icon = LoadTrayIcon(),
            Text = "VerbaCore — CapsLock으로 AI 사전/번역",
            Visible = true,
            ContextMenuStrip = contextMenu
        };

        _trayIcon.DoubleClick += (_, _) => ShowSettingsWindow();
    }

    private static Icon LoadTrayIcon()
    {
        // For single-file publish, AppContext.BaseDirectory points to the temp extraction dir,
        // not the actual install directory. Use the exe's real location instead.
        var exePath = Environment.ProcessPath;
        var exeDir = !string.IsNullOrEmpty(exePath)
            ? System.IO.Path.GetDirectoryName(exePath)!
            : AppContext.BaseDirectory;

        // 1. Try .ico files first (native icon format, best quality)
        var icoCandidates = new[]
        {
            System.IO.Path.Combine(exeDir, "res", "icons", "verbacore.ico"),
            System.IO.Path.Combine(AppContext.BaseDirectory, "res", "icons", "verbacore.ico"),
            System.IO.Path.Combine(exeDir, "..", "..", "..", "..", "..", "res", "icons", "verbacore.ico"),
        };

        foreach (var path in icoCandidates)
        {
            if (!System.IO.File.Exists(path)) continue;
            try { return new Icon(path, 32, 32); }
            catch { /* fall through */ }
        }

        // 2. Fall back to .png conversion
        var pngCandidates = new[]
        {
            System.IO.Path.Combine(exeDir, "res", "icons", "verbacore.png"),
            System.IO.Path.Combine(AppContext.BaseDirectory, "res", "icons", "verbacore.png"),
            System.IO.Path.Combine(exeDir, "..", "..", "..", "..", "..", "res", "icons", "verbacore.png"),
        };

        foreach (var path in pngCandidates)
        {
            if (!System.IO.File.Exists(path)) continue;
            using var bmp = new Bitmap(path);
            var hIcon = bmp.GetHicon();
            var icon = Icon.FromHandle(hIcon);
            var cloned = (Icon)icon.Clone();
            icon.Dispose();
            Helpers.NativeMethods.DestroyIcon(hIcon);
            return cloned;
        }

        return SystemIcons.Application;
    }

    private void ShowSettingsWindow()
    {
        if (_settingsWindow == null || !_settingsWindow.IsLoaded)
        {
            _settingsWindow = new SettingsWindow();
        }

        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    private void ExitApp()
    {
        _capsLockService?.Dispose();
        _capsLockService = null;
        _trayIcon?.Dispose();
        _trayIcon = null;
        _overlayWindow?.CloseForShutdown();
        _settingsWindow?.Close();
        _singleInstanceMutex?.ReleaseMutex();
        _singleInstanceMutex?.Dispose();
        _singleInstanceMutex = null;
        Shutdown();
    }

    public static void ApplyTheme(ThemeMode mode)
    {
        var theme = mode switch
        {
            ThemeMode.Light => ApplicationTheme.Light,
            ThemeMode.Dark => ApplicationTheme.Dark,
            _ => DetectSystemTheme()
        };

        // Skip if theme hasn't changed — avoids unnecessary resource dictionary swap
        // that resets overlay opacity and causes visual flicker
        if (ApplicationThemeManager.GetAppTheme() == theme)
            return;

        ApplicationThemeManager.Apply(theme);
    }

    private static ApplicationTheme DetectSystemTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            var value = key?.GetValue("AppsUseLightTheme");
            return value is int i && i == 1 ? ApplicationTheme.Light : ApplicationTheme.Dark;
        }
        catch
        {
            return ApplicationTheme.Dark;
        }
    }

    public static void ApplyStartWithWindows(bool enable)
    {
        const string appName = "VerbaCore";
        using var key = Registry.CurrentUser.OpenSubKey(
            @"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
        if (key == null) return;

        if (enable)
        {
            var exePath = GetAppExePath();
            if (string.IsNullOrEmpty(exePath) || !System.IO.File.Exists(exePath))
                return;
            key.SetValue(appName, $"\"{exePath}\"");
        }
        else
        {
            key.DeleteValue(appName, throwOnMissingValue: false);
        }
    }

    private static bool IsStartWithWindowsInRegistry()
    {
        const string appName = "VerbaCore";
        using var key = Registry.CurrentUser.OpenSubKey(
            @"Software\Microsoft\Windows\CurrentVersion\Run");
        return key?.GetValue(appName) is not null;
    }

    private static string? GetAppExePath()
    {
        var processPath = Environment.ProcessPath;

        // When running via 'dotnet run', ProcessPath points to dotnet.exe — resolve the actual app host
        if (processPath is not null
            && !System.IO.Path.GetFileNameWithoutExtension(processPath)
                    .Equals("dotnet", StringComparison.OrdinalIgnoreCase))
        {
            return processPath;
        }

        var asmLocation = System.Reflection.Assembly.GetEntryAssembly()?.Location;
        if (!string.IsNullOrEmpty(asmLocation))
        {
            return System.IO.Path.ChangeExtension(asmLocation, ".exe");
        }

        return processPath;
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // Prevent the app from crashing on UI thread exceptions
        System.Diagnostics.Debug.WriteLine($"[VerbaCore] Unhandled UI exception: {e.Exception}");
        e.Handled = true;
    }

    private static void OnUnobservedTaskException(object? sender, System.Threading.Tasks.UnobservedTaskExceptionEventArgs e)
    {
        // Prevent the app from crashing on unobserved async exceptions
        System.Diagnostics.Debug.WriteLine($"[VerbaCore] Unobserved task exception: {e.Exception}");
        e.SetObserved();
    }

    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        // Last resort — log but can't prevent termination if e.IsTerminating
        System.Diagnostics.Debug.WriteLine($"[VerbaCore] Unhandled domain exception: {e.ExceptionObject}");
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // CapsLock, tray, overlay already cleaned up in ExitApp();
        // only dispose remaining resources here for safety
        if (_capsLockService is { } cls)
        {
            cls.Dispose();
            _capsLockService = null;
        }
        if (_trayIcon is { } tray)
        {
            tray.Dispose();
            _trayIcon = null;
        }
        _singleInstanceMutex?.ReleaseMutex();
        _singleInstanceMutex?.Dispose();
        _singleInstanceMutex = null;
        Services.Dispose();
        base.OnExit(e);
    }
}
