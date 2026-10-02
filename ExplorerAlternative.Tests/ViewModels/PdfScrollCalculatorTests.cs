using ExplorerAlternative.ViewModels;

namespace ExplorerAlternative.Tests.ViewModels;

// 仕様書13章「PDF」：スクロール位置から、見えているページの範囲と、いまのページ番号を求める計算。
public sealed class PdfScrollCalculatorTests
{
    private const double Viewport = 600;

    // 高さ800のページを、間隔10で、縦に並べる。scrollは、スクロール量（表示領域の上端が、全体のどこにあるか）。
    private static List<(double Top, double Bottom)?> Pages(int count, double scroll, double pageHeight = 800, double gap = 10)
    {
        var list = new List<(double Top, double Bottom)?>();
        for (var i = 0; i < count; i++)
        {
            var top = (i * (pageHeight + gap)) - scroll;
            list.Add((top, top + pageHeight));
        }

        return list;
    }

    [Fact]
    public void AtTheTop_FirstPageIsCurrent_AndOnlyItIsVisible()
    {
        var range = PdfScrollCalculator.Compute(Pages(10, 0), Viewport);

        Assert.Equal(new PdfScrollCalculator.VisibleRange(0, 0, 0), range);
    }

    [Fact]
    public void WhenTwoPagesAreVisible_BothAreInTheRange_AndTheUpperOneIsCurrent()
    {
        // 500だけ進める → 1ページ目は残り300、2ページ目の頭が、画面の中ほど（310）から見える。
        var range = PdfScrollCalculator.Compute(Pages(10, 500), Viewport)!.Value;

        Assert.Equal(0, range.FirstVisible);
        Assert.Equal(1, range.LastVisible);
        Assert.Equal(0, range.CurrentPage); // 1ページ目の下端(300) > 画面の3分の1(200)
    }

    [Fact]
    public void WhenTheNextPageReachesTheUpperThird_ItBecomesCurrent()
    {
        // 700進める → 1ページ目の下端は100（画面の3分の1=200より上）。2ページ目が画面の上のほうを占める。
        var range = PdfScrollCalculator.Compute(Pages(10, 700), Viewport)!.Value;

        Assert.Equal(1, range.CurrentPage);
        Assert.Equal(0, range.FirstVisible); // 1ページ目の下端がまだ少し見えている
    }

    [Fact]
    public void ScrolledDeep_ReportsThePagesAroundThere()
    {
        // 5ページ目(0始まり4)の頭が、画面の上端に来る位置。
        var range = PdfScrollCalculator.Compute(Pages(10, 4 * 810), Viewport)!.Value;

        Assert.Equal(4, range.FirstVisible);
        Assert.Equal(4, range.LastVisible);
        Assert.Equal(4, range.CurrentPage);
    }

    [Fact]
    public void ShortPages_ManyAreVisibleAtOnce()
    {
        var range = PdfScrollCalculator.Compute(Pages(20, 0, pageHeight: 100), Viewport)!.Value;

        Assert.Equal(0, range.FirstVisible);
        Assert.Equal(5, range.LastVisible); // 0〜5ページ目（110刻み、6ページ目の頭が550）
        Assert.Equal(0, range.CurrentPage);
    }

    [Fact]
    public void AtTheVeryEnd_TheLastPageIsCurrentAndVisible()
    {
        var count = 5;
        var total = (count * 810) - 10;
        var range = PdfScrollCalculator.Compute(Pages(count, total - Viewport), Viewport)!.Value;

        Assert.Equal(count - 1, range.LastVisible);
        Assert.True(range.CurrentPage >= count - 2);
    }

    [Fact]
    public void PagesWithUnknownPositions_AreSkipped()
    {
        var pages = Pages(5, 500); // 1ページ目と2ページ目が見えている位置
        pages[0] = null; // 1ページ目の位置が分からない

        var range = PdfScrollCalculator.Compute(pages, Viewport)!.Value;

        Assert.Equal(1, range.FirstVisible);
        Assert.Equal(1, range.LastVisible);
    }

    [Fact]
    public void NothingVisible_GivesNull()
    {
        // どのページも、表示領域より下にある。
        var below = new List<(double Top, double Bottom)?> { (700, 1500), (1510, 2310) };
        // どのページも、表示領域より上にある。
        var above = new List<(double Top, double Bottom)?> { (-1500, -700), (-690, -10) };

        Assert.Null(PdfScrollCalculator.Compute(below, Viewport));
        Assert.Null(PdfScrollCalculator.Compute(above, Viewport));
        Assert.Null(PdfScrollCalculator.Compute(new List<(double Top, double Bottom)?>(), Viewport));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    public void NoViewport_GivesNull(double viewport)
    {
        Assert.Null(PdfScrollCalculator.Compute(Pages(3, 0), viewport));
    }

    [Fact]
    public void WorksWhenPositionsAfterTheViewportAreNotProvided()
    {
        // 画面（View）は、表示領域より下のページの位置を調べずに、打ち切る。それでも、同じ結果になる。
        var full = Pages(10, 500);
        var truncated = full.Take(2).ToList();

        Assert.Equal(PdfScrollCalculator.Compute(full, Viewport), PdfScrollCalculator.Compute(truncated, Viewport));
    }
}
