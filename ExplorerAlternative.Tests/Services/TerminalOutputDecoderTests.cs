using System.Text;
using ExplorerAlternative.Services;

namespace ExplorerAlternative.Tests.Services;

// 仕様書17章「文字コード」：Windows PowerShell 5.1（OEMコードページ）とgit等（UTF-8）の
// 出力が1本の標準出力に混在しても、文字化けさせずに復号する。
public sealed class TerminalOutputDecoderTests
{
    private static string Decode(TerminalOutputDecoder decoder, params byte[] bytes) => decoder.Decode(bytes, bytes.Length);

    [Fact]
    public void Decode_Ascii_ReturnsSameText()
    {
        var decoder = new TerminalOutputDecoder();

        Assert.Equal("PS C:\\> ", Decode(decoder, Encoding.ASCII.GetBytes("PS C:\\> ")));
    }

    [Fact]
    public void Decode_Utf8Japanese_IsDecodedAsUtf8()
    {
        var decoder = new TerminalOutputDecoder();

        Assert.Equal("こんにちは", Decode(decoder, Encoding.UTF8.GetBytes("こんにちは")));
    }

    // 正しいUTF-8ではないバイト列は、PowerShell自身のOEMコードページ出力とみなす。
    // （OEMコードページは環境によって異なるため、期待値は実際のOEMエンコーディングで求める。）
    [Fact]
    public void Decode_InvalidUtf8_FallsBackToOemEncoding()
    {
        var decoder = new TerminalOutputDecoder();
        byte[] bytes = { 0x82, 0xA0, 0x41 };

        var result = Decode(decoder, bytes);

        Assert.Equal(TerminalOutputDecoder.ShellInputEncoding.GetString(bytes), result);
    }

    // 日本語環境（OEM=CP932）での代表例：Shift-JIS由来の出力が文字化けしないこと。
    [Fact]
    public void Decode_ShiftJisBytes_DecodesToJapanese_OnJapaneseOem()
    {
        if (TerminalOutputDecoder.ShellInputEncoding.CodePage != 932)
        {
            // 日本語以外のOEMコードページの環境では、この検証は意味を持たない。
            return;
        }

        var decoder = new TerminalOutputDecoder();
        var sjis = TerminalOutputDecoder.ShellInputEncoding.GetBytes("日本語のフォルダ");

        Assert.Equal("日本語のフォルダ", Decode(decoder, sjis));
    }

    // 同じ出力ストリームでUTF-8とOEMが交互に届いても、チャンクごとに正しく切り替わる。
    [Fact]
    public void Decode_MixedChunks_AreDecodedIndependently()
    {
        var decoder = new TerminalOutputDecoder();
        byte[] oemBytes = { 0x82, 0xA0 };

        var utf8 = Decode(decoder, Encoding.UTF8.GetBytes("git: 完了\n"));
        var oem = Decode(decoder, oemBytes);
        var utf8Again = Decode(decoder, Encoding.UTF8.GetBytes("再びUTF-8"));

        Assert.Equal("git: 完了\n", utf8);
        Assert.Equal(TerminalOutputDecoder.ShellInputEncoding.GetString(oemBytes), oem);
        Assert.Equal("再びUTF-8", utf8Again);
    }

    // UTF-8のマルチバイト文字がチャンク境界で分割されても、文字化けせず次回へ持ち越す。
    [Fact]
    public void Decode_MultiByteCharSplitAcrossChunks_IsJoinedCorrectly()
    {
        var decoder = new TerminalOutputDecoder();
        var bytes = Encoding.UTF8.GetBytes("あ"); // E3 81 82

        var first = Decode(decoder, bytes[0], bytes[1]);
        var second = Decode(decoder, bytes[2]);

        Assert.Equal(string.Empty, first);
        Assert.Equal("あ", second);
    }

    [Fact]
    public void Decode_CompleteTextBeforeSplitChar_IsReturnedImmediately()
    {
        var decoder = new TerminalOutputDecoder();
        var bytes = Encoding.UTF8.GetBytes("abcあ");

        var first = Decode(decoder, bytes[0], bytes[1], bytes[2], bytes[3], bytes[4]);
        var second = Decode(decoder, bytes[5]);

        Assert.Equal("abc", first);
        Assert.Equal("あ", second);
    }

    [Fact]
    public void Decode_FourByteCharSplitInThreeChunks_IsJoinedCorrectly()
    {
        var decoder = new TerminalOutputDecoder();
        var bytes = Encoding.UTF8.GetBytes("😀");

        var a = Decode(decoder, bytes[0]);
        var b = Decode(decoder, bytes[1], bytes[2]);
        var c = Decode(decoder, bytes[3]);

        Assert.Equal(string.Empty, a);
        Assert.Equal(string.Empty, b);
        Assert.Equal("😀", c);
    }

    [Fact]
    public void Flush_ReturnsPendingBytes_AndClearsThem()
    {
        var decoder = new TerminalOutputDecoder();
        Decode(decoder, 0xE3, 0x81); // 「あ」の途中で終わったストリーム

        var tail = decoder.Flush();

        Assert.NotEqual(string.Empty, tail);
        Assert.Equal(string.Empty, decoder.Flush());
    }

    [Fact]
    public void Flush_WithNothingPending_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, new TerminalOutputDecoder().Flush());
    }

    [Fact]
    public void Decode_UsesOnlyTheGivenCount_NotTheWholeBuffer()
    {
        var decoder = new TerminalOutputDecoder();
        var buffer = Encoding.ASCII.GetBytes("HELLOgarbage");

        Assert.Equal("HELLO", decoder.Decode(buffer, 5));
    }

    [Fact]
    public void ShellInputEncoding_IsAvailable()
    {
        Assert.NotNull(TerminalOutputDecoder.ShellInputEncoding);
    }
}
