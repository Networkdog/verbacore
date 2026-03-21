using System.Drawing;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using VerbaCore.Models;
using VerbaCore.Services;
using VerbaCore.ViewModels;
using Wpf.Ui.Appearance;

namespace VerbaCore;

public partial class App : Application
{
    private static readonly ServiceProvider Services = ConfigureServices();
    private System.Windows.Forms.NotifyIcon? _trayIcon;
    private SettingsWindow? _settingsWindow;
    private OverlayWindow? _overlayWindow;
    private CapsLockService? _capsLockService;
    private ClipboardMonitorService? _clipboardService;

    public static T GetService<T>() where T : notnull => Services.GetRequiredService<T>();

    private static ServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();

        // Services
        services.AddSingleton<SettingsService>();
        services.AddSingleton<HistoryService>();
        services.AddSingleton<PromptBuilder>();
        services.AddSingleton<CapsLockService>();
        services.AddSingleton<ClipboardMonitorService>();
        services.AddSingleton<SpeechInputService>();
        services.AddSingleton<CursorTextService>();
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
        base.OnStartup(e);

        // Load settings & history
        var settings = GetService<SettingsService>();
        await settings.LoadAsync();

        var history = GetService<HistoryService>();
        await history.LoadAsync();

        // Apply theme
        ApplyTheme(settings.Current.Theme);

        // Create overlay window (hidden by default)
        _capsLockService = GetService<CapsLockService>();
        _overlayWindow = new OverlayWindow(
            GetService<IOpenAiService>(),
            settings,
            history,
            _capsLockService);

        // Install CapsLock hook
        _capsLockService.Install();

        // Set up system tray icon
        SetupTrayIcon();

        // Start clipboard monitoring if enabled
        if (settings.Current.ClipboardMonitorEnabled)
        {
            _clipboardService = GetService<ClipboardMonitorService>();
            var clipboardHelper = new Window
            {
                Width = 1, Height = 1,
                WindowStyle = System.Windows.WindowStyle.None,
                AllowsTransparency = true,
                Opacity = 0,
                ShowInTaskbar = false,
                ShowActivated = false
            };
            clipboardHelper.Show();
            _clipboardService.Start(clipboardHelper);
            _clipboardService.ClipboardTextChanged += OnClipboardTextChanged;
        }
    }

    private void OnClipboardTextChanged(object? sender, string text)
    {
        // Show overlay with clipboard text
        _overlayWindow?.Dispatcher.Invoke(() =>
        {
            if (_overlayWindow != null)
            {
                _capsLockService?.ClearBuffer();
                // Directly trigger lookup via overlay
            }
        });
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
                "Tab: 모드 전환 (사전/번역/분석)\n" +
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
        _clipboardService?.Dispose();
        _trayIcon?.Dispose();
        _overlayWindow?.Close();
        _settingsWindow?.Close();
        Shutdown();
    }

    public static void ApplyTheme(ThemeMode mode)
    {
        var theme = mode switch
        {
            ThemeMode.Light => ApplicationTheme.Light,
            ThemeMode.Dark => ApplicationTheme.Dark,
            _ => ApplicationTheme.Dark
        };
        ApplicationThemeManager.Apply(theme);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show($"An error occurred:\n{e.Exception.Message}", "VerbaCore Error",
            MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
