namespace ExplorerAlternative.Services.Abstractions;

/// <summary>
/// ターミナルのTab補完の候補を求める（仕様書17章）。標準入出力リダイレクト方式のPowerShellは
/// 対話的な補完を行わないため、補完だけを別のPowerShell（<c>TabExpansion2</c>）に問い合わせる。
/// </summary>
public interface ITabCompletionService : IDisposable
{
    /// <summary>
    /// 入力行<paramref name="input"/>のカーソル位置<paramref name="caretIndex"/>での補完候補を求める。
    /// 求められなかった場合（時間切れ・起動失敗など）は、例外にせず<c>null</c>を返す。
    /// </summary>
    /// <param name="workingDirectory">補完の基準にするフォルダ（ターミナルの現在のフォルダ）。</param>
    Task<TabCompletionResult?> CompleteAsync(string input, int caretIndex, string? workingDirectory, CancellationToken cancellationToken);
}

/// <summary>
/// 補完の結果。入力行の<see cref="ReplacementIndex"/>から<see cref="ReplacementLength"/>文字を、
/// <see cref="Matches"/>のどれか（そのまま挿入できる文字列。空白を含むパスは引用符付き）に置き換える。
/// </summary>
public sealed record TabCompletionResult(int ReplacementIndex, int ReplacementLength, IReadOnlyList<string> Matches)
{
    public static TabCompletionResult None { get; } = new(0, 0, Array.Empty<string>());
}
