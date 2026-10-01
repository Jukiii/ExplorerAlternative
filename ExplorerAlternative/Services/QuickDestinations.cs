using System.IO;
using ExplorerAlternative.Models;

namespace ExplorerAlternative.Services;

/// <summary>
/// 仕様書60章「クイックコピー / クイック移動」：コピー先・移動先の履歴を、ファイル操作履歴
/// （仕様書31章）から求める。画面に依存しない純粋な計算。
/// </summary>
public static class QuickDestinations
{
    /// <summary>候補として出す件数の上限。</summary>
    public const int DefaultMaxCount = 10;

    /// <summary>
    /// 最近のコピー先（<paramref name="isMove"/>がfalse）または移動先（true）を、新しい順に返す。
    /// - 成功した操作だけを対象にする。
    /// - 同じフォルダは1件にまとめる（大文字小文字は区別しない。Windowsのパスのため）。
    /// - 現在存在しないフォルダは除く（選んでもエラーになるため）。
    /// - <paramref name="excludeFolder"/>（現在のフォルダなど）は除く。
    /// </summary>
    /// <param name="history">操作履歴。新しい順（<c>IFileOperationHistoryService.GetAll</c>と同じ）。</param>
    /// <param name="directoryExists">フォルダの存在確認。テストで差し替えられるようにしてある。</param>
    public static IReadOnlyList<string> GetRecent(
        IEnumerable<FileOperationHistoryEntry> history,
        bool isMove,
        string? excludeFolder = null,
        int maxCount = DefaultMaxCount,
        Func<string, bool>? directoryExists = null)
    {
        var operation = isMove ? "移動" : "コピー";
        var exists = directoryExists ?? Directory.Exists;
        var excluded = Normalize(excludeFolder);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();

        foreach (var entry in history)
        {
            if (result.Count >= maxCount)
            {
                break;
            }

            if (!entry.Success || entry.Operation != operation || string.IsNullOrWhiteSpace(entry.Destination))
            {
                continue;
            }

            var destination = Normalize(entry.Destination);
            if (destination is null ||
                string.Equals(destination, excluded, StringComparison.OrdinalIgnoreCase) ||
                !seen.Add(destination) ||
                !exists(destination))
            {
                continue;
            }

            result.Add(destination);
        }

        return result;
    }

    // 末尾の区切り文字だけが違うパス（C:\a と C:\a\）を同じフォルダとして扱う。ルート（C:\）は残す。
    private static string? Normalize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var root = Path.GetPathRoot(path);
        return string.Equals(path, root, StringComparison.OrdinalIgnoreCase)
            ? path
            : path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }
}
