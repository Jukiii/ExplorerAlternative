using ExplorerAlternative.Models;

namespace ExplorerAlternative.Services.Abstractions;

/// <summary>
/// 仕様書15章：SSH接続機能。将来のリモートファイル操作・ターミナル操作への拡張基盤として、
/// Phase 2では「接続コマンドを組み立て、統合ターミナル(9章)上でSSHセッションを確立する」
/// ところまでを実装する。
/// </summary>
public interface ISshService
{
    bool IsSupported { get; }

    /// <summary>指定プロファイルへ接続するための、統合ターミナルへ送信するコマンド文字列を組み立てる。</summary>
    string BuildConnectCommand(SshConnectionProfile profile);
}
