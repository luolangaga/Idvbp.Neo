using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace Idvbp.Neo.Converters;

/// <summary>
/// 整型数值相等转换器，用于将整数与参数比较后返回布尔值，常用于 RadioButton 等场景。
/// </summary>
public class IntEqualsConverter : IValueConverter
{
    /// <summary>
    /// 将整数值与参数进行比较，返回是否相等。
    /// </summary>
    /// <param name="value">整数值。</param>
    /// <param name="targetType">目标类型。</param>
    /// <param name="parameter">比较目标值（字符串形式）。</param>
    /// <param name="culture">区域文化信息。</param>
    /// <returns>相等返回 true，否则返回 false。</returns>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is int intValue && parameter is string paramStr && int.TryParse(paramStr, out var target))
            return intValue == target;
        return false;
    }

    /// <summary>
    /// 将布尔值反向转换为对应的整数值。
    /// </summary>
    /// <param name="value">布尔值。</param>
    /// <param name="targetType">目标类型。</param>
    /// <param name="parameter">目标整数值（字符串形式）。</param>
    /// <param name="culture">区域文化信息。</param>
    /// <returns>若值为 true 则返回对应整数，否则返回 null。</returns>
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is true && parameter is string paramStr && int.TryParse(paramStr, out var target))
            return target;
        return null;
    }
}
