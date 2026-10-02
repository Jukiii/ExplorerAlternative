using System.ComponentModel;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Documents;
using System.Windows.Media.Imaging;
using ExplorerAlternative.Models;
using ExplorerAlternative.Mvvm;
using ExplorerAlternative.Rendering;
using ExplorerAlternative.Services;
using ExplorerAlternative.Services.Abstractions;

namespace ExplorerAlternative.ViewModels;

/// <summary>
/// Quick Lookに近いプレビュー（仕様書11章・13章・14章・15章）。Spaceキーで選択中のファイル・
/// フォルダを表示する。固定（15章）・前後移動・Markdownソース表示切り替えのため、
/// 表示中に変化しうる状態のみObservableObjectで公開する。
/// </summary>
public sealed class PreviewViewModel : ObservableObject
{
    private const int MaxPreviewChars = 200_000;
    private const int RecentFilesCount = 5;

    private static readonly string[] DefaultTextExtensions =
    {
        "txt", "md", "markdown", "json", "xml", "csv", "cs", "xaml", "ps1",
        "py", "js", "ts", "html", "css", "yml", "yaml", "ini", "log"
    };

    private static readonly string[] ImageExtensions =
    {
        "png", "jpg", "jpeg", "gif", "bmp", "tiff", "tif", "webp"
    };

    private bool _isShowingMarkdownSource;
    private bool _isPinned;
    private string _folderSizeDisplay = string.Empty;
    private CancellationTokenSource? _folderSizeCts;
    private CodeSymbol? _selectedSymbol;

    // 仕様書13章「PDF」：全ページを縦に並べて、スクロールで見る。
    private const int PdfRenderWidth = 1000;

    /// <summary>見えている範囲の前後に、先に描いておくページ数（スクロールしたとき、空白が見えないように）。</summary>
    internal const int PdfPrefetchPages = 1;

    /// <summary>見えている範囲から、これより離れたページの画像は捨てる（メモリを使いすぎないように）。</summary>
    internal const int PdfKeepPages = 6;

    private CancellationTokenSource? _pdfCts;
    private IPdfDocument? _pdfDocument;
    private readonly SemaphoreSlim _pdfRenderGate = new(1, 1);
    private int _pdfRangeGeneration;
    private int _pdfVisibleFirst;
    private int _pdfVisibleLast;
    private int _pdfPageNumber;
    private int _pdfPageCount;
    private string _pdfStatusMessage = string.Empty;

    private PreviewViewModel(
        string title, string fullPath, PreviewKind kind, string textContent, FlowDocument? markdownDocument,
        string folderSummary, bool truncated, BitmapImage? imageSource,
        string folderModified, string folderVcsSummary, string folderTagsSummary,
        IReadOnlyList<string> recentFiles,
        IReadOnlyList<CodeSymbol>? symbols = null)
    {
        Title = title;
        FullPath = fullPath;
        Kind = kind;
        TextContent = textContent;
        MarkdownDocument = markdownDocument;
        FolderSummary = folderSummary;
        Truncated = truncated;
        ImageSource = imageSource;
        FolderModified = folderModified;
        FolderVcsSummary = folderVcsSummary;
        FolderTagsSummary = folderTagsSummary;
        RecentFiles = recentFiles;
        Symbols = symbols ?? Array.Empty<CodeSymbol>();

        ToggleMarkdownSourceCommand = new RelayCommand(_ => IsShowingMarkdownSource = !IsShowingMarkdownSource);
        TogglePinCommand = new RelayCommand(_ => IsPinned = !IsPinned);
        PreviousCommand = new RelayCommand(_ => RequestPrevious?.Invoke());
        NextCommand = new RelayCommand(_ => RequestNext?.Invoke());
        OpenExternallyCommand = new RelayCommand(_ => OpenExternally());
    }

    /// <summary>仕様書16章「コードシンボル表示」：対応言語のソースコードのみ非空。</summary>
    public IReadOnlyList<CodeSymbol> Symbols { get; }

    public bool HasSymbols => Symbols.Count > 0;

    /// <summary>シンボル一覧で選択した項目。選択と同時に該当行へジャンプする（RequestJumpToLine）。</summary>
    public CodeSymbol? SelectedSymbol
    {
        get => _selectedSymbol;
        set
        {
            if (SetProperty(ref _selectedSymbol, value) && value is not null)
            {
                RequestJumpToLine?.Invoke(value.Line);
            }
        }
    }

