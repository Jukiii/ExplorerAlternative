using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace ExplorerAlternative.Converters;

/// <summary>仕様書5章：タグに設定された#RRGGBB文字列をBrushへ変換する。未設定時はグレー。</summary>
public sealed class HexColorToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string hex && !string.IsNullOrWhiteSpace(hex))
        {
            try
            {
                return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            }
            catch (FormatException)
            {
                // 不正な色文字列は既定色にフォールバックする。
            }
        }

        return new SolidColorBrush(Color.FromRgb(0x6B, 0x72, 0x80));
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
