using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Idvbp.Neo.Converters;

public class LogLevelToColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string level)
            return Brushes.White;

        return level.ToUpperInvariant() switch
        {
            "ERROR" or "FATAL" or "CRITICAL" => new SolidColorBrush(Color.Parse("#FF6B6B")),
            "WARN" or "WARNING" => new SolidColorBrush(Color.Parse("#FFA94D")),
            "INFO" => new SolidColorBrush(Color.Parse("#74C0FC")),
            "DEBUG" => new SolidColorBrush(Color.Parse("#8CE99A")),
            "TRACE" => new SolidColorBrush(Color.Parse("#B197FC")),
            _ => Brushes.White
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