    /// <summary>仕様書16章「シンボルクリックで該当位置へジャンプ」。呼び出し側（View）が実際のスクロール・選択を行う。</summary>
    public Action<int>? RequestJumpToLine { get; set; }

    public string Title { get; }

    /// <summary>仕様書13章：PDFを既定のアプリで開くボタン等に使う対象の完全パス。</summary>
    public string FullPath { get; }

    public PreviewKind Kind { get; }

    public string TextContent { get; }

    public FlowDocument? MarkdownDocument { get; }

    public string FolderSummary { get; }

    public bool Truncated { get; }

    public BitmapImage? ImageSource { get; }

    public string FolderModified { get; }

    public string FolderVcsSummary { get; }

    public string FolderTagsSummary { get; }

    public IReadOnlyList<string> RecentFiles { get; }

    /// <summary>仕様書13章：Markdownの「レンダリング表示 / ソース表示」の切り替え。</summary>
    public bool IsShowingMarkdownSource
    {
        get => _isShowingMarkdownSource;
        set => SetProperty(ref _isShowingMarkdownSource, value);
    }

    public RelayCommand ToggleMarkdownSourceCommand { get; }

    /// <summary>仕様書15章「Quick Look固定」。固定中は別項目を選択してもこの内容を維持する。</summary>
    public bool IsPinned
    {
        get => _isPinned;
        set => SetProperty(ref _isPinned, value);
    }

    public RelayCommand TogglePinCommand { get; }

    public RelayCommand PreviousCommand { get; }

    public RelayCommand NextCommand { get; }

    /// <summary>仕様書13章「← / → 前後」。呼び出し側（MainWindowViewModel）が実際の移動を担う。</summary>
    public Action? RequestPrevious { get; set; }

    public Action? RequestNext { get; set; }

    /// <summary>仕様書13章「Esc 閉じる」。呼び出し側（View）に対して「閉じてほしい」と要求する。</summary>
    public Action? RequestClose { get; set; }

    /// <summary>
    /// ウィンドウが（タイトルバーの×ボタン等、RequestClose経由以外の方法で）実際に閉じられた
    /// ときにViewから呼び出される。MainWindowViewModel側の状態（_currentPreview）をリセットする
    /// ためのものであり、再度ウィンドウを閉じようとはしない（RequestCloseと役割を分けている）。
    /// </summary>
    public Action? Closed { get; set; }

    /// <summary>仕様書14章「フォルダQuick Look」：合計サイズ（非同期集計）。計算中は"計算中..."を表示する。</summary>
    public string FolderSizeDisplay
    {
        get => _folderSizeDisplay;
        private set => SetProperty(ref _folderSizeDisplay, value);
    }

    /// <summary>仕様書13章「PDF」：PDFを既定のアプリで開くボタン（ページを表示できない場合の代わりにも使う）。</summary>
    public RelayCommand OpenExternallyCommand { get; }

    // ===== 仕様書13章「PDF」：全ページをスクロールで見る =====

    /// <summary>全ページ（場所だけ先に決まっていて、画像は、見える範囲の近くだけ描く）。</summary>
    public ObservableCollection<PdfPageItem> PdfPages { get; } = new();

    /// <summary>いま見えている付近のページ番号（1始まり。スクロールに合わせて、画面が更新する）。PDFを読み込めていない間は0。</summary>
    public int PdfPageNumber
    {
        get => _pdfPageNumber;
        private set
        {
            if (SetProperty(ref _pdfPageNumber, value))
            {
                OnPropertyChanged(nameof(PdfPageLabel));
            }
        }
    }

    /// <summary>総ページ数。PDFを読み込めていない間は0。</summary>
    public int PdfPageCount
    {
        get => _pdfPageCount;
        private set
        {
            if (SetProperty(ref _pdfPageCount, value))
            {
                OnPropertyChanged(nameof(HasPdfPages));
                OnPropertyChanged(nameof(PdfPageLabel));
            }
        }
    }

    /// <summary>「3 / 12」のような、ページ位置の表示。</summary>
    public string PdfPageLabel => PdfPageCount > 0 ? $"{PdfPageNumber} / {PdfPageCount}" : string.Empty;

