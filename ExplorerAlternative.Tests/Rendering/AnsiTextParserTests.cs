using System.Windows.Media;
using ExplorerAlternative.Models;
using ExplorerAlternative.Rendering;

namespace ExplorerAlternative.Tests.Rendering;

// 仕様書17章「ANSIカラー」：ANSIエスケープシーケンスの解釈。
public sealed class AnsiTextParserTests
{
    private const string Esc = "\u001b";

    private static string JoinText(IEnumerable<TerminalSegment> segments) =>
        string.Concat(segments.Select(s => s.Text));

    [Fact]
    public void Parse_PlainText_ReturnsSingleUncoloredSegment()
    {
        var parser = new AnsiTextParser();

        var segments = parser.Parse("hello world");

        var segment = Assert.Single(segments);
        Assert.Equal("hello world", segment.Text);
        Assert.Null(segment.Foreground);
        Assert.Null(segment.Background);
        Assert.False(segment.IsBold);
    }

    [Fact]
    public void Parse_EmptyChunk_ReturnsNoSegments()
    {
        Assert.Empty(new AnsiTextParser().Parse(string.Empty));
    }

    [Fact]
    public void Parse_StandardForeground_ColorsOnlyTheFollowingText()
    {
        var parser = new AnsiTextParser();

        var segments = parser.Parse($"{Esc}[31mERR{Esc}[0m ok");

        Assert.Equal(2, segments.Count);
        Assert.Equal("ERR", segments[0].Text);
        Assert.Equal(Color.FromRgb(0xC5, 0x0F, 0x1F), segments[0].Foreground);
        Assert.Equal(" ok", segments[1].Text);
        Assert.Null(segments[1].Foreground);
    }

    [Fact]
    public void Parse_BrightForeground_UsesBrightPalette()
    {
        var segments = new AnsiTextParser().Parse($"{Esc}[91mX");

        Assert.Equal(Color.FromRgb(0xE7, 0x48, 0x56), Assert.Single(segments).Foreground);
    }

    [Fact]
    public void Parse_Background_IsApplied()
    {
        var segments = new AnsiTextParser().Parse($"{Esc}[44mX");

        Assert.Equal(Color.FromRgb(0x00, 0x37, 0xDA), Assert.Single(segments).Background);
    }

    [Fact]
    public void Parse_BoldAndReset()
    {
        var segments = new AnsiTextParser().Parse($"{Esc}[1mB{Esc}[22mN{Esc}[1mB2{Esc}[0mN2");

        Assert.Equal(new[] { true, false, true, false }, segments.Select(s => s.IsBold).ToArray());
    }

    [Fact]
    public void Parse_EmptySgr_MeansReset()
    {
        var segments = new AnsiTextParser().Parse($"{Esc}[31mR{Esc}[mN");

        Assert.Null(segments[1].Foreground);
    }

    [Fact]
    public void Parse_Color256_UsesCubeAndGrayscale()
    {
        var parser = new AnsiTextParser();

        // 196 = カラーキューブの (5,0,0) = 純赤。
        var red = parser.Parse($"{Esc}[38;5;196mR");
        Assert.Equal(Color.FromRgb(255, 0, 0), Assert.Single(red).Foreground);

        // 232 = グレースケールの最も暗い段階（8）。
        var gray = parser.Parse($"{Esc}[38;5;232mG");
        Assert.Equal(Color.FromRgb(8, 8, 8), Assert.Single(gray).Foreground);

        // 0〜15は標準16色と同じ。
        var standard = parser.Parse($"{Esc}[38;5;1mS");
        Assert.Equal(Color.FromRgb(0xC5, 0x0F, 0x1F), Assert.Single(standard).Foreground);
    }

    [Fact]
    public void Parse_TrueColor_UsesGivenRgb()
    {
        var segments = new AnsiTextParser().Parse($"{Esc}[38;2;10;20;30mX{Esc}[48;2;1;2;3mY");

        Assert.Equal(Color.FromRgb(10, 20, 30), segments[0].Foreground);
        Assert.Equal(Color.FromRgb(1, 2, 3), segments[1].Background);
    }

