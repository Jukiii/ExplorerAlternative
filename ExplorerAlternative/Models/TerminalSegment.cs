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
}