    public bool HasPdfPages => PdfPageCount > 0;

    /// <summary>「読み込み中...」や、読み込めなかった理由（空なら、表示すべきメッセージはない）。</summary>
    public string PdfStatusMessage
    {
        get => _pdfStatusMessage;
        private set => SetProperty(ref _pdfStatusMessage, value);
    }

    /// <summary>スクロールで、見えているページが変わったときに、画面が呼ぶ（範囲外は、最初・最後のページに丸める）。</summary>
    public void SetCurrentPdfPage(int pageNumber)
    {
        if (PdfPageCount == 0)
        {
            return;
        }

        PdfPageNumber = Math.Clamp(pageNumber, 1, PdfPageCount);
    }

    /// <summary>PDFの読み込み（最初のページの表示まで）の完了を待つためのもの（テスト用）。</summary>
    internal Task PdfLoadTask { get; private set; } = Task.CompletedTask;

    private void StartPdfRendering(string path, IPdfRenderService pdfRenderService)
    {
        _pdfCts = new CancellationTokenSource();
        PdfStatusMessage = "PDFを読み込み中...";
        PdfLoadTask = LoadPdfAsync(path, pdfRenderService, _pdfCts.Token);
    }

    // PDFを開き、最初のページを表示する。開けない場合は、理由を表示して、ファイル情報と
    // 「既定のアプリで開く」ボタンにフォールバックする（27章：エラーで落とさない）。
    private async Task LoadPdfAsync(string path, IPdfRenderService pdfRenderService, CancellationToken token)
    {
        try
        {
            var result = await pdfRenderService.OpenAsync(path, token);

            if (token.IsCancellationRequested)
            {
                result.Document?.Dispose();
                return;
            }

            if (result.Document is null)
            {
                PdfStatusMessage = result.FailureReason ?? "PDFを読み込めませんでした。";
                return;
            }

            var document = result.Document;
            _pdfDocument = document;

            // 全ページの場所を、先に決める（画像は、見える範囲の近くだけ描く）。
            for (var i = 0; i < document.PageCount; i++)
            {
                PdfPages.Add(CreatePdfPageItem(document, i));
            }

            PdfPageCount = document.PageCount;
            PdfPageNumber = 1;
            PdfStatusMessage = string.Empty;

            // 最初に見える付近を、すぐ描く（画面からも、スクロール位置に応じて呼ばれる）。
            await UpdatePdfVisibleRangeAsync(0, 0);
        }
        catch (OperationCanceledException)
        {
            // プレビューが切り替わった・閉じられたことによる中断は、エラーではない。
        }
        catch (Exception ex)
        {
            PdfStatusMessage = $"PDFの表示中にエラーが発生しました。({ex.Message})";
        }
    }

    // ページの縦横比から、画面上の大きさを決める。大きさを取れないページは、A4縦のような比にする。
    private static PdfPageItem CreatePdfPageItem(IPdfDocument document, int pageIndex)
    {
        var height = PdfRenderWidth * 1.414;

        try
        {
            var (width, pageHeight) = document.GetPageSize(pageIndex);
            if (width > 0 && pageHeight > 0)
            {
                height = PdfRenderWidth * pageHeight / width;
            }
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or ArgumentException or InvalidOperationException)
        {
            // このページの大きさを取れなくても、他のページは表示する。
        }

        return new PdfPageItem(pageIndex + 1, PdfRenderWidth, height);
    }

