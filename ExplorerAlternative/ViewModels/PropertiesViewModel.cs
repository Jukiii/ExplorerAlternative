using System.IO;
using ExplorerAlternative.Services;
using ExplorerAlternative.Services.Abstractions;

namespace ExplorerAlternative.ViewModels;

/// <summary>
/// プロパティ表示（仕様書48章「基本ファイル操作」）。選択中の1件について
/// 名前・パス・種類・サイズ・作成日時・更新日時・属性を表示する。
/// </summary>
public sealed class PropertiesViewModel
{
    private PropertiesViewModel(string name, string fullPath, string typeLabel, string sizeLabel,
        string createdLabel, string modifiedLabel, string attributesLabel)
    {
        Name = name;
        FullPath = fullPath;
        TypeLabel = typeLabel;
        SizeLabel = sizeLabel;
        CreatedLabel = createdLabel;
        ModifiedLabel = modifiedLabel;
        AttributesLabel = attributesLabel;
    }

    public string Name { get; }

    public string FullPath { get; }

    public string TypeLabel { get; }

    public string SizeLabel { get; }

    public string CreatedLabel { get; }

    public string ModifiedLabel { get; }

    public string AttributesLabel { get; }

    public static PropertiesViewModel Create(FileSystemNodeViewModel node, IFileSystemService fileSystemService)
    {
        var created = FormatDate(node.Created);
        var modified = FormatDate(node.LastModified);
        var attributes = FormatAttributes(node.FullPath, node.IsDirectory);

        if (node.IsDirectory)
        {
            var sizeLabel = "計算できませんでした";

            try
            {
                var children = fileSystemService.GetChildren(node.FullPath);
                var folderCount = children.Count(c => c.IsDirectory);
                var fileCount = children.Count - folderCount;
                sizeLabel = $"フォルダ: {folderCount} 件 / ファイル: {fileCount} 件（直下のみ）";
            }
            catch (AppOperationException)
            {
                // アクセス権限がない等の場合はラベルを既定値のまま表示する。
            }

            return new PropertiesViewModel(node.Name, node.FullPath, "フォルダー", sizeLabel, created, modified, attributes);
        }

        var extension = System.IO.Path.GetExtension(node.Name);
        var typeLabel = string.IsNullOrEmpty(extension) ? "ファイル" : $"{extension.TrimStart('.').ToUpperInvariant()} ファイル";
        var fileSizeLabel = node.SizeBytes is null ? "不明" : FormatBytes(node.SizeBytes.Value);

        return new PropertiesViewModel(node.Name, node.FullPath, typeLabel, fileSizeLabel, created, modified, attributes);
    }

    private static string FormatDate(DateTime? value) => value?.ToString("yyyy/MM/dd HH:mm:ss") ?? "不明";

    private static string FormatAttributes(string path, bool isDirectory)
    {
        try
        {
            var attributes = isDirectory
                ? new System.IO.DirectoryInfo(path).Attributes
                : new System.IO.FileInfo(path).Attributes;

            var flags = new List<string>();

            if (attributes.HasFlag(FileAttributes.ReadOnly))
            {
                flags.Add("読み取り専用");
            }

            if (attributes.HasFlag(FileAttributes.Hidden))
            {
                flags.Add("隠しファイル");
            }

            return flags.Count == 0 ? "標準" : string.Join(" / ", flags);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return "不明";
        }
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = { "バイト", "KB", "MB", "GB", "TB" };
        double size = bytes;
        var unitIndex = 0;

        while (size >= 1024 && unitIndex < units.Length - 1)
        {
            size /= 1024;
            unitIndex++;
        }

        var formatted = unitIndex == 0 ? $"{size:0}" : $"{size:0.#}";
        return unitIndex == 0 ? $"{formatted} {units[unitIndex]}" : $"{formatted} {units[unitIndex]} ({bytes:N0} バイト)";
    }
}
