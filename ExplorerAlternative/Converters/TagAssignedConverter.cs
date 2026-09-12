using System.Globalization;
using System.Windows.Data;
using ExplorerAlternative.Models;
using ExplorerAlternative.ViewModels;

namespace ExplorerAlternative.Converters;

/// <summary>コンテキストメニューの「タグ」項目で、選択中ノードにそのタグが付与済みかをチェック状態にする。</summary>
public sealed class TagAssignedConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Length < 2 || values[0] is not TagDefinition tag || values[1] is not IReadOnlyList<string> tags)
        {
            return false;
        }

        return tags.Contains(tag.Name);
    }

    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
