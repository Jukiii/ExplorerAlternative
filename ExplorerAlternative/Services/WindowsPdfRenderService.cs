using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;
using ExplorerAlternative.Services.Abstractions;
using Windows.Data.Pdf;
using Windows.Storage.Streams;

namespace ExplorerAlternative.Services;

/// <summary>
/// Windows標準のPDF描画（<c>Windows.Data.Pdf</c>）による、PDFのページ描画（仕様書13章「PDF」）。
/// Windows 10以降に標準で入っているため、追加のライブラリは要らない。
/// </summary>
public sealed class WindowsPdfRenderService : IPdfRenderService
{
    public async Task<PdfOpenResult> OpenAsync(string path, CancellationToken cancellationToken)
    {
        FileStream? fileStream = null;

        try
        {
            // 他のアプリがファイルを削除・書き換えられるよう、共有を許可して開く
            // （プレビューを開いている間、ファイルをロックしてしまわないように）。
            fileStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var randomAccessStream = fileStream.AsRandomAccessStream();

            var document = await PdfDocument.LoadFromStreamAsync(randomAccessStream).AsTask(cancellationToken);

            if (document.IsPasswordProtected)
            {
                fileStream.Dispose();
                return PdfOpenResult.Failure("パスワードで保護されたPDFのため、表示できません。");
            }

            if (document.PageCount == 0)
            {
                fileStream.Dispose();
                return PdfOpenResult.Failure("ページがないPDFです。");
            }

            return PdfOpenResult.Success(new WindowsPdfDocument(document, fileStream));
        }
        catch (OperationCanceledException)
        {
            fileStream?.Dispose();
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or COMException or ArgumentException or InvalidOperationException or NotSupportedException)
        {
            fileStream?.Dispose();
            return PdfOpenResult.Failure(DescribeOpenFailure(ex));
        }
    }

    // 利用者に見せる理由。開けない原因は、壊れている・形式が違う・読み取りできない、が大半で、
    // COMExceptionのメッセージは英語や数値で分かりにくいため、原因の目安を日本語で添える。
    private static string DescribeOpenFailure(Exception ex) => ex switch
    {
        UnauthorizedAccessException => "PDFを読み取る権限がありません。",
        IOException => $"PDFを読み込めませんでした。({ex.Message})",
        _ => "PDFを開けませんでした。壊れている、または対応していない形式の可能性があります。"
    };

    private sealed class WindowsPdfDocument : IPdfDocument
    {
        private readonly PdfDocument _document;
        private readonly FileStream _fileStream;
        private bool _disposed;

        public WindowsPdfDocument(PdfDocument document, FileStream fileStream)
        {
            _document = document;
            _fileStream = fileStream;
        }

        public int PageCount => (int)_document.PageCount;

        public (double Width, double Height) GetPageSize(int pageIndex)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (pageIndex < 0 || pageIndex >= PageCount)
            {
                throw new ArgumentOutOfRangeException(nameof(pageIndex), $"ページ番号が範囲外です。(0〜{PageCount - 1})");
            }

            using var page = _document.GetPage((uint)pageIndex);
            return (page.Size.Width, page.Size.Height);
        }

        public async Task<BitmapSource> RenderPageAsync(int pageIndex, int widthPixels, CancellationToken cancellationToken)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (pageIndex < 0 || pageIndex >= PageCount)
            {
                throw new ArgumentOutOfRangeException(nameof(pageIndex), $"ページ番号が範囲外です。(0〜{PageCount - 1})");
            }

            using var page = _document.GetPage((uint)pageIndex);
            using var output = new InMemoryRandomAccessStream();

            var options = new PdfPageRenderOptions { DestinationWidth = (uint)Math.Max(1, widthPixels) };
            await page.RenderToStreamAsync(output, options).AsTask(cancellationToken);

            using var memory = new MemoryStream();
            output.Seek(0);
            await output.AsStreamForRead().CopyToAsync(memory, cancellationToken);
            memory.Position = 0;

            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = memory;
            image.EndInit();
            image.Freeze();
            return image;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _fileStream.Dispose();
        }
    }
}