    [Fact]
    public void Parse_TrueColor_ClampsOutOfRangeValues()
    {
        var segments = new AnsiTextParser().Parse($"{Esc}[38;2;999;0;0mX");

        Assert.Equal(Color.FromRgb(255, 0, 0), Assert.Single(segments).Foreground);
    }

    [Fact]
    public void Parse_CombinedCodes_AreAllApplied()
    {
        var segments = new AnsiTextParser().Parse($"{Esc}[1;32;44mX");

        var segment = Assert.Single(segments);
        Assert.True(segment.IsBold);
        Assert.Equal(Color.FromRgb(0x13, 0xA1, 0x0E), segment.Foreground);
        Assert.Equal(Color.FromRgb(0x00, 0x37, 0xDA), segment.Background);
    }

    [Fact]
    public void Parse_DefaultForegroundCode_ClearsColor()
    {
        var segments = new AnsiTextParser().Parse($"{Esc}[31mR{Esc}[39mN");

        Assert.Null(segments[1].Foreground);
    }

    [Fact]
    public void Parse_Reverse_SwapsForegroundAndBackground()
    {
        var segments = new AnsiTextParser().Parse($"{Esc}[31;7mX");

        var segment = Assert.Single(segments);
        Assert.Null(segment.Foreground);
        Assert.Equal(Color.FromRgb(0xC5, 0x0F, 0x1F), segment.Background);
    }

    // 標準出力は任意の位置で分割されて届くため、エスケープシーケンスの途中で切れても解釈できること。
    [Fact]
    public void Parse_SequenceSplitAcrossChunks_IsStillRecognized()
    {
        var parser = new AnsiTextParser();

        var first = parser.Parse($"a{Esc}[3");
        var second = parser.Parse("1mRED");

        Assert.Equal("a", JoinText(first));
        var red = Assert.Single(second);
        Assert.Equal("RED", red.Text);
        Assert.Equal(Color.FromRgb(0xC5, 0x0F, 0x1F), red.Foreground);
    }

