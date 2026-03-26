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
        services.AddSingleton<SpeechInputService>();
        services.AddSingleton<CursorTextService>();
        services.AddSingleton<HotkeyService>();
        services.AddHttpClient<IOpenAiService, OpenAiService>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(60);
            client.DefaultRequestVersion = new Version(2, 0);
            client.DefaultVersionPolicy = System.Net.Http.HttpVersionPolicy.RequestVersionOrLower;
        });

        // ViewModels
        services.AddSingleton<MainViewModel>();
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
        ApplyStartWithWindows(settings.Current.StartWithWindows);

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
            Icon = SystemIcons.Application,
            Text = "VerbaCore — CapsLock으로 AI 사전/번역",
            Visible = true,
            ContextMenuStrip = contextMenu
        };

        _trayIcon.DoubleClick += (_, _) => ShowSettingsWindow();
    }

    private void ShowSettingsWindow()
    {
        if (_settingsWindow == null)
        {
            _settingsWindow = new SettingsWindow();
        }

        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    private void ExitApp()
    {
        _capsLockService?.Dispose();
        _trayIcon?.Dispose();
        _overlayWindow?.Close();
        _settingsWindow?.Close();
        _singleInstanceMutex?.ReleaseMutex();
        _singleInstanceMutex?.Dispose();
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
        MessageBox.Show($"An error occurred:\n{e.Exception.Message}", "VerbaCore Error",
            MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _capsLockService?.Dispose();
        _trayIcon?.Dispose();
        _singleInstanceMutex?.ReleaseMutex();
        _singleInstanceMutex?.Dispose();
        Services.Dispose();
        base.OnExit(e);
    }
}
