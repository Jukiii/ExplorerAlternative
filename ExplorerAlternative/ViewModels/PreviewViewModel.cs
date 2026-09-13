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

    private PreviewViewModel(
        string title, PreviewKind kind, string textContent, FlowDocument? markdownDocument,
        string folderSummary, bool truncated, BitmapImage? imageSource,
        string folderModified, string folderVcsSummary, string folderTagsSummary,
        IReadOnlyList<string> recentFiles)
    {
        Title = title;
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

        ToggleMarkdownSourceCommand = new RelayCommand(_ => IsShowingMarkdownSource = !IsShowingMarkdownSource);
        TogglePinCommand = new RelayCommand(_ => IsPinned = !IsPinned);
        PreviousCommand = new RelayCommand(_ => RequestPrevious?.Invoke());
        NextCommand = new RelayCommand(_ => RequestNext?.Invoke());
    }

    public string Title { get; }

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

    public static PreviewViewModel Create(
        FileSystemNodeViewModel node,
        IFileSystemService fileSystemService,
        IVersionControlService versionControlService,
        ISettingsService settingsService)
    {
        return node.IsDirectory
            ? CreateForFolder(node, fileSystemService, versionControlService, settingsService)
            : CreateForFile(node, fileSystemService, settingsService.Current.TextFileExtensions);
    }

    private static PreviewViewModel CreateForFolder(
        FileSystemNodeViewModel node,
        IFileSystemService fileSystemService,
        IVersionControlService versionControlService,
        ISettingsService settingsService)
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

            return new PreviewViewModel(
                node.Name, PreviewKind.Folder, string.Empty, null, summary, false, null,
                node.LastModified?.ToString("yyyy/MM/dd HH:mm") ?? string.Empty,
                vcsSummary, tagsSummary, recentFiles);
        }
        catch (AppOperationException ex)
        {
            return new PreviewViewModel(
                node.Name, PreviewKind.Folder, string.Empty, null, ex.Message, false, null,
                string.Empty, string.Empty, string.Empty, Array.Empty<string>());
        }
    }

    private static PreviewViewModel CreateForFile(FileSystemNodeViewModel node, IFileSystemService fileSystemService, IReadOnlyList<string> customTextExtensions)
    {
        var extension = Path.GetExtension(node.Name).TrimStart('.').ToLowerInvariant();
        var isMarkdown = extension is "md" or "markdown";

        if (ImageExtensions.Contains(extension))
        {
            return CreateForImage(node);
        }

        var textExtensions = new HashSet<string>(DefaultTextExtensions, StringComparer.OrdinalIgnoreCase);
        foreach (var ext in customTextExtensions)
        {
            textExtensions.Add(ext.TrimStart('.'));
        }

        if (!isMarkdown && !textExtensions.Contains(extension))
        {
            return Unsupported(node.Name, "このファイル形式はプレビューに対応していません。");
        }

        try
        {
            var content = fileSystemService.ReadTextPreview(node.FullPath, MaxPreviewChars, out var truncated);

            if (isMarkdown)
            {
                var document = MarkdownRenderer.Render(content);
                return new PreviewViewModel(
                    node.Name, PreviewKind.Markdown, content, document, string.Empty, truncated, null,
                    string.Empty, string.Empty, string.Empty, Array.Empty<string>());
            }

            return new PreviewViewModel(
                node.Name, PreviewKind.Text, content, null, string.Empty, truncated, null,
                string.Empty, string.Empty, string.Empty, Array.Empty<string>());
        }
        catch (AppOperationException ex)
        {
            return Unsupported(node.Name, ex.Message);
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
                node.Name, PreviewKind.Image, string.Empty, null, string.Empty, false, image,
                string.Empty, string.Empty, string.Empty, Array.Empty<string>());
        }
        catch (Exception ex) when (ex is NotSupportedException or IOException or ArgumentException or InvalidOperationException)
        {
            return Unsupported(node.Name, $"この画像を読み込めませんでした。実行環境に対応するコーデックがない可能性があります。({ex.Message})");
        }
    }

    private static PreviewViewModel Unsupported(string title, string reason)
    {
        return new PreviewViewModel(
            title, PreviewKind.Unsupported, reason, null, string.Empty, false, null,
            string.Empty, string.Empty, string.Empty, Array.Empty<string>());
    }
}
