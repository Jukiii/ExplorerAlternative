using System.Windows.Media.Imaging;
using ExplorerAlternative.Mvvm;

namespace ExplorerAlternative.ViewModels;

/// <summary>
/// PDFプレビューの1ページ分（仕様書13章「PDF」）。全ページを縦に並べてスクロールで見られるように、
/// 画像を描く前から、ページの場所（大きさ）が決まっている。画像は、見える範囲の近くのページだけ描き、
/// 遠くなったページは捨てる（ページ数が多いPDFでも、メモリを使いすぎないため）。
/// </summary>
public sealed class PdfPageItem : ObservableObject
{
    private BitmapSource? _image;
    private string? _failureMessage;

    public PdfPageItem(int pageNumber, double layoutWidth, double layoutHeight)
    {
        PageNumber = pageNumber;
        LayoutWidth = layoutWidth;
        LayoutHeight = layoutHeight;
    }

    /// <summary>ページ番号（1始まり）。</summary>
    public int PageNumber { get; }

    /// <summary>画面上の、ページの幅と高さ（縦横比どおり。窓が狭いときは、全体が縮小される）。</summary>
    public double LayoutWidth { get; }

    public double LayoutHeight { get; }

    /// <summary>描画済みの画像。まだ・もう描いていないページは<c>null</c>（その間は、ページ番号の入った空白を表示する）。</summary>
    public BitmapSource? Image
    {
        get => _image;
        internal set
        {
            if (SetProperty(ref _image, value))
            {
                OnPropertyChanged(nameof(IsPlaceholder));
            }
        }
    }

    public bool IsPlaceholder => _image is null;

    /// <summary>このページを描けなかったときの理由（描けているときは<c>null</c>）。</summary>
    public string? FailureMessage
    {
        get => _failureMessage;
        internal set => SetProperty(ref _failureMessage, value);
    }
}
