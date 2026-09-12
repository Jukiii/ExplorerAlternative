namespace ExplorerAlternative.Models;

/// <summary>仕様書15章：SSH接続先の情報。</summary>
public sealed class SshConnectionProfile
{
    public required string Host { get; init; }

    public int Port { get; init; } = 22;

    public string? UserName { get; init; }

    public string? IdentityFilePath { get; init; }
}
