using System.Globalization;
using System.Windows;
using System.Windows.Data;
using ExplorerAlternative.Models;

namespace ExplorerAlternative.Converters;

/// <summary>仕様書12.3章：Git/SVNいずれにも該当しない場合は情報を表示しない。</summary>
public sealed class VcsKindToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is VersionControlKind.None or null ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
