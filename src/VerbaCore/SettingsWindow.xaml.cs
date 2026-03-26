using System.ComponentModel;
using Wpf.Ui.Controls;

namespace VerbaCore;

public partial class SettingsWindow : FluentWindow
{
    public SettingsWindow()
    {
        InitializeComponent();

        SettingsViewPage.DataContext = App.GetService<ViewModels.SettingsViewModel>();
        HistoryViewPage.DataContext = App.GetService<ViewModels.HistoryViewModel>();

        Closing += OnClosing;

        // Refresh history when the window becomes visible
        IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is true)
            {
                if (HistoryViewPage.DataContext is ViewModels.HistoryViewModel hvm)
                    hvm.RefreshItems();
            }
        };
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        // Don't close, just hide — reuse the window
        e.Cancel = true;
        Hide();
    }
}
