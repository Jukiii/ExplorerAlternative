using System.IO;
using System.Windows.Documents;
using ExplorerAlternative.Rendering;
using ExplorerAlternative.Services;
using ExplorerAlternative.Services.Abstractions;

namespace ExplorerAlternative.ViewModels;

/// <summary>
/// Quick Lookに近いプレビュー（仕様書11章）。Spaceキーで選択中のファイル・フォルダを表示する。
/// </summary>
public sealed class PreviewViewModel
{
    private const int MaxPreviewChars = 200_000;

    private static readonly string[] DefaultTextExtensions =
    {
        "txt", "md", "markdown", "json", "xml", "csv", "cs", "xaml", "ps1",
        "py", "js", "ts", "html", "css", "yml", "yaml", "ini", "log"
    };

    private PreviewViewModel(string title, PreviewKind kind, string textContent, FlowDocument? markdownDocument, string folderSummary, bool truncated)
    {
        Title = title;
        Kind = kind;
        TextContent = textContent;
        MarkdownDocument = markdownDocument;
        FolderSummary = folderSummary;
        Truncated = truncated;
    }

    public string Title { get; }

    public PreviewKind Kind { get; }

    public string TextContent { get; }

    public FlowDocument? MarkdownDocument { get; }

    public string FolderSummary { get; }

    public bool Truncated { get; }

    public static PreviewViewModel Create(FileSystemNodeViewModel node, IFileSystemService fileSystemService, IReadOnlyList<string> customTextExtensions)
    {
        if (node.IsDirectory)
        {
            return CreateForFolder(node, fileSystemService);
        }

        return CreateForFile(node, fileSystemService, customTextExtensions);
    }

    private static PreviewViewModel CreateForFolder(FileSystemNodeViewModel node, IFileSystemService fileSystemService)
    {
        try
        {
            var children = fileSystemService.GetChildren(node.FullPath);
            var folderCount = children.Count(c => c.IsDirectory);
            var fileCount = children.Count - folderCount;
            var summary = $"フォルダ: {folderCount} 件 / ファイル: {fileCount} 件";
            return new PreviewViewModel(node.Name, PreviewKind.Folder, string.Empty, null, summary, false);
        }
        catch (AppOperationException ex)
        {
            return new PreviewViewModel(node.Name, PreviewKind.Folder, string.Empty, null, ex.Message, false);
        }
    }

    private static PreviewViewModel CreateForFile(FileSystemNodeViewModel node, IFileSystemService fileSystemService, IReadOnlyList<string> customTextExtensions)
    {
        var extension = Path.GetExtension(node.Name).TrimStart('.').ToLowerInvariant();
        var isMarkdown = extension is "md" or "markdown";

        var textExtensions = new HashSet<string>(DefaultTextExtensions, StringComparer.OrdinalIgnoreCase);
        foreach (var ext in customTextExtensions)
        {
            textExtensions.Add(ext.TrimStart('.'));
        }

        if (!isMarkdown && !textExtensions.Contains(extension))
        {
            return new PreviewViewModel(node.Name, PreviewKind.Unsupported, "このファイル形式はプレビューに対応していません。", null, string.Empty, false);
        }

        try
        {
            var content = fileSystemService.ReadTextPreview(node.FullPath, MaxPreviewChars, out var truncated);

            if (isMarkdown)
            {
                var document = MarkdownRenderer.Render(content);
                return new PreviewViewModel(node.Name, PreviewKind.Markdown, content, document, string.Empty, truncated);
            }

            return new PreviewViewModel(node.Name, PreviewKind.Text, content, null, string.Empty, truncated);
        }
        catch (AppOperationException ex)
        {
            return new PreviewViewModel(node.Name, PreviewKind.Unsupported, ex.Message, null, string.Empty, false);
        }
    }
}