    /// <summary>
    /// 見えているページの範囲（0始まり。<paramref name="firstVisible"/>〜<paramref name="lastVisible"/>）が変わったときに、
    /// 画面が呼ぶ。見えているページと、その前後の少しのページの画像を描き（見えているページを先に）、
    /// 遠くなったページの画像は捨てる。呼び出しが重なった場合は、最新の範囲だけを処理する。
    /// </summary>
    internal async Task UpdatePdfVisibleRangeAsync(int firstVisible, int lastVisible)
    {
        var document = _pdfDocument;
        if (document is null || PdfPages.Count == 0)
        {
            return;
        }

        var last = PdfPages.Count - 1;
        firstVisible = Math.Clamp(Math.Min(firstVisible, lastVisible), 0, last);
        lastVisible = Math.Clamp(Math.Max(firstVisible, lastVisible), 0, last);

        var generation = Interlocked.Increment(ref _pdfRangeGeneration);
        var token = _pdfCts?.Token ?? CancellationToken.None;
        _pdfVisibleFirst = firstVisible;
        _pdfVisibleLast = lastVisible;

        // 遠くなったページの画像を捨てる。
        for (var i = 0; i < PdfPages.Count; i++)
        {
            if ((i < firstVisible - PdfKeepPages || i > lastVisible + PdfKeepPages) && PdfPages[i].Image is not null)
            {
                PdfPages[i].Image = null;
            }
        }

        // 見えているページ → 前後の少しのページ、の順に描く。
        var wanted = Enumerable.Range(firstVisible, lastVisible - firstVisible + 1).ToList();
        for (var offset = 1; offset <= PdfPrefetchPages; offset++)
        {
            if (lastVisible + offset <= last)
            {
                wanted.Add(lastVisible + offset);
            }

            if (firstVisible - offset >= 0)
            {
                wanted.Add(firstVisible - offset);
            }
        }

        foreach (var index in wanted)
        {
            if (generation != Volatile.Read(ref _pdfRangeGeneration) || token.IsCancellationRequested)
            {
                return;
            }

            await RenderPdfPageAsync(document, index, generation, token);
        }
    }

    private bool IsWithinPdfKeepRange(int index) =>
        index >= _pdfVisibleFirst - PdfKeepPages && index <= _pdfVisibleLast + PdfKeepPages;

    private async Task RenderPdfPageAsync(IPdfDocument document, int index, int generation, CancellationToken token)
    {
        var page = PdfPages[index];
        if (page.Image is not null || page.FailureMessage is not null)
        {
            return;
        }

        try
        {
            await _pdfRenderGate.WaitAsync(token);

            try
            {
                // 順番を待っている間に、範囲が変わった・閉じられた場合は、描かない。
                if (generation != Volatile.Read(ref _pdfRangeGeneration) || token.IsCancellationRequested || page.Image is not null)
                {
                    return;
                }

                var image = await document.RenderPageAsync(index, PdfRenderWidth, token);

                // 描いている間に、そのページが遠くなっていた（スクロールが進んだ）場合は、捨てる（メモリを使わないため）。
                if (!token.IsCancellationRequested && IsWithinPdfKeepRange(index))
                {
                    page.Image = image;
                }
            }
            finally
            {
                _pdfRenderGate.Release();
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException)
        {
            // プレビューが切り替わった・閉じられた（ドキュメントが閉じられた）ことによる中断は、エラーではない。
        }
        catch (Exception ex)
        {
            page.FailureMessage = $"このページを表示できませんでした。({ex.Message})";
        }
    }

    private void OpenExternally()
    {
        if (string.IsNullOrEmpty(FullPath))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(FullPath) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            // プレビュー表示自体は継続する（27章：エラーで落とさない）。
        }
    }

    /// <summary>プレビューが閉じられる・別項目に切り替わる際に呼び出し、進行中のフォルダサイズ集計を中断する。</summary>
    public void CancelPendingWork()
    {
        _folderSizeCts?.Cancel();

        // PDFの読み込み・描画を中断し、開いているPDFを閉じる（ファイルを開いたままにしない）。
        _pdfCts?.Cancel();
        Interlocked.Increment(ref _pdfRangeGeneration);
        _pdfDocument?.Dispose();
        _pdfDocument = null;
    }

    private static readonly string[] OfficeExtensions = { "docx", "xlsx", "pptx" };

    public static PreviewViewModel Create(
        FileSystemNodeViewModel node,
        IFileSystemService fileSystemService,
        IVersionControlService versionControlService,
        ISettingsService settingsService,
        IFolderScanService folderScanService,
        IPdfRenderService pdfRenderService)
    {
        return node.IsDirectory
            ? CreateForFolder(node, fileSystemService, versionControlService, settingsService, folderScanService)
            : CreateForFile(node, fileSystemService, settingsService.Current.TextFileExtensions, pdfRenderService);
    }

