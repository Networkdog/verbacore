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
    private readonly SettingsService _settingsService;
    private readonly MainViewModel _mainVm;

    public MainWindow()
    {
        _settingsService = App.GetService<SettingsService>();
        _hotkeyService = App.GetService<HotkeyService>();
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
        // Only register once — Loaded fires each time Show() is called
        if (_hotkeyRegistered) return;
        _hotkeyRegistered = true;

        // Register global hotkey
        _hotkeyService.HotkeyPressed += OnHotkeyPressed;
        _hotkeyService.Register(_settingsService.Current.GlobalHotkey);
    }

    private bool _hotkeyRegistered;

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

    private void OnStateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized)
        {
            Hide();
        }
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        // Hide to tray instead of closing
        e.Cancel = true;
        Hide();
    }
}