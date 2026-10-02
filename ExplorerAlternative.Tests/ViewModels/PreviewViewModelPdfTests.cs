using System.Windows.Media;
using System.Windows.Media.Imaging;
using ExplorerAlternative.Services.Abstractions;
using ExplorerAlternative.Tests.TestDoubles;
using ExplorerAlternative.ViewModels;

namespace ExplorerAlternative.Tests.ViewModels;

// 仕様書13章「PDF」：全ページを縦に並べてスクロールで見る。ページの場所（大きさ）は先に決まり、画像は、
// 見えている付近のページだけ描く。PDFの描画そのものは偽物にして、ViewModelの動き（順序・競合・後始末）を確かめる。
public sealed class PreviewViewModelPdfTests : IDisposable
{
    private readonly PaneTestHost _host = new();
    private readonly FakePdfRenderService _pdf = new();

    public void Dispose() => _host.Dispose();

    private PreviewViewModel Open(string name = "doc.pdf")
    {
        _host.CreateFile(name);
        _host.Pane.RefreshCommand.Execute(null);
        var node = _host.Pane.VisibleNodes.Single(n => n.Name == name);

        return PreviewViewModel.Create(
            node,
            _host.FileSystem,
            _host.VersionControl,
            _host.Settings,
            StubProxy.Create<IFolderScanService>(),
            _pdf);
    }

    private static int[] Rendered(PreviewViewModel preview) =>
        preview.PdfPages.Where(p => p.Image is not null).Select(p => p.PageNumber).ToArray();

    // ===== 読み込み =====

    [Fact]
    public async Task Load_ListsEveryPage_AndDrawsOnlyTheFirstOnesNearTheTop()
    {
        _pdf.PageCount = 20;
        var preview = Open();

        await preview.PdfLoadTask;

        Assert.Equal(PreviewKind.Pdf, preview.Kind);
        Assert.Equal(20, preview.PdfPages.Count);
        Assert.Equal(20, preview.PdfPageCount);
        Assert.Equal("1 / 20", preview.PdfPageLabel);
        Assert.True(preview.HasPdfPages);
        Assert.Equal(string.Empty, preview.PdfStatusMessage);
        // 1ページ目と、先読みの2ページ目だけを描く。
        Assert.Equal(new[] { 1, 2 }, Rendered(preview));
    }

    [Fact]
    public async Task Pages_KeepTheirAspectRatio_BeforeAnyImageIsDrawn()
    {
        _pdf.PageCount = 3;
        _pdf.PageSizes[1] = (200, 100); // 横長のページ
        var preview = Open();

        await preview.PdfLoadTask;

        Assert.Equal(1000, preview.PdfPages[1].LayoutWidth);
        Assert.Equal(500, preview.PdfPages[1].LayoutHeight, precision: 3);
        Assert.Equal(1333.333, preview.PdfPages[0].LayoutHeight, precision: 2); // 300x400（既定）
        Assert.Equal(new[] { 1, 2, 3 }, preview.PdfPages.Select(p => p.PageNumber));
    }

    [Fact]
    public async Task PageWhoseSizeCannotBeRead_GetsADefaultShape_AndOthersAreUnaffected()
    {
        _pdf.PageCount = 3;
        _pdf.SizeFailures.Add(1);
        var preview = Open();

        await preview.PdfLoadTask;

        Assert.Equal(3, preview.PdfPages.Count);
        Assert.InRange(preview.PdfPages[1].LayoutHeight, 1000, 2000);
    }

    [Fact]
    public void BeforeTheLoadFinishes_ShowsALoadingMessage_AndNoPages()
    {
        _pdf.HoldOpen = new TaskCompletionSource();
        var preview = Open();

        Assert.Contains("読み込み中", preview.PdfStatusMessage);
        Assert.Empty(preview.PdfPages);
        Assert.False(preview.HasPdfPages);
    }

    [Fact]
    public async Task OpenFailure_FallsBackToTheReason_WithoutPages()
    {
        _pdf.FailureReason = "パスワードで保護されたPDFのため、表示できません。";
        var preview = Open();

        await preview.PdfLoadTask;

        Assert.Equal("パスワードで保護されたPDFのため、表示できません。", preview.PdfStatusMessage);
        Assert.Empty(preview.PdfPages);
        Assert.False(preview.HasPdfPages);
        Assert.Contains("サイズ", preview.TextContent);
    }

