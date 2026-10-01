using System.Windows.Media.Imaging;

namespace ExplorerAlternative.Services.Abstractions;

/// <summary>
/// PDFを開いて、ページを画像として描画する（仕様書13章「PDF」）。Windows標準のPDF描画
/// （<c>Windows.Data.Pdf</c>）を使い、外部のライブラリは追加しない。
/// </summary>
public interface IPdfRenderService
{
    /// <summary>
    /// PDFを開く。開けなかった場合（壊れている・パスワード保護・読み取りできない等）は、例外にせず、
    /// <see cref="PdfOpenResult.FailureReason"/>に、利用者に見せる理由を入れて返す。
    /// </summary>
    Task<PdfOpenResult> OpenAsync(string path, CancellationToken cancellationToken);
}

/// <summary>開いたPDF。使い終わったら<see cref="IDisposable.Dispose"/>で閉じる（ファイルを開いたままにしない）。</summary>
public interface IPdfDocument : IDisposable
{
    /// <summary>総ページ数。</summary>
    int PageCount { get; }

    /// <summary>
    /// 指定のページ（0始まり）を、幅<paramref name="widthPixels"/>ピクセルの画像として描画する（縦横比は保つ）。
    /// 描画した画像は、別のスレッドからも使えるよう、フリーズしてある。
    /// </summary>
    Task<BitmapSource> RenderPageAsync(int pageIndex, int widthPixels, CancellationToken cancellationToken);
}

/// <summary>PDFを開いた結果。成功なら<see cref="Document"/>、失敗なら<see cref="FailureReason"/>に理由が入る。</summary>
public sealed record PdfOpenResult(IPdfDocument? Document, string? FailureReason)
{
    public static PdfOpenResult Success(IPdfDocument document) => new(document, null);

    public static PdfOpenResult Failure(string reason) => new(null, reason);
}
