using System.Windows.Media;

namespace ExplorerAlternative.Models;

/// <summary>
/// 仕様書17章「ANSIカラー」：ターミナル出力を色付きの断片単位で保持する。
/// 色がnullの場合はターミナル既定色（テーマのTerminalForegroundBrush等）を使う。
/// </summary>
public sealed class TerminalSegment
{
    public required string Text { get; init; }

    public Color? Foreground { get; init; }

    public Color? Background { get; init; }

    public bool IsBold { get; init; }

    /// <summary>
    /// 「現在の行を消して、行頭から書き直す」ことを表す印（テキストは空）。単独のCR（プログレスバー等の
    /// 行の上書き）や、行全体の消去・行頭へ戻る制御シーケンスで発生する。スクロールバックには残さず、
    /// 受け取った側が現在の行を消す。
    /// </summary>
    public bool IsLineReset { get; init; }

    public static TerminalSegment LineReset() => new() { Text = string.Empty, IsLineReset = true };
}
