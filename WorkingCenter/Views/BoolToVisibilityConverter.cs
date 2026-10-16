using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace WorkingCenter.Views;

/// <summary>Matches an enum (or bool) value to a ConverterParameter string.
/// - For enums: Visibility.Visible when value.ToString() == parameter (case-insensitive), else Collapsed.
/// - For bool with parameter "invert": Visible when value is false.
/// - For bool with no parameter: Visible when value is true.</summary>
internal sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        string? p = parameter as string;
        // Enum match path (used for CurrentView: Work/Settings)
        if (!string.IsNullOrEmpty(p) && p != "invert")
        {
            bool match = string.Equals(value?.ToString(), p, StringComparison.OrdinalIgnoreCase);
            return match ? Visibility.Visible : Visibility.Collapsed;
        }
        // Bool path
        bool b = value is bool v && v;
        bool invert = p == "invert";
        if (invert) b = !b;
        return b ? Visibility.Visible : Visibility.Collapsed;
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is Visibility vis && vis == Visibility.Visible;
}
