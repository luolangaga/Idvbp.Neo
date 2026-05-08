using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Idvbp.Neo.Services;

namespace Idvbp.Neo.Converters;

public sealed class AppNotificationSeverityBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var severity = value is AppNotificationSeverity notificationSeverity
            ? notificationSeverity
            : AppNotificationSeverity.Info;

        var variant = parameter as string;
        var color = (severity, variant) switch
        {
            (AppNotificationSeverity.Success, "Accent") => "#46D16A",
            (AppNotificationSeverity.Warning, "Accent") => "#F0B429",
            (AppNotificationSeverity.Error, "Accent") => "#FF6B6B",
            (_, "Accent") => "#6EA8FE",
            (AppNotificationSeverity.Success, _) => "#E51E3A25",
            (AppNotificationSeverity.Warning, _) => "#E5403218",
            (AppNotificationSeverity.Error, _) => "#E5421F24",
            _ => "#E51F2937"
        };

        return SolidColorBrush.Parse(color);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
