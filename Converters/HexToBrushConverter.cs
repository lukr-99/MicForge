using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace MicForge.Converters;

/// <summary>Converts a <c>"#RRGGBB"</c> string into a frozen <see cref="SolidColorBrush"/>.</summary>
public sealed class HexToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        try
        {
            var brush = (SolidColorBrush)new BrushConverter().ConvertFromString((string)value);
            brush.Freeze();
            return brush;
        }
        catch { return Brushes.Gray; }
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
