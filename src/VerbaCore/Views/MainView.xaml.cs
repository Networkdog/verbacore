using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace VerbaCore.Views;

public partial class MainView : UserControl
{
    public MainView()
    {
        InitializeComponent();
    }

    public void FocusInput()
    {
        InputTextBox.Focus();
        InputTextBox.SelectAll();
    }

    private void InputTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            var vm = DataContext as ViewModels.MainViewModel;
            vm?.LookupCommand.Execute(null);
            e.Handled = true;
        }
    }
}
