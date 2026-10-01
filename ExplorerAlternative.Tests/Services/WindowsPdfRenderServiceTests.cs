using System.Windows.Media;
using System.Windows.Media.Imaging;
using ExplorerAlternative.Services;
using ExplorerAlternative.Tests.TestDoubles;

namespace ExplorerAlternative.Tests.Services;

// 仕様書13章「PDF」：Windows標準のPDF描画による、ページの描画。実際のPDF（テスト用に生成）を描画して確認する。
public sealed class WindowsPdfRenderServiceTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("eat_pdf_").FullName;
    private readonly WindowsPdfRenderService _sut = new();

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private static (int R, int G, int B) CenterPixel(BitmapSource image)
    {
        var converted = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
        var pixel = new byte[4];
        // 文字の描かれない、ページの左上寄りの点を取る（背景の色）。
        converted.CopyPixels(new System.Windows.Int32Rect(10, 10, 1, 1), pixel, 4, 0);
        return (pixel[2], pixel[1], pixel[0]);
    }

    // ===== 開く =====

    [Fact]
    public async Task Open_ReadsThePageCount()
    {
        var path = PdfTestFile.Write(_root, "three.pdf", 3);

        var result = await _sut.OpenAsync(path, CancellationToken.None);

        Assert.NotNull(result.Document);
        Assert.Null(result.FailureReason);
        using var document = result.Document!;
        Assert.Equal(3, document.PageCount);
    }

    [Fact]
    public async Task Open_SinglePage()
    {
        var path = PdfTestFile.Write(_root, "one.pdf", 1);

        var result = await _sut.OpenAsync(path, CancellationToken.None);

        using var document = result.Document!;
        Assert.Equal(1, document.PageCount);
    }

    [Fact]
    public async Task Open_NonexistentFile_ReturnsAFailure_NotAnException()
    {
        var result = await _sut.OpenAsync(Path.Combine(_root, "missing.pdf"), CancellationToken.None);

        Assert.Null(result.Document);
        Assert.False(string.IsNullOrWhiteSpace(result.FailureReason));
    }

    [Fact]
    public async Task Open_FileThatIsNotAPdf_ReturnsAFailureWithAReason()
    {
        var path = Path.Combine(_root, "fake.pdf");
        File.WriteAllText(path, "これはPDFではありません。");

        var result = await _sut.OpenAsync(path, CancellationToken.None);

        Assert.Null(result.Document);
        Assert.Contains("PDF", result.FailureReason);
    }

    [Fact]
    public async Task Open_EmptyFile_ReturnsAFailure()
    {
        var path = Path.Combine(_root, "empty.pdf");
        File.WriteAllBytes(path, Array.Empty<byte>());

        var result = await _sut.OpenAsync(path, CancellationToken.None);

        Assert.Null(result.Document);
        Assert.False(string.IsNullOrWhiteSpace(result.FailureReason));
    }

    [Fact]
    public async Task Open_TruncatedPdf_DoesNotThrow()
    {
        var bytes = PdfTestFile.Build(2);
        var path = Path.Combine(_root, "truncated.pdf");
        File.WriteAllBytes(path, bytes[..(bytes.Length / 2)]);

        var result = await _sut.OpenAsync(path, CancellationToken.None);

        // 壊れたPDFでも、例外にならない（開けた場合は、閉じる）。
        result.Document?.Dispose();
        Assert.True(result.Document is not null || result.FailureReason is not null);
    }

    [Fact]
    public async Task Open_Cancelled_Throws()
    {
        var path = PdfTestFile.Write(_root, "a.pdf", 1);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _sut.OpenAsync(path, cts.Token));
    }

    // ===== 描画 =====

    [Fact]
    public async Task Render_GivesAFrozenImageOfTheRequestedWidth_KeepingTheAspectRatio()
    {
        var path = PdfTestFile.Write(_root, "r.pdf", 1);
        using var document = (await _sut.OpenAsync(path, CancellationToken.None)).Document!;

        var image = await document.RenderPageAsync(0, 600, CancellationToken.None);

        Assert.True(image.IsFrozen); // 別のスレッド（画面のスレッド）で使えるように

        // Windows標準のPDF描画は、画面の拡大率（DPI。125%なら1.25倍）を掛けた大きさで出力する。
        // そのため、幅は「指定以上」（拡大率100%なら、ちょうど600）で、機械によって変わる。画面側は、
        // 表示領域に合わせて縮小して表示するので、問題ない。確実に言えるのは、縦横比が保たれること。
        Assert.InRange(image.PixelWidth, 600, 1200);
        // 300x400ポイントのページ（縦横比 3:4）。
        Assert.Equal(image.PixelWidth * 4.0 / 3.0, image.PixelHeight, precision: 0);
    }

    [Fact]
    public async Task Render_DrawsThePageContent_NotABlankImage()
    {
        var path = PdfTestFile.Write(_root, "content.pdf", 1);
        using var document = (await _sut.OpenAsync(path, CancellationToken.None)).Document!;

        var image = await document.RenderPageAsync(0, 600, CancellationToken.None);

        // 背景は、ページ指定の色（赤1.0・緑0.8・青0.8）で塗られている。真っ白ではない。
        var (r, g, b) = CenterPixel(image);
        Assert.InRange(r, 240, 255);
        Assert.InRange(g, 190, 215);
        Assert.InRange(b, 190, 215);
    }

    [Fact]
    public async Task Render_DifferentPages_AreDifferentImages()
    {
        var path = PdfTestFile.Write(_root, "pages.pdf", 3);
        using var document = (await _sut.OpenAsync(path, CancellationToken.None)).Document!;

        var first = CenterPixel(await document.RenderPageAsync(0, 300, CancellationToken.None));
        var last = CenterPixel(await document.RenderPageAsync(2, 300, CancellationToken.None));

        // ページごとに、背景の赤の成分を変えてあるので、1ページ目と3ページ目は、色が違う。
        Assert.NotEqual(first.R, last.R);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    [InlineData(100)]
    public async Task Render_PageOutOfRange_Throws(int pageIndex)
    {
        var path = PdfTestFile.Write(_root, "range.pdf", 3);
        using var document = (await _sut.OpenAsync(path, CancellationToken.None)).Document!;

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => document.RenderPageAsync(pageIndex, 300, CancellationToken.None));
    }

    [Fact]
    public async Task Render_AfterDispose_Throws()
    {
        var path = PdfTestFile.Write(_root, "disposed.pdf", 1);
        var document = (await _sut.OpenAsync(path, CancellationToken.None)).Document!;
        document.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => document.RenderPageAsync(0, 300, CancellationToken.None));
    }

    [Fact]
    public async Task Dispose_CanBeCalledTwice()
    {
        var path = PdfTestFile.Write(_root, "twice.pdf", 1);
        var document = (await _sut.OpenAsync(path, CancellationToken.None)).Document!;

        document.Dispose();
        document.Dispose();
    }

    // ===== ファイルを開いたままにしない =====

    // プレビューで開いている間も、他のアプリ（このアプリ自身の削除・名前変更を含む）がファイルを
    // 操作できるよう、共有を許可して開く。閉じたあとは、ロックが残らない。
    [Fact]
    public async Task OpenDocument_DoesNotBlockOtherReadersOrWriters()
    {
        var path = PdfTestFile.Write(_root, "shared.pdf", 1);
        using var document = (await _sut.OpenAsync(path, CancellationToken.None)).Document!;

        // 開いている間も、別に読み取れる・書き込める。
        using (var reader = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        {
            Assert.True(reader.Length > 0);
        }

        File.AppendAllText(path, "\n% appended");
    }

    [Fact]
    public async Task AfterDispose_TheFileCanBeDeleted()
    {
        var path = PdfTestFile.Write(_root, "release.pdf", 1);
        var document = (await _sut.OpenAsync(path, CancellationToken.None)).Document!;
        document.Dispose();

        File.Delete(path);

        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task FailedOpen_DoesNotLeaveTheFileLocked()
    {
        var path = Path.Combine(_root, "notpdf.pdf");
        File.WriteAllText(path, "not a pdf");
        await _sut.OpenAsync(path, CancellationToken.None);

        File.Delete(path);

        Assert.False(File.Exists(path));
    }
}
