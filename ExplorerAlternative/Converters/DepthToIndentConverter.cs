using System.Globalization;
using System.Windows.Data;

namespace ExplorerAlternative.Converters;

/// <summary>階層表示のインデント幅を、ノードの深さから算出する（仕様書4章）。</summary>
public sealed class DepthToIndentConverter : IValueConverter
{
    private const double IndentWidth = 16;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var depth = value is int i ? i : 0;
        return new System.Windows.Thickness(depth * IndentWidth, 0, 0, 0);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