    private static PreviewViewModel CreateForFolder(
        FileSystemNodeViewModel node,
        IFileSystemService fileSystemService,
        IVersionControlService versionControlService,
        ISettingsService settingsService,
        IFolderScanService folderScanService)
    {
        try
        {
            var children = fileSystemService.GetChildren(node.FullPath);
            var folderCount = children.Count(c => c.IsDirectory);
            var fileCount = children.Count - folderCount;
            var summary = $"フォルダ: {folderCount} 件 / ファイル: {fileCount} 件（直下のみ）";

            var vcsInfo = versionControlService.Detect(node.FullPath);
            var vcsSummary = vcsInfo.Kind switch
            {
                VersionControlKind.Git => $"Git（{vcsInfo.BranchName ?? "unknown"}）",
                VersionControlKind.Svn => "SVN",
                _ => string.Empty
            };

            var tags = settingsService.Current.TagAssignments
                .FirstOrDefault(a => string.Equals(a.Path, node.FullPath, StringComparison.OrdinalIgnoreCase))
                ?.Tags ?? new List<string>();
            var tagsSummary = tags.Count > 0 ? string.Join(", ", tags) : string.Empty;

            var recentFiles = children
                .Where(c => !c.IsDirectory && c.LastModified is not null)
                .OrderByDescending(c => c.LastModified)
                .Take(RecentFilesCount)
                .Select(c => $"{c.Name}（{c.LastModified:yyyy/MM/dd HH:mm}）")
                .ToList();

            var preview = new PreviewViewModel(
                node.Name, node.FullPath, PreviewKind.Folder, string.Empty, null, summary, false, null,
                node.LastModified?.ToString("yyyy/MM/dd HH:mm") ?? string.Empty,
                vcsSummary, tagsSummary, recentFiles);

            preview.StartFolderSizeCalculation(node.FullPath, folderScanService);
            return preview;
        }
        catch (AppOperationException ex)
        {
            return new PreviewViewModel(
                node.Name, node.FullPath, PreviewKind.Folder, string.Empty, null, ex.Message, false, null,
                string.Empty, string.Empty, string.Empty, Array.Empty<string>());
        }
    }

    // 仕様書14章：フォルダの合計サイズは大きいフォルダだと時間がかかるため、非同期・キャンセル可能にする。
    private void StartFolderSizeCalculation(string path, IFolderScanService folderScanService)
    {
        _folderSizeCts = new CancellationTokenSource();
        var token = _folderSizeCts.Token;
        FolderSizeDisplay = "計算中...";

        _ = Task.Run(async () =>
        {
            try
            {
                var totalBytes = await folderScanService.CalculateFolderSizeAsync(path, token);
                if (!token.IsCancellationRequested)
                {
                    FolderSizeDisplay = FormatSize(totalBytes);
                }
            }
            catch (OperationCanceledException)
            {
                // プレビューが切り替わった・閉じられたことによるキャンセルは無視する。
            }
        }, token);
    }

    private static string FormatSize(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double size = bytes;
        var unitIndex = 0;

        while (size >= 1024 && unitIndex < units.Length - 1)
        {
            size /= 1024;
            unitIndex++;
        }

        return unitIndex == 0 ? $"{size:0} {units[unitIndex]}" : $"{size:0.#} {units[unitIndex]}";
    }

    private static PreviewViewModel CreateForFile(
        FileSystemNodeViewModel node,
        IFileSystemService fileSystemService,
        IReadOnlyList<string> customTextExtensions,
        IPdfRenderService pdfRenderService)
    {
        var extension = Path.GetExtension(node.Name).TrimStart('.').ToLowerInvariant();
        var isMarkdown = extension is "md" or "markdown";

        if (ImageExtensions.Contains(extension))
        {
            return CreateForImage(node);
        }

        if (extension == "pdf")
        {
            return CreateForPdf(node, pdfRenderService);
        }

        if (OfficeExtensions.Contains(extension))
        {
            return CreateForOffice(node, extension);
        }

        var textExtensions = new HashSet<string>(DefaultTextExtensions, StringComparer.OrdinalIgnoreCase);
        foreach (var ext in customTextExtensions)
        {
            textExtensions.Add(ext.TrimStart('.'));
        }

        if (!isMarkdown && !textExtensions.Contains(extension))
        {
            return Unsupported(node.Name, node.FullPath, "このファイル形式はプレビューに対応していません。");
        }

        try
        {
            var content = fileSystemService.ReadTextPreview(node.FullPath, MaxPreviewChars, out var truncated);

            if (isMarkdown)
            {
                var document = MarkdownRenderer.Render(content);
                return new PreviewViewModel(
                    node.Name, node.FullPath, PreviewKind.Markdown, content, document, string.Empty, truncated, null,
                    string.Empty, string.Empty, string.Empty, Array.Empty<string>());
            }

            var symbols = CodeSymbolExtractor.IsSupported(extension)
                ? CodeSymbolExtractor.Extract(content, extension)
                : Array.Empty<CodeSymbol>();

            return new PreviewViewModel(
                node.Name, node.FullPath, PreviewKind.Text, content, null, string.Empty, truncated, null,
                string.Empty, string.Empty, string.Empty, Array.Empty<string>(), symbols);
        }
        catch (AppOperationException ex)
        {
            return Unsupported(node.Name, node.FullPath, ex.Message);
        }
    }

