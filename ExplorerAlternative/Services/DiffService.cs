using ExplorerAlternative.Models;
using ExplorerAlternative.Services.Abstractions;

namespace ExplorerAlternative.Services;

/// <summary>
/// 行単位のDiff（仕様書23章・25章）。LCS（最長共通部分列）に基づく素朴な実装。
/// 巨大ファイル同士だとO(n*m)のDPテーブルが破綻するため、上限を超える場合は
/// 行単位の詳細比較を諦め「全置換」として扱う（27章：クラッシュさせない）。
/// </summary>
public sealed class DiffService : IDiffService
{
    private const int MaxCellCount = 4_000_000;

    public IReadOnlyList<DiffLine> Compare(string? leftText, string? rightText)
    {
        var left = SplitLines(leftText);
        var right = SplitLines(rightText);

        if ((long)left.Length * right.Length > MaxCellCount)
        {
            return BuildWholeFileReplacement(left, right);
        }

        return BuildLcsDiff(left, right);
    }

    private static string[] SplitLines(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return Array.Empty<string>();
        }

        return text.Replace("\r\n", "\n").Split('\n');
    }

    private static List<DiffLine> BuildWholeFileReplacement(string[] left, string[] right)
    {
        var result = new List<DiffLine>(left.Length + right.Length);

        foreach (var line in left)
        {
            result.Add(new DiffLine { Kind = DiffLineKind.Removed, Text = line });
        }

        foreach (var line in right)
        {
            result.Add(new DiffLine { Kind = DiffLineKind.Added, Text = line });
        }

        return result;
    }

    private static List<DiffLine> BuildLcsDiff(string[] left, string[] right)
    {
        var n = left.Length;
        var m = right.Length;
        var dp = new int[n + 1, m + 1];

        for (var i = n - 1; i >= 0; i--)
        {
            for (var j = m - 1; j >= 0; j--)
            {
                dp[i, j] = left[i] == right[j]
                    ? dp[i + 1, j + 1] + 1
                    : Math.Max(dp[i + 1, j], dp[i, j + 1]);
            }
        }

        var result = new List<DiffLine>();
        var a = 0;
        var b = 0;

        while (a < n && b < m)
        {
            if (left[a] == right[b])
            {
                result.Add(new DiffLine { Kind = DiffLineKind.Equal, Text = left[a] });
                a++;
                b++;
            }
            else if (dp[a + 1, b] >= dp[a, b + 1])
            {
                result.Add(new DiffLine { Kind = DiffLineKind.Removed, Text = left[a] });
                a++;
            }
            else
            {
                result.Add(new DiffLine { Kind = DiffLineKind.Added, Text = right[b] });
                b++;
            }
        }

        while (a < n)
        {
            result.Add(new DiffLine { Kind = DiffLineKind.Removed, Text = left[a] });
            a++;
        }

        while (b < m)
        {
            result.Add(new DiffLine { Kind = DiffLineKind.Added, Text = right[b] });
            b++;
        }

        return result;
    }
}
