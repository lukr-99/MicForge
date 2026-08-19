using System;
using System.Globalization;
using System.Windows.Data;

namespace MicForge.Converters;

/// <summary>
/// True when the bound value equals the <c>ConverterParameter</c> string; converts back to
/// the parameter when set true (used to drive single-select filter chips / radio buttons).
/// </summary>
public sealed class StringEqualsConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => string.Equals(value as string, parameter as string, StringComparison.Ordinal);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? parameter : Binding.DoNothing;
}
