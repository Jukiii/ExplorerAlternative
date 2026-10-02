namespace ExplorerAlternative.Services;

/// <summary>
/// SFTP接続に使う秘密鍵が、パスフレーズで保護されていて、パスフレーズが無い（または違う）ことを表す。
/// 呼び出し側（ViewModel）は、利用者にパスフレーズを入力してもらい、接続をやり直す。
/// パスフレーズは、どこにも保存しない（接続のあいだ、メモリに持つだけ）。
/// </summary>
public sealed class SftpKeyPassphraseException : Exception
{
    public SftpKeyPassphraseException(string keyFileName, bool wasWrong)
        : base(wasWrong
            ? $"秘密鍵「{keyFileName}」のパスフレーズが違います。"
            : $"秘密鍵「{keyFileName}」はパスフレーズで保護されています。")
    {
        KeyFileName = keyFileName;
        WasWrong = wasWrong;
    }

    public string KeyFileName { get; }

    /// <summary>パスフレーズを指定したが、違っていた場合はtrue。未指定だった場合はfalse。</summary>
    public bool WasWrong { get; }
}
