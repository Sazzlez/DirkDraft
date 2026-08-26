using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace DraftPilot.App;

/// <summary>Shows an element only while a bool is false — the idle card is the draft view's negative.</summary>
public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is Visibility.Collapsed;
}
