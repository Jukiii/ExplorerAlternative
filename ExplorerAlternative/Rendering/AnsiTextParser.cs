using System.Text;
using System.Windows.Media;
using ExplorerAlternative.Models;

namespace ExplorerAlternative.Rendering;

/// <summary>
/// 仕様書17章「ANSIカラー」：ANSIエスケープシーケンス（SGR）を解釈して色付きの
/// <see cref="TerminalSegment"/>へ変換する。ターミナル1つにつき1インスタンスを使い、
/// 色の状態とエスケープシーケンスの途中状態をチャンクをまたいで保持する
/// （標準出力は任意の位置で分割されて届くため）。
///
/// 行の上書き（プログレスバー等）：単独のCR（直後が改行でないCR）と、行全体の消去（ESC[2K）・
/// 行頭への移動（ESC[G / ESC[1G）は、「現在の行を消して書き直す」印（<see cref="TerminalSegment.IsLineReset"/>）
/// として出力する。CRLFのCRは、これまでどおり改行だけとして扱う。端末のカーソル位置は管理せず、
/// 常に行末に書き続けるモデルなので、CRのあとの文字列が元の行より短い場合も、元の行の残りは
/// 残らない（実際の端末では残る）。
/// それ以外のCSI（カーソルの上下移動・画面消去等）とOSC（ウィンドウタイトル等）は読み飛ばす。
/// </summary>
public sealed class AnsiTextParser
{
    private const char Escape = '\u001b';

    // 標準16色（Windows Terminalの既定に近い値）。
    private static readonly Color[] StandardColors =
    {
        Color.FromRgb(0x0C, 0x0C, 0x0C), // 0 black
        Color.FromRgb(0xC5, 0x0F, 0x1F), // 1 red
        Color.FromRgb(0x13, 0xA1, 0x0E), // 2 green
        Color.FromRgb(0xC1, 0x9C, 0x00), // 3 yellow
        Color.FromRgb(0x00, 0x37, 0xDA), // 4 blue
        Color.FromRgb(0x88, 0x17, 0x98), // 5 magenta
        Color.FromRgb(0x3A, 0x96, 0xDD), // 6 cyan
        Color.FromRgb(0xCC, 0xCC, 0xCC), // 7 white
        Color.FromRgb(0x76, 0x76, 0x76), // 8 bright black
        Color.FromRgb(0xE7, 0x48, 0x56), // 9 bright red
        Color.FromRgb(0x16, 0xC6, 0x0C), // 10 bright green
        Color.FromRgb(0xF9, 0xF1, 0xA5), // 11 bright yellow
        Color.FromRgb(0x3B, 0x78, 0xFF), // 12 bright blue
        Color.FromRgb(0xB4, 0x00, 0x9E), // 13 bright magenta
        Color.FromRgb(0x61, 0xD6, 0xD6), // 14 bright cyan
        Color.FromRgb(0xF2, 0xF2, 0xF2)  // 15 bright white
    };

    private readonly StringBuilder _pendingText = new();
    private readonly StringBuilder _pendingEscape = new();

    private Color? _foreground;
    private Color? _background;
    private bool _isBold;
    private bool _isReverse;

    // CRを受け取ったが、次の1文字がまだ分からない状態（CRLFかCR単独かの判定待ち）。
    // 標準出力は任意の位置で分割されて届くため、CRとLFが別のチャンクになることがある。
    private bool _pendingCarriageReturn;

    /// <summary>受け取った生テキストを解釈し、色付きの断片へ変換する。
    /// エスケープシーケンスが途中で切れている場合は次回の呼び出しまで持ち越す。</summary>
    public IReadOnlyList<TerminalSegment> Parse(string chunk)
    {
        var segments = new List<TerminalSegment>();

        foreach (var c in chunk)
        {
            if (_pendingCarriageReturn)
            {
                _pendingCarriageReturn = false;

                // CRの次が改行ならCRLF（改行だけとして扱う）。それ以外なら、行の上書きの印にする。
                if (c != '\n')
                {
                    FlushPendingText(segments);
                    segments.Add(TerminalSegment.LineReset());
                }
            }

            if (_pendingEscape.Length > 0)
            {
                _pendingEscape.Append(c);

                if (IsEscapeComplete(_pendingEscape))
                {
                    var sequence = _pendingEscape.ToString();
                    _pendingEscape.Clear();

                    // 色が変わる直前までのテキストを、変更前の色で確定させる。
                    FlushPendingText(segments);
                    ApplySequence(sequence, segments);
                }

                continue;
            }

            if (c == Escape)
            {
                _pendingEscape.Append(c);
                continue;
            }

            if (c == '\r')
            {
                // 次の1文字でCRLFかCR単独かを判定する（CRより前のテキストは、その判定まで保留しておく）。
                _pendingCarriageReturn = true;
                continue;
            }

            _pendingText.Append(c);
        }

        FlushPendingText(segments);
        return segments;
    }