    [Fact]
    public async Task UnexpectedOpenException_DoesNotCrash_AndShowsAMessage()
    {
        _pdf.OpenException = new InvalidOperationException("boom");
        var preview = Open();

        await preview.PdfLoadTask;

        Assert.Contains("boom", preview.PdfStatusMessage);
        Assert.Empty(preview.PdfPages);
    }

    // ===== スクロール：見える範囲に合わせて描く =====

    [Fact]
    public async Task Scrolling_DrawsTheVisiblePages_AndAFewAround()
    {
        _pdf.PageCount = 30;
        var preview = Open();
        await preview.PdfLoadTask;

        await preview.UpdatePdfVisibleRangeAsync(9, 10); // 10・11ページ目が見えている

        var rendered = Rendered(preview);
        Assert.Contains(10, rendered);
        Assert.Contains(11, rendered);
        Assert.Contains(9, rendered);  // 前の先読み
        Assert.Contains(12, rendered); // 後ろの先読み
    }

    [Fact]
    public async Task VisiblePagesAreDrawnBeforeThePrefetchedOnes()
    {
        _pdf.PageCount = 30;
        var preview = Open();
        await preview.PdfLoadTask;
        _pdf.Document!.RenderedPages.Clear();

        await preview.UpdatePdfVisibleRangeAsync(14, 15);

        var firstTwo = _pdf.Document.RenderedPages.Take(2).OrderBy(i => i).ToArray();
        Assert.Equal(new[] { 14, 15 }, firstTwo); // 0始まりの14・15 = 見えているページ
    }

    [Fact]
    public async Task PagesThatAreFarAway_LoseTheirImage_SoMemoryStaysBounded()
    {
        _pdf.PageCount = 60;
        var preview = Open();
        await preview.PdfLoadTask;
        Assert.Contains(1, Rendered(preview));

        await preview.UpdatePdfVisibleRangeAsync(50, 50);

        Assert.DoesNotContain(1, Rendered(preview));
        Assert.Contains(51, Rendered(preview));
        // 描いているページ数は、見えている範囲の前後に限られる。
        Assert.True(Rendered(preview).Length <= 1 + (2 * (PreviewViewModel.PdfKeepPages + 1)));
    }

    [Fact]
    public async Task APageThatIsAlreadyDrawn_IsNotDrawnAgain()
    {
        _pdf.PageCount = 5;
        var preview = Open();
        await preview.PdfLoadTask;
        _pdf.Document!.RenderedPages.Clear();

        await preview.UpdatePdfVisibleRangeAsync(0, 0);
        await preview.UpdatePdfVisibleRangeAsync(0, 0);

        Assert.Empty(_pdf.Document.RenderedPages);
    }

    [Fact]
    public async Task ScrollingBackToAPageThatWasDropped_DrawsItAgain()
    {
        _pdf.PageCount = 60;
        var preview = Open();
        await preview.PdfLoadTask;
        await preview.UpdatePdfVisibleRangeAsync(50, 50);
        Assert.DoesNotContain(1, Rendered(preview));

        await preview.UpdatePdfVisibleRangeAsync(0, 0);

        Assert.Contains(1, Rendered(preview));
    }

    [Theory]
    [InlineData(-5, -3)]
    [InlineData(500, 600)]
    [InlineData(3, 1)]
    public async Task OutOfRangeOrReversedRanges_AreClamped_NotACrash(int first, int last)
    {
        _pdf.PageCount = 5;
        var preview = Open();
        await preview.PdfLoadTask;

        await preview.UpdatePdfVisibleRangeAsync(first, last);

        Assert.NotEmpty(Rendered(preview));
    }

    [Fact]
    public async Task UpdateRange_BeforeTheDocumentIsOpen_DoesNothing()
    {
        _pdf.HoldOpen = new TaskCompletionSource();
        var preview = Open();

        await preview.UpdatePdfVisibleRangeAsync(0, 3);

        Assert.Empty(preview.PdfPages);
    }

    // ===== 連続したスクロール（古い要求の破棄） =====

