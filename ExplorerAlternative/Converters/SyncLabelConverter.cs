using System.Globalization;
using System.Windows.Data;

namespace ExplorerAlternative.Converters;

/// <summary>仕様書9.2章：ターミナルの同期ON/OFF切り替えボタンの表示文言。</summary>
public sealed class SyncLabelConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var isOn = value is bool b && b;
        return isOn ? "同期 ON" : "同期 OFF";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
