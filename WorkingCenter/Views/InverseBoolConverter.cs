using System.Globalization;
using System.Windows.Data;

namespace WorkingCenter.Views;

/// <summary>bool -> inverted bool (true becomes false and vice versa).
/// Used e.g. by the "否" radio of a yes/no pair bound to a single bool state.</summary>
internal sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b ? !b : false;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b ? !b : false;
}
