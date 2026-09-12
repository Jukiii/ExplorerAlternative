using System.Text;
using ExplorerAlternative.Models;
using ExplorerAlternative.Services.Abstractions;

namespace ExplorerAlternative.Services;

/// <summary>
/// 仕様書15章の実装。Windows 10/11標準のOpenSSHクライアント(ssh.exe)を、
/// 既存の統合ターミナル（<see cref="IPowerShellTerminalService"/>）経由で起動する。
/// </summary>
public sealed class SshService : ISshService
{
    public bool IsSupported => true;

    public string BuildConnectCommand(SshConnectionProfile profile)
    {
        if (string.IsNullOrWhiteSpace(profile.Host))
        {
            throw new AppOperationException("接続先ホストが指定されていません。");
        }

        var builder = new StringBuilder("ssh");

        if (profile.Port != 22)
        {
            builder.Append(" -p ").Append(profile.Port);
        }

        if (!string.IsNullOrWhiteSpace(profile.IdentityFilePath))
        {
            builder.Append(" -i \"").Append(profile.IdentityFilePath).Append('"');
        }

        var target = string.IsNullOrWhiteSpace(profile.UserName)
            ? profile.Host
            : $"{profile.UserName}@{profile.Host}";

        builder.Append(' ').Append(target);

        return builder.ToString();
    }
}