    [Fact]
    public async Task ANewerRange_SupersedesTheOlderOne_WhichStopsDrawing()
    {
        _pdf.PageCount = 100;
        var preview = Open();
        await preview.PdfLoadTask;

        // 40ページ目付近の描画を止めておき、その間に、80ページ目付近までスクロールする。
        var gate = _pdf.Document!.Hold(39);
        var slow = preview.UpdatePdfVisibleRangeAsync(39, 39);
        await Task.Delay(50);
        var fast = preview.UpdatePdfVisibleRangeAsync(79, 79);
        gate.SetResult();
        await Task.WhenAll(slow, fast);

        Assert.Contains(80, Rendered(preview));
        // 古い範囲（40ページ目付近）は、遠くなったので、画像を持たない。
        Assert.DoesNotContain(40, Rendered(preview));
    }

    // ===== 現在のページ =====

    [Fact]
    public async Task SetCurrentPage_UpdatesTheLabel_AndClamps()
    {
        _pdf.PageCount = 10;
        var preview = Open();
        await preview.PdfLoadTask;

        preview.SetCurrentPdfPage(7);
        Assert.Equal("7 / 10", preview.PdfPageLabel);

        preview.SetCurrentPdfPage(99);
        Assert.Equal(10, preview.PdfPageNumber);

        preview.SetCurrentPdfPage(0);
        Assert.Equal(1, preview.PdfPageNumber);
    }

    [Fact]
    public void SetCurrentPage_WithoutPages_DoesNothing()
    {
        _pdf.HoldOpen = new TaskCompletionSource();
        var preview = Open();

        preview.SetCurrentPdfPage(3);

        Assert.Equal(0, preview.PdfPageNumber);
        Assert.Equal(string.Empty, preview.PdfPageLabel);
    }

    // ===== 描けなかったページ =====

    [Fact]
    public async Task APageThatFailsToDraw_ShowsItsOwnMessage_AndOthersStillDraw()
    {
        _pdf.PageCount = 3;
        _pdf.RenderFailures.Add(0);
        var preview = Open();

        await preview.PdfLoadTask;

        Assert.Contains("表示できませんでした", preview.PdfPages[0].FailureMessage);
        Assert.Null(preview.PdfPages[0].Image);
        Assert.NotNull(preview.PdfPages[1].Image);
    }

    [Fact]
    public async Task APageThatFailed_IsNotRetriedOnEveryScroll()
    {
        _pdf.PageCount = 3;
        _pdf.RenderFailures.Add(0);
        var preview = Open();
        await preview.PdfLoadTask;
        var attempts = _pdf.Document!.RenderedPages.Count(i => i == 0);

        await preview.UpdatePdfVisibleRangeAsync(0, 0);
        await preview.UpdatePdfVisibleRangeAsync(0, 0);

        Assert.Equal(attempts, _pdf.Document.RenderedPages.Count(i => i == 0));
    }

    // ===== 閉じる・切り替え =====

    [Fact]
    public async Task CancelPendingWork_ClosesTheOpenDocument()
    {
        var preview = Open();
        await preview.PdfLoadTask;
        Assert.False(_pdf.Document!.IsDisposed);

        preview.CancelPendingWork();

        Assert.True(_pdf.Document.IsDisposed);
    }

    [Fact]
    public async Task CancelPendingWork_CalledTwice_DisposesOnce()
    {
        var preview = Open();
        await preview.PdfLoadTask;

        preview.CancelPendingWork();
        preview.CancelPendingWork();

        Assert.Equal(1, _pdf.Document!.DisposeCount);
    }

    [Fact]
    public async Task ClosedWhileOpening_TheLateDocumentIsDisposed_AndNothingIsShown()
    {
        _pdf.PageCount = 2;
        _pdf.HoldOpen = new TaskCompletionSource();
        var preview = Open();

        preview.CancelPendingWork();
        _pdf.HoldOpen.SetResult();
        await preview.PdfLoadTask;

        Assert.True(_pdf.Document!.IsDisposed);
        Assert.Empty(preview.PdfPages);
        Assert.Equal(0, preview.PdfPageCount);
    }

    [Fact]
    public async Task ClosedWhileDrawing_TheLateImageIsNotShown_AndNothingThrows()
    {
        _pdf.PageCount = 40;
        var preview = Open();
        await preview.PdfLoadTask;

        var gate = _pdf.Document!.Hold(29);
        var pending = preview.UpdatePdfVisibleRangeAsync(29, 29);
        await Task.Delay(50);
        preview.CancelPendingWork();
        gate.SetResult();
        await pending;

        Assert.Null(preview.PdfPages[29].Image);
    }

