using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace MicForge.Converters;

/// <summary>Multi-binding: all inputs true → <see cref="Visibility.Collapsed"/> (inverse of
/// <see cref="AllTrueToVisibleConverter"/>).</summary>
public sealed class AllTrueToCollapsedConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        foreach (var v in values)
            if (v is not bool b || !b) return Visibility.Visible;
        return Visibility.Collapsed;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
