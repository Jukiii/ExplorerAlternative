using System.IO;
using System.Text.RegularExpressions;

namespace ExplorerAlternative.Services;

/// <summary>
/// 仕様書19章「TerminalからExplorerへのドラッグ」：ターミナル上で選んだ文字列を、Explorerのペインへ
/// ドロップしたときに、それがどのファイル・フォルダを指すかを求める。画面に依存しない純粋な計算。
///
/// ターミナルの出力から選んだ文字列には、引用符・PowerShellのプロンプト・複数行・相対パスなどが
/// 含まれることがあるため、次の順で解釈する：
/// 1. 最初の空でない行だけを使い、前後の空白を除く。
/// 2. 行頭のPowerShellのプロンプト（<c>PS C:\foo&gt; </c>）は除く。
/// 3. 前後の引用符（<c>"</c> または <c>'</c>）は除く。
/// 4. 環境変数（<c>%USERPROFILE%</c>）は展開する。
/// 5. 相対パスは、<paramref name="baseDirectory"/>（ペインの現在のフォルダ）からのパスとして解釈する。
/// 6. 実在するファイル・フォルダのときだけ、成功とする。
/// </summary>
public static class DroppedPathResolver
{
    private static readonly Regex PromptPattern = new(@"^PS [^>]*>\s*", RegexOptions.Compiled);

    public static bool TryResolve(
        string? text,
        string baseDirectory,
        out string fullPath,
        out bool isDirectory,
        Func<string, bool>? directoryExists = null,
        Func<string, bool>? fileExists = null)
    {
        fullPath = string.Empty;
        isDirectory = false;

        var candidate = ExtractCandidate(text);
        if (candidate is null)
        {
            return false;
        }

        var dirExists = directoryExists ?? Directory.Exists;
        var fileExistsCheck = fileExists ?? File.Exists;

        string resolved;
        try
        {
            candidate = Environment.ExpandEnvironmentVariables(candidate);

            if (candidate.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
            {
                return false;
            }

            if (!Path.IsPathRooted(candidate))
            {
                // 現在のフォルダが無い（PC直下）場合は、相対パスを解釈できない。
                if (string.IsNullOrEmpty(baseDirectory))
                {
                    return false;
                }

                candidate = Path.Combine(baseDirectory, candidate);
            }

            resolved = Path.GetFullPath(candidate);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }

        // 末尾の区切り文字は除く（C:\ のようなルートは残す）。
        var root = Path.GetPathRoot(resolved);
        if (!string.Equals(resolved, root, StringComparison.OrdinalIgnoreCase))
        {
            resolved = resolved.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        if (dirExists(resolved))
        {
            fullPath = resolved;
            isDirectory = true;
            return true;
        }

        if (fileExistsCheck(resolved))
        {
            fullPath = resolved;
            return true;
        }

        return false;
    }

    // 最初の空でない行から、プロンプトと前後の引用符を除いた文字列を取り出す。
    private static string? ExtractCandidate(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var line = text.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0);
        if (line is null)
        {
            return null;
        }

        line = PromptPattern.Replace(line, string.Empty).Trim();

        if (line.Length >= 2 && ((line[0] == '"' && line[^1] == '"') || (line[0] == '\'' && line[^1] == '\'')))
        {
            line = line[1..^1].Trim();
        }

        return line.Length == 0 ? null : line;
    }
}
