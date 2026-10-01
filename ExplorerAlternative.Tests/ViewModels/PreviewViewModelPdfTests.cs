using System.Windows.Media;
using System.Windows.Media.Imaging;
using ExplorerAlternative.Services.Abstractions;
using ExplorerAlternative.Tests.TestDoubles;
using ExplorerAlternative.ViewModels;

namespace ExplorerAlternative.Tests.ViewModels;

// 仕様書13章「PDF」：プレビューのページ表示・ページ移動・失敗時のフォールバック・閉じるときの後始末。
// PDFの描画そのものは偽物にして、ViewModelの動き（順序・競合・後始末）を確かめる。
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

    // ===== 読み込み =====

    [Fact]
    public async Task Load_ShowsTheFirstPage()
    {
        _pdf.PageCount = 3;
        var preview = Open();

        await preview.PdfLoadTask;

        Assert.Equal(PreviewKind.Pdf, preview.Kind);
        Assert.Equal(3, preview.PdfPageCount);
        Assert.Equal(1, preview.PdfPageNumber);
        Assert.Equal("1 / 3", preview.PdfPageLabel);
        Assert.NotNull(preview.PdfPageImage);
        Assert.Equal(string.Empty, preview.PdfStatusMessage);
        Assert.Equal(new[] { 0 }, _pdf.Document!.RenderedPages);
    }

    [Fact]
    public void BeforeTheLoadFinishes_ShowsALoadingMessage_AndNoImage()
    {
        _pdf.HoldOpen = new TaskCompletionSource();
        var preview = Open();

        Assert.Contains("読み込み中", preview.PdfStatusMessage);
        Assert.Null(preview.PdfPageImage);
        Assert.False(preview.HasPdfPages);
    }

    [Fact]
    public async Task OpenFailure_FallsBackToTheReason_WithoutAnImage()
    {
        _pdf.FailureReason = "パスワードで保護されたPDFのため、表示できません。";
        var preview = Open();

        await preview.PdfLoadTask;

        Assert.Equal("パスワードで保護されたPDFのため、表示できません。", preview.PdfStatusMessage);
        Assert.Null(preview.PdfPageImage);
        Assert.False(preview.HasPdfPages);
        // ファイル情報は、そのまま表示される。
        Assert.Contains("サイズ", preview.TextContent);
    }

    [Fact]
    public async Task UnexpectedOpenException_DoesNotCrash_AndShowsAMessage()
    {
        _pdf.OpenException = new InvalidOperationException("boom");
        var preview = Open();

        await preview.PdfLoadTask;

        Assert.Contains("boom", preview.PdfStatusMessage);
        Assert.Null(preview.PdfPageImage);
    }

    [Fact]
    public async Task FirstPageRenderFailure_ShowsAMessage_AndKeepsTheDocumentForRetry()
    {
        _pdf.PageCount = 2;
        _pdf.RenderException = new InvalidOperationException("render failed");
        var preview = Open();

        await preview.PdfLoadTask;

        Assert.Contains("1ページ目を表示できませんでした", preview.PdfStatusMessage);
        Assert.Null(preview.PdfPageImage);
        Assert.Equal(2, preview.PdfPageCount);
    }

    // ===== ページ移動 =====

    [Fact]
    public async Task NextAndPrevious_MoveOnePageAtATime()
    {
        _pdf.PageCount = 3;
        var preview = Open();
        await preview.PdfLoadTask;

        await preview.ShowPdfPageAsync(preview.PdfPageNumber + 1);
        Assert.Equal(2, preview.PdfPageNumber);

        await preview.ShowPdfPageAsync(preview.PdfPageNumber + 1);
        Assert.Equal("3 / 3", preview.PdfPageLabel);

        await preview.ShowPdfPageAsync(preview.PdfPageNumber - 1);
        Assert.Equal(2, preview.PdfPageNumber);
        Assert.Equal(new[] { 0, 1, 2, 1 }, _pdf.Document!.RenderedPages);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    [InlineData(4, 3)]
    [InlineData(99, 3)]
    public async Task ShowPage_OutOfRange_IsClamped(int requested, int expected)
    {
        _pdf.PageCount = 3;
        var preview = Open();
        await preview.PdfLoadTask;

        await preview.ShowPdfPageAsync(requested);

        Assert.Equal(expected, preview.PdfPageNumber);
        Assert.Equal(expected - 1, _pdf.Document!.RenderedPages.Last());
    }

    [Fact]
    public async Task Commands_AreDisabledAtTheEnds()
    {
        _pdf.PageCount = 2;
        var preview = Open();
        await preview.PdfLoadTask;

        Assert.False(preview.PreviousPageCommand.CanExecute(null));
        Assert.True(preview.NextPageCommand.CanExecute(null));

        await preview.ShowPdfPageAsync(2);

        Assert.True(preview.PreviousPageCommand.CanExecute(null));
        Assert.False(preview.NextPageCommand.CanExecute(null));
    }

    [Fact]
    public async Task SinglePage_HasNoNavigation()
    {
        _pdf.PageCount = 1;
        var preview = Open();
        await preview.PdfLoadTask;

        Assert.False(preview.PreviousPageCommand.CanExecute(null));
        Assert.False(preview.NextPageCommand.CanExecute(null));
        Assert.Equal("1 / 1", preview.PdfPageLabel);
    }

    [Fact]
    public async Task ShowPage_BeforeTheDocumentIsOpen_DoesNothing()
    {
        _pdf.HoldOpen = new TaskCompletionSource();
        var preview = Open();

        await preview.ShowPdfPageAsync(2);

        Assert.Equal(0, preview.PdfPageNumber);
        Assert.Null(preview.PdfPageImage);
    }

    // ===== 連打（古い描画結果の破棄） =====

    [Fact]
    public async Task StaleRender_IsDiscarded_WhenANewerPageWasRequested()
    {
        _pdf.PageCount = 3;
        var preview = Open();
        await preview.PdfLoadTask;
        var firstImage = preview.PdfPageImage;

        // ページ2の描画を遅らせ、そのあいだにページ3を要求する。
        var gate2 = _pdf.Document!.Hold(1);
        var slow = preview.ShowPdfPageAsync(2);
        await preview.ShowPdfPageAsync(3);
        var imageOfPage3 = preview.PdfPageImage;
        Assert.NotSame(firstImage, imageOfPage3);

        gate2.SetResult();
        await slow;

        // 遅れて終わったページ2の画像で、ページ3の表示を上書きしない。
        Assert.Same(imageOfPage3, preview.PdfPageImage);
        Assert.Equal(3, preview.PdfPageNumber);
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

        // 閉じたあとに開けたPDFは、すぐ閉じる（ファイルを開いたままにしない）。
        Assert.True(_pdf.Document!.IsDisposed);
        Assert.Null(preview.PdfPageImage);
        Assert.Equal(0, preview.PdfPageCount);
    }

    [Fact]
    public async Task ClosedWhileRendering_TheLateImageIsNotShown()
    {
        _pdf.PageCount = 2;
        var preview = Open();
        await preview.PdfLoadTask;
        var before = preview.PdfPageImage;

        var gate = _pdf.Document!.Hold(1);
        var pending = preview.ShowPdfPageAsync(2);
        preview.CancelPendingWork();
        gate.SetResult();
        await pending;

        Assert.Same(before, preview.PdfPageImage);
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

        public Exception? RenderException { get; set; }

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

            Document = new FakePdfDocument(PageCount, RenderException);

            if (HoldOpen is not null)
            {
                await HoldOpen.Task;
            }

            return PdfOpenResult.Success(Document);
        }
    }

    private sealed class FakePdfDocument : IPdfDocument
    {
        private readonly Exception? _renderException;
        private readonly Dictionary<int, TaskCompletionSource> _gates = new();

        public FakePdfDocument(int pageCount, Exception? renderException)
        {
            PageCount = pageCount;
            _renderException = renderException;
        }

        public int PageCount { get; }

        public List<int> RenderedPages { get; } = new();

        public int DisposeCount { get; private set; }

        public bool IsDisposed => DisposeCount > 0;

        /// <summary>指定のページ（0始まり）の描画を、返された入れ物が完了するまで遅らせる。</summary>
        public TaskCompletionSource Hold(int pageIndex)
        {
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _gates[pageIndex] = gate;
            return gate;
        }

        public async Task<BitmapSource> RenderPageAsync(int pageIndex, int widthPixels, CancellationToken cancellationToken)
        {
            RenderedPages.Add(pageIndex);

            if (_renderException is not null)
            {
                throw _renderException;
            }

            if (_gates.Remove(pageIndex, out var gate))
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
