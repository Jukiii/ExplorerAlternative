using System.IO;
using ExplorerAlternative.Models;

namespace ExplorerAlternative.Services;

/// <summary>
/// 仕様書34章「常にこのアプリで開く」の、拡張子の正規化・関連付けの検索・登録・解除。
/// 画面・ファイルに依存しない純粋な処理。
/// </summary>
public static class AppAssociationResolver
{
    /// <summary>
    /// 拡張子を、ドット付き・小文字の形（<c>.cs</c>）にそろえる。入力は、拡張子（<c>cs</c> / <c>.CS</c>）でも、
    /// ファイル名・パス（<c>C:\x\Main.CS</c>）でもよい。拡張子として扱えない入力はnull。
    /// </summary>
    public static string? NormalizeExtension(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }

        var text = input.Trim();
        string extension;

        if (text.IndexOfAny(new[] { '\\', '/' }) >= 0 || (text.Contains('.') && !text.StartsWith('.')))
        {
            // ファイル名やパスとして渡された場合は、その拡張子を使う。
            extension = Path.GetExtension(text);
        }
        else
        {
            extension = text.StartsWith('.') ? text : "." + text;
        }

        if (extension.Length < 2 ||
            extension.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            extension.Any(char.IsWhiteSpace) ||
            extension[1..].Contains('.'))
        {
            return null;
        }

        return extension.ToLowerInvariant();
    }

    /// <summary>ファイルに関連付けられたアプリを探す。拡張子の大文字小文字は区別しない。無ければnull。</summary>
    public static AppAssociation? Find(IEnumerable<AppAssociation> associations, string filePath)
    {
        var extension = NormalizeExtension(Path.GetExtension(filePath));
        if (extension is null)
        {
            return null;
        }

        return associations.FirstOrDefault(a => string.Equals(NormalizeExtension(a.Extension), extension, StringComparison.Ordinal));
    }

    /// <summary>関連付けを登録する。同じ拡張子が既にあれば置き換える。登録できた拡張子（正規化後）を返し、拡張子として扱えない場合はnull。</summary>
    public static string? Set(IList<AppAssociation> associations, string extensionOrFileName, string executablePath)
    {
        var extension = NormalizeExtension(extensionOrFileName);
        if (extension is null || string.IsNullOrWhiteSpace(executablePath))
        {
            return null;
        }

        var existing = associations.FirstOrDefault(a => string.Equals(NormalizeExtension(a.Extension), extension, StringComparison.Ordinal));
        if (existing is not null)
        {
            existing.ExecutablePath = executablePath;
        }
        else
        {
            associations.Add(new AppAssociation { Extension = extension, ExecutablePath = executablePath });
        }

        return extension;
    }

    /// <summary>関連付けを解除する。解除した場合はtrue。</summary>
    public static bool Remove(IList<AppAssociation> associations, string extensionOrFileName)
    {
        var extension = NormalizeExtension(extensionOrFileName);
        if (extension is null)
        {
            return false;
        }

        var existing = associations.FirstOrDefault(a => string.Equals(NormalizeExtension(a.Extension), extension, StringComparison.Ordinal));
        return existing is not null && associations.Remove(existing);
    }
}
