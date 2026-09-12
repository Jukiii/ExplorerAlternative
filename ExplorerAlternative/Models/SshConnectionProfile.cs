namespace ExplorerAlternative.Models;

/// <summary>
/// 仕様書44章：SSH接続先の登録情報。
/// パスワード・パスフレーズはここに一切保持しない（平文保存の禁止）。
/// 接続実行は常にOpenSSHクライアント（ssh.exe）へ委譲し、鍵のパスフレーズ入力は
/// 統合ターミナル上でOpenSSH自身の対話プロンプト（またはssh-agent）に任せる。
/// </summary>
public sealed class SshConnectionProfile
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");

    public required string DisplayName { get; init; }

    public required string Host { get; init; }

    public int Port { get; init; } = 22;

    public string? UserName { get; init; }

    public string? IdentityFilePath { get; init; }
}
