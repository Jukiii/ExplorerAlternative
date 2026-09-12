namespace ExplorerAlternative.Services.Abstractions;

/// <summary>
/// SSH接続のパスワードを安全に保存する（仕様書1章「Windows Credential Manager等の
/// 安全な認証保存」、44章「秘密情報を平文保存しない」）。settings.jsonには一切含めず、
/// Windows Credential Managerにのみ保持する。
/// </summary>
public interface ISshCredentialStore
{
    void SavePassword(string profileId, string password);

    string? TryGetPassword(string profileId);

    void DeletePassword(string profileId);
}