    [Fact]
    public void NonPdfFiles_DoNotTouchThePdfService()
    {
        var preview = Open("note.txt");

        Assert.NotEqual(PreviewKind.Pdf, preview.Kind);
        Assert.Equal(0, _pdf.OpenCount);
    }

    // ===== 偽物 =====

    private sealed class FakePdfRenderService : IPdfRenderService
    {
        public int PageCount { get; set; } = 1;

        public string? FailureReason { get; set; }

        public Exception? OpenException { get; set; }

        /// <summary>ページごとの大きさ（0始まり）。指定のないページは300x400。</summary>
        public Dictionary<int, (double Width, double Height)> PageSizes { get; } = new();

        /// <summary>大きさを取れない（例外になる）ページ（0始まり）。</summary>
        public HashSet<int> SizeFailures { get; } = new();

        /// <summary>描けない（例外になる）ページ（0始まり）。</summary>
        public HashSet<int> RenderFailures { get; } = new();

        /// <summary>設定すると、これが完了するまで、開く処理が終わらない。</summary>
        public TaskCompletionSource? HoldOpen { get; set; }

        public int OpenCount { get; private set; }

        public FakePdfDocument? Document { get; private set; }

        public async Task<PdfOpenResult> OpenAsync(string path, CancellationToken cancellationToken)
        {
            OpenCount++;

            if (OpenException is not null)
            {
                throw OpenException;
            }

            if (FailureReason is not null)
            {
                return PdfOpenResult.Failure(FailureReason);
            }

            Document = new FakePdfDocument(PageCount, PageSizes, SizeFailures, RenderFailures);

            if (HoldOpen is not null)
            {
                await HoldOpen.Task;
            }

            return PdfOpenResult.Success(Document);
        }
    }

    private sealed class FakePdfDocument : IPdfDocument
    {
        private readonly Dictionary<int, (double Width, double Height)> _sizes;
        private readonly HashSet<int> _sizeFailures;
        private readonly HashSet<int> _renderFailures;
        private readonly Dictionary<int, TaskCompletionSource> _gates = new();
        private readonly object _lock = new();

        public FakePdfDocument(
            int pageCount,
            Dictionary<int, (double Width, double Height)> sizes,
            HashSet<int> sizeFailures,
            HashSet<int> renderFailures)
        {
            PageCount = pageCount;
            _sizes = sizes;
            _sizeFailures = sizeFailures;
            _renderFailures = renderFailures;
        }

        public int PageCount { get; }

        public List<int> RenderedPages { get; } = new();

        public int DisposeCount { get; private set; }

        public bool IsDisposed => DisposeCount > 0;

        public (double Width, double Height) GetPageSize(int pageIndex)
        {
            if (_sizeFailures.Contains(pageIndex))
            {
                throw new InvalidOperationException("size unavailable");
            }

            return _sizes.TryGetValue(pageIndex, out var size) ? size : (300, 400);
        }

        /// <summary>指定のページ（0始まり）の描画を、返された入れ物が完了するまで遅らせる。</summary>
        public TaskCompletionSource Hold(int pageIndex)
        {
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_lock)
            {
                _gates[pageIndex] = gate;
            }

            return gate;
        }

        public async Task<BitmapSource> RenderPageAsync(int pageIndex, int widthPixels, CancellationToken cancellationToken)
        {
            ObjectDisposedException.ThrowIf(IsDisposed, this);

            lock (_lock)
            {
                RenderedPages.Add(pageIndex);
            }

            if (_renderFailures.Contains(pageIndex))
            {
                throw new InvalidOperationException("render failed");
            }

            TaskCompletionSource? gate;
            lock (_lock)
            {
                _gates.Remove(pageIndex, out gate);
            }

            if (gate is not null)
            {
                await gate.Task;
            }

            // ページごとに別の画像（参照が違う）を返す。
            var image = BitmapSource.Create(2, 2, 96, 96, PixelFormats.Bgra32, null, new byte[16], 8);
            image.Freeze();
            return image;
        }

        public void Dispose() => DisposeCount++;
    }
}
