using System.Globalization;
using System.Windows.Data;

namespace ExplorerAlternative.Converters;

/// <summary>表示モード切替ボタン等、値がConverterParameterと一致するかをbool化する。</summary>
public sealed class EnumEqualsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is not null && parameter is not null && value.Equals(parameter);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