    // 仕様書13章「PDF」：Windows標準のPDF描画で、ページを画像として表示し、ページ移動できるようにする
    // （外部のライブラリは追加しない）。読み込みは非同期で、その間と、読み込めなかった場合は、
    // ファイル情報と「既定のアプリで開く」ボタンを表示する。
    private static PreviewViewModel CreateForPdf(FileSystemNodeViewModel node, IPdfRenderService pdfRenderService)
    {
        string summary;
        try
        {
            var info = new FileInfo(node.FullPath);
            summary = $"サイズ: {FormatSize(info.Length)}\n更新日時: {info.LastWriteTime:yyyy/MM/dd HH:mm}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            summary = "ファイルの情報を取得できませんでした。";
        }

        var preview = new PreviewViewModel(
            node.Name, node.FullPath, PreviewKind.Pdf, summary, null, string.Empty, false, null,
            string.Empty, string.Empty, string.Empty, Array.Empty<string>());

        preview.StartPdfRendering(node.FullPath, pdfRenderService);
        return preview;
    }

    // 仕様書13章「Office」：docx/xlsx/pptxはOOXML(ZIP+XML)であることを利用し、本文テキストのみを
    // 抽出して表示する（書式・レイアウトの再現は行わない簡易プレビュー）。
    private static PreviewViewModel CreateForOffice(FileSystemNodeViewModel node, string extension)
    {
        try
        {
            var text = OfficeTextExtractor.Extract(node.FullPath, extension);
            var content = string.IsNullOrWhiteSpace(text)
                ? "（テキストを抽出できませんでした。画像のみのファイル等の可能性があります）"
                : text;

            return new PreviewViewModel(
                node.Name, node.FullPath, PreviewKind.Text, content, null, string.Empty, false, null,
                string.Empty, string.Empty, string.Empty, Array.Empty<string>());
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or System.Xml.XmlException)
        {
            return Unsupported(node.Name, node.FullPath, $"このファイルを読み込めませんでした。({ex.Message})");
        }
    }

    // 仕様書13章：画像プレビュー。WebP等、実行環境のWICコーデックが対応していない形式は
    // 例外を握りつぶし、日本語の理由付きで「対応していません」表示にフォールバックする（27章）。
    private static PreviewViewModel CreateForImage(FileSystemNodeViewModel node)
    {
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.UriSource = new Uri(node.FullPath, UriKind.Absolute);
            image.EndInit();
            image.Freeze();

            return new PreviewViewModel(
                node.Name, node.FullPath, PreviewKind.Image, string.Empty, null, string.Empty, false, image,
                string.Empty, string.Empty, string.Empty, Array.Empty<string>());
        }
        catch (Exception ex) when (ex is NotSupportedException or IOException or ArgumentException or InvalidOperationException)
        {
            return Unsupported(node.Name, node.FullPath, $"この画像を読み込めませんでした。実行環境に対応するコーデックがない可能性があります。({ex.Message})");
        }
    }

    private static PreviewViewModel Unsupported(string title, string fullPath, string reason)
    {
        return new PreviewViewModel(
            title, fullPath, PreviewKind.Unsupported, reason, null, string.Empty, false, null,
            string.Empty, string.Empty, string.Empty, Array.Empty<string>());
    }
}
