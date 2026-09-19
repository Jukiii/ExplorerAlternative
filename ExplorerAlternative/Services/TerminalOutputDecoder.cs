using System.Runtime.InteropServices;
using System.Text;

namespace ExplorerAlternative.Services;

/// <summary>
/// ターミナル出力のバイト列を文字列へ復号する（仕様書17章）。
///
/// Windows PowerShell 5.1 は自身のメッセージ（日本語ファイル名・エラー等）をOEM
/// コードページ（日本語環境ではCP932）で書き出す一方、git等の開発ツールはUTF-8で
/// 書き出すため、1本の標準出力に2種類の文字コードが混在する。どちらか片方に固定すると
/// もう片方が必ず文字化けするため、チャンクごとに「正しいUTF-8として解釈できるか」を
/// 判定して復号方式を切り替える。
///
/// UTF-8のマルチバイト文字がチャンク境界で分割された場合に備え、末尾の不完全な
/// バイト列は次回の呼び出しへ持ち越す。
/// </summary>
public sealed class TerminalOutputDecoder
{
    [DllImport("kernel32.dll")]
    private static extern uint GetOEMCP();

    private static readonly Encoding Utf8Strict = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private static readonly Encoding OemEncoding = CreateOemEncoding();

    private byte[] _pending = Array.Empty<byte>();

    /// <summary>シェルの標準入力へ書き込む際に使う文字コード。PowerShell 5.1は
    /// リダイレクトされた標準入力をOEMコードページとして読むため、これに合わせないと
    /// 日本語を含むコマンド（日本語ファイル名のドラッグ&ドロップ等）が化ける。</summary>
    public static Encoding ShellInputEncoding => OemEncoding;

    private static Encoding CreateOemEncoding()
    {
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding((int)GetOEMCP());
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PlatformNotSupportedException)
        {
            // コードページが取得できない環境ではUTF-8にフォールバックする。
            return new UTF8Encoding(false);
        }
    }

    public string Decode(byte[] buffer, int count)
    {
        var bytes = Combine(_pending, buffer, count);
        _pending = Array.Empty<byte>();

        // 末尾がUTF-8のマルチバイト文字の途中なら、その分を次回へ持ち越す。
        var usableLength = bytes.Length - CountTrailingIncompleteUtf8Bytes(bytes);
        if (usableLength < bytes.Length)
        {
            _pending = bytes[usableLength..];
            bytes = bytes[..usableLength];
        }

        if (bytes.Length == 0)
        {
            return string.Empty;
        }

        try
        {
            return Utf8Strict.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            // 正しいUTF-8ではない＝PowerShell自身のOEMコードページ出力とみなす。
            return OemEncoding.GetString(bytes);
        }
    }

    /// <summary>ストリーム終端で、持ち越していた不完全なバイト列を吐き出す。</summary>
    public string Flush()
    {
        if (_pending.Length == 0)
        {
            return string.Empty;
        }

        var bytes = _pending;
        _pending = Array.Empty<byte>();
        return OemEncoding.GetString(bytes);
    }

    private static byte[] Combine(byte[] pending, byte[] buffer, int count)
    {
        if (pending.Length == 0)
        {
            return buffer[..count];
        }

        var combined = new byte[pending.Length + count];
        pending.CopyTo(combined, 0);
        Array.Copy(buffer, 0, combined, pending.Length, count);
        return combined;
    }

    // 末尾にあるUTF-8マルチバイト文字の「途中まで」のバイト数を返す。
    private static int CountTrailingIncompleteUtf8Bytes(byte[] bytes)
    {
        // 継続バイト(10xxxxxx)を遡り、先頭バイトを見つけて必要長と比較する。
        var index = bytes.Length - 1;
        var continuationCount = 0;

        while (index >= 0 && (bytes[index] & 0b1100_0000) == 0b1000_0000)
        {
            continuationCount++;
            index--;

            // UTF-8の最大長は4バイトなので、それ以上遡る必要はない。
            if (continuationCount >= 3)
            {
                break;
            }
        }

        if (index < 0)
        {
            return 0;
        }

        var lead = bytes[index];
        var expectedLength = lead switch
        {
            < 0x80 => 1,
            >= 0xC0 and < 0xE0 => 2,
            >= 0xE0 and < 0xF0 => 3,
            >= 0xF0 and < 0xF8 => 4,
            _ => 0 // 継続バイト単独など、UTF-8として不正
        };

        if (expectedLength <= 1)
        {
            return 0;
        }

        var actualLength = continuationCount + 1;
        return actualLength < expectedLength ? actualLength : 0;
    }
}
