using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace MicForge.Converters;

/// <summary>Null/empty string → <see cref="Visibility.Collapsed"/>, otherwise Visible.</summary>
public sealed class EmptyStringToCollapsedConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => string.IsNullOrWhiteSpace(value as string) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