    [Fact]
    public void Parse_EscapeAloneAtChunkEnd_IsCarriedOver()
    {
        var parser = new AnsiTextParser();

        var first = parser.Parse($"abc{Esc}");
        var second = parser.Parse("[32mG");

        Assert.Equal("abc", JoinText(first));
        Assert.Equal(Color.FromRgb(0x13, 0xA1, 0x0E), Assert.Single(second).Foreground);
        // 序数比較を指定する（既定の文化依存比較では、ESCのような制御文字が空文字と同一視され、
        // どんな文字列にも「含まれる」と判定されてしまう）。
        Assert.DoesNotContain(Esc, JoinText(first) + JoinText(second), StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_ColorPersistsAcrossChunks()
    {
        var parser = new AnsiTextParser();

        parser.Parse($"{Esc}[33m");
        var segments = parser.Parse("still yellow");

        Assert.Equal(Color.FromRgb(0xC1, 0x9C, 0x00), Assert.Single(segments).Foreground);
    }

    [Fact]
    public void Parse_OscSequence_IsSkipped()
    {
        var parser = new AnsiTextParser();

        var bel = parser.Parse($"{Esc}]0;window title\u0007Hello");
        var st = parser.Parse($"{Esc}]0;another{Esc}\\World");

        Assert.Equal("Hello", JoinText(bel));
        Assert.Equal("World", JoinText(st));
    }

    // カーソル移動・画面消去などSGR以外のCSIは、表示せず読み飛ばす。
    [Fact]
    public void Parse_NonSgrCsi_IsSkipped()
    {
        var segments = new AnsiTextParser().Parse($"A{Esc}[2K{Esc}[1;1HB");

        Assert.Equal("AB", JoinText(segments));
    }

    [Fact]
    public void Parse_CrLf_KeepsOnlyLineFeed()
    {
        var segments = new AnsiTextParser().Parse("line1\r\nline2\r\n");

        Assert.Equal("line1\nline2\n", JoinText(segments));
    }

    // ===== 行の上書き（プログレスバー等。仕様書17章） =====

    private static string Describe(IEnumerable<TerminalSegment> segments) =>
        string.Join("|", segments.Select(s => s.IsLineReset ? "<RESET>" : s.Text.Replace("\n", "\\n")));

    [Fact]
    public void Parse_LoneCarriageReturn_EmitsLineResetBetweenTexts()
    {
        var segments = new AnsiTextParser().Parse("50%\r51%");

        Assert.Equal("50%|<RESET>|51%", Describe(segments));
    }

    [Fact]
    public void Parse_CrLf_IsPlainNewline_NotAReset()
    {
        var segments = new AnsiTextParser().Parse("a\r\nb\r\n");

        Assert.DoesNotContain(segments, s => s.IsLineReset);
        Assert.Equal("a\nb\n", JoinText(segments));
    }

    // CRとLFが別のチャンクで届いても、CRLFとして扱う（リセットしない）。
    [Fact]
    public void Parse_CrAndLfInSeparateChunks_IsStillCrLf()
    {
        var parser = new AnsiTextParser();

        var first = parser.Parse("line\r");
        var second = parser.Parse("\nnext");

        Assert.DoesNotContain(first.Concat(second), s => s.IsLineReset);
        Assert.Equal("line\nnext", JoinText(first) + JoinText(second));
    }

    // CRが末尾で、次のチャンクが文字で始まる場合は、行の上書き。
    [Fact]
    public void Parse_CrAtChunkEnd_ThenText_IsLineReset()
    {
        var parser = new AnsiTextParser();

        var first = parser.Parse("10%\r");
        var second = parser.Parse("20%");

        Assert.Equal("10%", Describe(first));
        Assert.Equal("<RESET>|20%", Describe(second));
    }

    [Fact]
    public void Parse_ProgressBarSequence_ProducesOneResetPerUpdate()
    {
        var segments = new AnsiTextParser().Parse("0%\r50%\r100%\ndone\n");

        Assert.Equal("0%|<RESET>|50%|<RESET>|100%\\ndone\\n", Describe(segments));
    }

    [Fact]
    public void Parse_DoubleCr_ThenText_StillResets()
    {
        var segments = new AnsiTextParser().Parse("a\r\rb");

        Assert.Contains(segments, s => s.IsLineReset);
        Assert.Equal("ab", JoinText(segments));
    }

    [Fact]
    public void Parse_CrFollowedByColorSequence_ResetsBeforeApplyingColor()
    {
        var segments = new AnsiTextParser().Parse($"old\r{Esc}[32mnew");

        Assert.Equal("old|<RESET>|new", Describe(segments));
        Assert.Equal(Color.FromRgb(0x13, 0xA1, 0x0E), segments.Last().Foreground);
    }

    [Fact]
    public void Parse_EraseWholeLine_IsLineReset()
    {
        var segments = new AnsiTextParser().Parse($"abc{Esc}[2Kdef");

        Assert.Equal("abc|<RESET>|def", Describe(segments));
    }

    [Theory]
    [InlineData("[G")]
    [InlineData("[1G")]
    public void Parse_CursorToColumnOne_IsLineReset(string sequence)
    {
        var segments = new AnsiTextParser().Parse($"abc{Esc}{sequence}def");

        Assert.Equal("abc|<RESET>|def", Describe(segments));
    }

    // 行末までの消去は、常に行末に書き続けるモデルでは何もしなくてよい。
    [Fact]
    public void Parse_EraseToEndOfLine_IsIgnored()
    {
        var segments = new AnsiTextParser().Parse($"abc{Esc}[Kdef");

        Assert.DoesNotContain(segments, s => s.IsLineReset);
        Assert.Equal("abcdef", JoinText(segments));
    }

    [Fact]
    public void Parse_OtherCursorMovements_AreStillIgnored()
    {
        var segments = new AnsiTextParser().Parse($"a{Esc}[5Ab{Esc}[10Cc{Esc}[3;7Hd");

        Assert.DoesNotContain(segments, s => s.IsLineReset);
        Assert.Equal("abcd", JoinText(segments));
    }

    [Fact]
    public void Parse_JapaneseText_IsPreserved()
    {
        var segments = new AnsiTextParser().Parse($"{Esc}[32mこんにちは{Esc}[0m世界");

        Assert.Equal("こんにちは世界", JoinText(segments));
    }
}
