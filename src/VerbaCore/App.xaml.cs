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
        services.AddSingleton<LookupCacheService>();
        services.AddSingleton<LocalizationService>();
        services.AddSingleton<PromptBuilder>();
        services.AddSingleton<CapsLockService>();
        services.AddSingleton<CursorTextService>();
        services.AddSingleton<HotkeyService>();

        // Dedicated HttpClient for update checks (separate from OpenAiService's client).
        services.AddSingleton<System.Net.Http.HttpClient>(_ =>
            new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(30) });
        services.AddSingleton<UpdateService>();

        services.AddSingleton<IOpenAiService>(sp =>
        {
            var handler = new System.Net.Http.HttpClientHandler
            {
                AutomaticDecompression = System.Net.DecompressionMethods.All
            };
            var client = new System.Net.Http.HttpClient(handler, disposeHandler: true)
            {
                Timeout = TimeSpan.FromSeconds(60),
                DefaultRequestVersion = new Version(2, 0),
                DefaultVersionPolicy = System.Net.Http.HttpVersionPolicy.RequestVersionOrLower
            };
            return new OpenAiService(
                client,
                sp.GetRequiredService<SettingsService>(),
                sp.GetRequiredService<PromptBuilder>());
        });

        // ViewModels
        services.AddSingleton<SettingsViewModel>();
        services.AddTransient<HistoryViewModel>(sp =>
        {
            var historyService = sp.GetRequiredService<HistoryService>();
            return new HistoryViewModel(historyService, _ => { });
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
            MessageBox.Show("VerbaCore is already running.\nVerbaCore가 이미 실행 중입니다.", "VerbaCore",
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

        var cache = GetService<LookupCacheService>();
        await cache.LoadAsync();

        // Apply saved settings
        ApplyTheme(settings.Current.Theme);

        var loc = GetService<LocalizationService>();
        loc.Apply(settings.Current.UiLanguage);

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
            GetService<CursorTextService>(),
            cache);

        // Install CapsLock hook
        _capsLockService.Install();

        // Set up system tray icon
        SetupTrayIcon();

        // Warm up the slow one-time costs during startup idle so the FIRST CapsLock
        // activation pops up instantly: (1) build/render the overlay's visual tree
        // off-screen, and (2) initialize the UIA accessibility client. Both otherwise
        // run on the critical path the first time and can cost several seconds.
        var cursorText = GetService<CursorTextService>();
        _ = Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            _overlayWindow?.PrimeRender();
            cursorText.WarmUp();
        });

        // Background auto-update check (fire-and-forget). Throttled to once per 24h.
        if (settings.Current.AutoCheckUpdate)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                    if (DateTime.UtcNow - settings.Current.LastUpdateCheckUtc < TimeSpan.FromHours(24))
                        return;
                    await Dispatcher.InvokeAsync(() => CheckForUpdatesAsync(silentIfNone: true));
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[VerbaCore] Auto-update check failed: {ex.Message}");
                }
            });
        }
    }

    /// <summary>
    /// Check for updates and, if one is available, prompt the user to download & install.
    /// When <paramref name="silentIfNone"/> is true, no message is shown when up-to-date or on error.
    /// </summary>
    public async Task CheckForUpdatesAsync(bool silentIfNone)
    {
        var loc = GetService<LocalizationService>();
        var settings = GetService<SettingsService>();
        var updater = GetService<UpdateService>();

        var (info, available) = await updater.CheckAsync(CancellationToken.None);

        if (info is null)
        {
            if (!silentIfNone)
                MessageBox.Show(loc.Get("Update_Failed"), "VerbaCore",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!available)
        {
            if (!silentIfNone)
                MessageBox.Show(loc.Get("Update_UpToDate"), "VerbaCore",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // Respect skip preference for auto checks only.
        if (silentIfNone && info.Version == settings.Current.SkippedUpdateVersion)
            return;

        var prompt = string.Format(loc.Get("Update_Available"),
            info.Version, UpdateService.CurrentVersion);
        if (!string.IsNullOrWhiteSpace(info.ReleaseNotes))
            prompt += "\n\n" + info.ReleaseNotes;

        var result = MessageBox.Show(prompt, "VerbaCore",
            MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes)
        {
            if (silentIfNone)
            {
                settings.Current.SkippedUpdateVersion = info.Version;
                _ = settings.SaveAsync();
            }
            return;
        }

        try
        {
            var path = await updater.DownloadAsync(info, progress: null, CancellationToken.None);
            UpdateService.LaunchInstaller(path);
            // Inno Setup with CloseApplications=force will terminate us; shut down gracefully now.
            ExitApp();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"{loc.Get("Update_Failed")}\n\n{ex.Message}", "VerbaCore",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SetupTrayIcon()
    {
        var loc = GetService<LocalizationService>();
        BuildTrayMenu(loc);

        _trayIcon = new System.Windows.Forms.NotifyIcon
        {
            Icon = LoadTrayIcon(),
            Text = loc.Get("Tray_Tooltip"),
            Visible = true,
            ContextMenuStrip = _trayContextMenu
        };

        _trayIcon.DoubleClick += (_, _) => ShowSettingsWindow();

        loc.LanguageChanged += () =>
        {
            BuildTrayMenu(loc);
            if (_trayIcon is not null)
            {
                _trayIcon.Text = loc.Get("Tray_Tooltip");
                _trayIcon.ContextMenuStrip = _trayContextMenu;
            }
        };
    }

    private System.Windows.Forms.ContextMenuStrip? _trayContextMenu;

    private void BuildTrayMenu(LocalizationService loc)
    {
        _trayContextMenu?.Dispose();
        var contextMenu = new System.Windows.Forms.ContextMenuStrip();
        contextMenu.Items.Add(loc.Get("Tray_Settings"), null, (_, _) => ShowSettingsWindow());
        contextMenu.Items.Add(loc.Get("Tray_CheckUpdate"), null,
            async (_, _) => await CheckForUpdatesAsync(silentIfNone: false));
        contextMenu.Items.Add("-");
        contextMenu.Items.Add(loc.Get("Tray_About"), null, (_, _) =>
            MessageBox.Show(
                loc.Get("Tray_AboutText"),
                "VerbaCore", MessageBoxButton.OK, MessageBoxImage.Information));
        contextMenu.Items.Add("-");
        contextMenu.Items.Add(loc.Get("Tray_Exit"), null, (_, _) => ExitApp());
        _trayContextMenu = contextMenu;
    }

    private static Icon LoadTrayIcon()
    {
        // Load from the embedded WPF resource — works identically in dev, single-file publish, and installed builds.
        try
        {
            var uri = new Uri("pack://application:,,,/res/icons/verbacore.ico", UriKind.Absolute);
            var info = Application.GetResourceStream(uri);
            if (info?.Stream is { } stream)
            {
                using (stream)
                {
                    return new Icon(stream, 32, 32);
                }
            }
        }
        catch { /* fall through */ }

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
        _trayContextMenu?.Dispose();
        _trayContextMenu = null;
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
