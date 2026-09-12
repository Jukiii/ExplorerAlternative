using System.IO;

namespace ExplorerAlternative.ViewModels;

/// <summary>
/// 一括名前変更（仕様書21章）のパターン展開ロジック。プレビュー表示と実際のリネーム実行の
/// 両方から呼び出し、結果が食い違わないようにする。
/// </summary>
public static class RenamePatternExpander
{
    /// <summary>
    /// {n} = 連番（1始まり）、{name} = 拡張子を除いた元の名前、{ext} = 拡張子（ドットなし）。
    /// </summary>
    public static string Expand(string pattern, string originalName, int index)
    {
        return pattern
            .Replace("{n}", (index + 1).ToString())
            .Replace("{name}", Path.GetFileNameWithoutExtension(originalName))
            .Replace("{ext}", Path.GetExtension(originalName).TrimStart('.'));
    }
}
