using System.Globalization;
using System.Windows;
using System.Windows.Data;
using VerbaCore.Models;
using Wpf.Ui.Controls;

namespace VerbaCore.Helpers;

/// <summary>
/// Converts LookupMode to ButtonAppearance (Primary if selected, Secondary otherwise)
/// </summary>
public sealed class ModeToAppearanceConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is LookupMode current && parameter is string paramStr)
        {
            var target = Enum.Parse<LookupMode>(paramStr);
            return current == target
                ? ControlAppearance.Primary
                : ControlAppearance.Secondary;
        }
        return ControlAppearance.Secondary;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Inverts a boolean value
/// </summary>
public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b ? !b : value;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b ? !b : value;
}

/// <summary>
/// Converts bool to Visibility (True = Visible, False = Collapsed)
/// </summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var flag = value is true;
        if (parameter is string s && s.Equals("Invert", StringComparison.OrdinalIgnoreCase))
            flag = !flag;
        return flag ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is Visibility.Visible;
}

/// <summary>
/// Shows element when count is zero (for empty state)
/// </summary>
public sealed class ZeroToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is 0 ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Returns appropriate placeholder text based on API provider selection
/// </summary>
public sealed class BoolToAzureKeyPlaceholderConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? "Azure API Key..." : "sk-...";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