    private void FlushPendingText(List<TerminalSegment> segments)
    {
        if (_pendingText.Length == 0)
        {
            return;
        }

        var foreground = _isReverse ? _background : _foreground;
        var background = _isReverse ? _foreground : _background;

        segments.Add(new TerminalSegment
        {
            Text = _pendingText.ToString(),
            Foreground = foreground,
            Background = background,
            IsBold = _isBold
        });

        _pendingText.Clear();
    }

    // CSI（ESC[...）は@〜~の範囲の文字で終端、OSC（ESC]...）はBELまたはESC\で終端。
    // それ以外のESC+1文字（文字セット選択等）は2文字で完結とみなす。
    private static bool IsEscapeComplete(StringBuilder buffer)
    {
        if (buffer.Length < 2)
        {
            return false;
        }

        var last = buffer[^1];

        return buffer[1] switch
        {
            '[' => buffer.Length > 2 && last is >= '@' and <= '~',
            ']' => last == '\u0007' || (buffer.Length > 2 && last == '\\' && buffer[^2] == Escape),
            _ => true
        };
    }

    private void ApplySequence(string sequence, List<TerminalSegment> segments)
    {
        if (sequence.Length < 3 || sequence[1] != '[')
        {
            return;
        }

        if (sequence[^1] != 'm')
        {
            // SGR以外は表示色に影響しない。ただし、行全体の消去（ESC[2K）と行頭への移動
            // （ESC[G / ESC[1G）は、CRと同じく「現在の行を消して書き直す」ものとして扱う。
            // 行末までの消去（ESC[K）は、常に行末に書くモデルなので何もしなくてよい。
            var final = sequence[^1];
            var parameter = sequence[2..^1];

            if ((final == 'K' && parameter == "2") || (final == 'G' && (parameter.Length == 0 || parameter == "1")))
            {
                segments.Add(TerminalSegment.LineReset());
            }

            return;
        }

        var body = sequence[2..^1];
        if (body.Length == 0)
        {
            Reset();
            return;
        }

        var codes = body.Split(';');

        for (var i = 0; i < codes.Length; i++)
        {
            if (!int.TryParse(codes[i], out var code))
            {
                continue;
            }

            switch (code)
            {
                case 0:
                    Reset();
                    break;
                case 1:
                    _isBold = true;
                    break;
                case 22:
                    _isBold = false;
                    break;
                case 7:
                    _isReverse = true;
                    break;
                case 27:
                    _isReverse = false;
                    break;
                case 39:
                    _foreground = null;
                    break;
                case 49:
                    _background = null;
                    break;
                case >= 30 and <= 37:
                    _foreground = StandardColors[code - 30];
                    break;
                case >= 90 and <= 97:
                    _foreground = StandardColors[code - 90 + 8];
                    break;
                case >= 40 and <= 47:
                    _background = StandardColors[code - 40];
                    break;
                case >= 100 and <= 107:
                    _background = StandardColors[code - 100 + 8];
                    break;
                case 38:
                    _foreground = ReadExtendedColor(codes, ref i);
                    break;
                case 48:
                    _background = ReadExtendedColor(codes, ref i);
                    break;
            }
        }
    }

    // 38/48 に続く拡張色指定：「5;N」が256色、「2;R;G;B」がフルカラー。
    private static Color? ReadExtendedColor(string[] codes, ref int index)
    {
        if (index + 1 >= codes.Length || !int.TryParse(codes[index + 1], out var mode))
        {
            return null;
        }

        if (mode == 5 && index + 2 < codes.Length && int.TryParse(codes[index + 2], out var paletteIndex))
        {
            index += 2;
            return FromPalette(paletteIndex);
        }

        if (mode == 2 && index + 4 < codes.Length &&
            int.TryParse(codes[index + 2], out var r) &&
            int.TryParse(codes[index + 3], out var g) &&
            int.TryParse(codes[index + 4], out var b))
        {
            index += 4;
            return Color.FromRgb(ClampByte(r), ClampByte(g), ClampByte(b));
        }

        return null;
    }

    private static Color FromPalette(int index)
    {
        if (index is >= 0 and < 16)
        {
            return StandardColors[index];
        }

        // 16-231: 6x6x6 のカラーキューブ
        if (index is >= 16 and <= 231)
        {
            var offset = index - 16;
            var r = offset / 36;
            var g = offset % 36 / 6;
            var b = offset % 6;
            return Color.FromRgb(CubeLevel(r), CubeLevel(g), CubeLevel(b));
        }

        // 232-255: グレースケール
        if (index is >= 232 and <= 255)
        {
            var level = ClampByte(8 + ((index - 232) * 10));
            return Color.FromRgb(level, level, level);
        }

        return StandardColors[7];
    }

    private static byte CubeLevel(int component) => component == 0 ? (byte)0 : ClampByte(55 + (component * 40));

    private static byte ClampByte(int value) => (byte)Math.Clamp(value, 0, 255);

    private void Reset()
    {
        _foreground = null;
        _background = null;
        _isBold = false;
        _isReverse = false;
    }
}
