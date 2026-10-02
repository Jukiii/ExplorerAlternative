namespace ExplorerAlternative.ViewModels;

/// <summary>
/// 仕様書13章「PDF」：全ページを縦に並べてスクロールしているとき、見えているページの範囲と、
/// いまのページ番号を、各ページの上端・下端の位置（表示領域の上端を0とした座標）から求める。
/// 画面（WPF）に依存しない計算だけを持ち、画面（View）は、各ページの位置を渡して結果を受け取る。
/// </summary>
public static class PdfScrollCalculator
{
    /// <param name="FirstVisible">見えている最初のページ（0始まり）。</param>
    /// <param name="LastVisible">見えている最後のページ（0始まり）。</param>
    /// <param name="CurrentPage">いまのページ（0始まり）。</param>
    public readonly record struct VisibleRange(int FirstVisible, int LastVisible, int CurrentPage);

    /// <summary>
    /// 見えているページの範囲を求める。<paramref name="pages"/>は、ページ順の、上端と下端の位置
    /// （表示領域の上端が0、下端が<paramref name="viewportHeight"/>）。位置が分からないページは、
    /// <c>null</c>にする（飛ばす）。1ページも見えない場合は、<c>null</c>を返す。
    /// </summary>
    /// <remarks>
    /// 「いまのページ」は、画面の上から3分の1より下まで続いている、最初のページ。
    /// 次のページの頭が、画面の上3分の1に入ったら、そのページに切り替わる。
    /// ただし、見えている最初のページの頭が画面の上端以下にあるとき（先頭までスクロールしているとき等）は、
    /// ページが短くても、そのページをいまのページとする。
    /// </remarks>
    public static VisibleRange? Compute(IReadOnlyList<(double Top, double Bottom)?> pages, double viewportHeight)
    {
        if (viewportHeight <= 0)
        {
            return null;
        }

        var first = -1;
        var last = -1;
        var current = -1;

        for (var i = 0; i < pages.Count; i++)
        {
            if (pages[i] is not { } bounds)
            {
                continue;
            }

            if (bounds.Top >= viewportHeight)
            {
                break;
            }

            if (bounds.Bottom <= 0)
            {
                continue;
            }

            if (first < 0)
            {
                first = i;
            }

            last = i;

            if (current < 0 && bounds.Bottom > viewportHeight / 3)
            {
                current = i;
            }
        }

        if (first < 0)
        {
            return null;
        }

        if (pages[first] is { Top: >= 0 })
        {
            current = first;
        }

        return new VisibleRange(first, last, current < 0 ? first : current);
    }
}
