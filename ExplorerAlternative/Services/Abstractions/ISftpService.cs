using ExplorerAlternative.Models;

namespace ExplorerAlternative.Services.Abstractions;

/// <summary>仕様書44章「SFTPリモートファイル操作」：SSH接続プロファイルからSFTPセッションを確立する。</summary>
public interface ISftpService
{
    /// <summary>
    /// 秘密鍵（パスフレーズなしのもの）が指定されていればまず鍵認証を試み、
    /// 未指定または失敗した場合はパスワード認証（Windows Credential Managerに
    /// 保存されているもの）を試みる。どちらも利用できない場合は例外を投げる。
    /// 秘密鍵がパスフレーズで保護されている場合は、<paramref name="keyPassphrase"/>で開く。
    /// 無い・違う場合は<see cref="SftpKeyPassphraseException"/>を投げる（パスフレーズは保存しない）。
    /// </summary>
    ISftpSession Connect(SshConnectionProfile profile, string? password, string? keyPassphrase = null);
}
