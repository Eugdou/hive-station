using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace WorkingCenter.Views;

/// <summary>Non-empty string -> Visible; empty/null -> Collapsed.</summary>
internal sealed class NonEmptyToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        string? s = value as string;
        return !string.IsNullOrEmpty(s) ? Visibility.Visible : Visibility.Collapsed;
    }
    public object? ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is Visibility v && v == Visibility.Visible ? "" : (object?)null;
}
