using System.ComponentModel;
using System.Windows;
using Wpf.Ui.Controls;
using VerbaCore.Helpers;
using VerbaCore.Services;
using VerbaCore.ViewModels;

namespace VerbaCore;

public partial class MainWindow : FluentWindow
{
    private readonly HotkeyService _hotkeyService;
    private readonly ClipboardMonitorService _clipboardService;
    private readonly SettingsService _settingsService;
    private readonly MainViewModel _mainVm;

    public MainWindow()
    {
        _settingsService = App.GetService<SettingsService>();
        _hotkeyService = App.GetService<HotkeyService>();
        _clipboardService = App.GetService<ClipboardMonitorService>();
        _mainVm = App.GetService<MainViewModel>();

        InitializeComponent();

        // Set DataContexts
        MainViewPage.DataContext = _mainVm;
        SettingsViewPage.DataContext = App.GetService<SettingsViewModel>();
        HistoryViewPage.DataContext = App.GetService<HistoryViewModel>();

        Loaded += OnLoaded;
        Closing += OnClosing;
        StateChanged += OnStateChanged;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Register global hotkey
        _hotkeyService.HotkeyPressed += OnHotkeyPressed;
        _hotkeyService.Register(_settingsService.Current.GlobalHotkey);

        // Start clipboard monitoring
        if (_settingsService.Current.ClipboardMonitorEnabled)
        {
            _clipboardService.ClipboardTextChanged += OnClipboardTextChanged;
            _clipboardService.Start(this);
        }
    }

    private void OnHotkeyPressed(object? sender, EventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            if (WindowState == WindowState.Minimized)
                WindowState = WindowState.Normal;

            Show();
            Activate();
            NativeMethods.SetForegroundWindow(new System.Windows.Interop.WindowInteropHelper(this).Handle);

            // Focus input
            MainTabControl.SelectedIndex = 0;
            MainViewPage.FocusInput();
        });
    }

    private void OnClipboardTextChanged(object? sender, string text)
    {
        _mainVm.HandleClipboardText(text);
    }

    private void OnStateChanged(object? sender, EventArgs e)
    {
        // Minimize to tray behavior can be added later
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        _hotkeyService.Dispose();
        _clipboardService.Dispose();
    }
}