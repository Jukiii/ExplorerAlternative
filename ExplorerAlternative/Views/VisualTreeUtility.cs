using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace ExplorerAlternative.Views;

/// <summary>
/// マウス操作の <c>OriginalSource</c> から親方向へたどるための共通処理。
///
/// <see cref="VisualTreeHelper.GetParent"/> は <see cref="Visual"/>／<see cref="Visual3D"/> 以外を
/// 渡すと例外（「Paragraph は Visual または Visual3D ではありません」）を投げる。ところが
/// RichTextBoxの文字部分をクリックすると、OriginalSourceは <see cref="Visual"/> ではなく
/// <see cref="ContentElement"/>（<c>Run</c>・<c>Paragraph</c>等）になる。ターミナル画面（17章）は
/// RichTextBoxで文字を描画しているため、その文字部分をクリックするだけでアプリが落ちていた。
/// ContentElementも含めてたどれるようにする。
/// </summary>
public static class VisualTreeUtility
{
    /// <summary>
    /// 親要素を返す。Visualはビジュアルツリー、ContentElementはそのコンテンツの親をたどり、
    /// たどれない場合はロジカルツリーで探す。親が無い場合はnull。
    /// </summary>
    public static DependencyObject? GetParent(DependencyObject child)
    {
        if (child is Visual or Visual3D)
        {
            return VisualTreeHelper.GetParent(child);
        }

        if (child is ContentElement contentElement)
        {
            var contentParent = ContentOperations.GetParent(contentElement);
            if (contentParent is not null)
            {
                return contentParent;
            }
        }

        return LogicalTreeHelper.GetParent(child);
    }
}
