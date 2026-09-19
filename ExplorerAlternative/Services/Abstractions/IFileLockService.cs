using ExplorerAlternative.Models;

namespace ExplorerAlternative.Services.Abstractions;

/// <summary>
/// ファイルを使用しているプロセスの調べ方（仕様書52章「ファイルロック」）。
/// 削除・移動等が失敗した際に、原因の手掛かりとして使用中のプロセスを表示するために使う。
/// </summary>
public interface IFileLockService
{
    /// <summary>
    /// 指定したファイル（フォルダを指定した場合はその配下のファイル）を使用しているプロセスを返す。
    /// 調べられなかった場合や、使用中のプロセスが無い場合は空のリストを返す（例外は投げない）。
    /// </summary>
    IReadOnlyList<LockingProcess> GetLockingProcesses(IEnumerable<string> paths);

    /// <summary>
    /// ユーザー向けの表示文（仕様書52章の書式）を作る。使用中のプロセスが無い場合はnullを返す。
    /// </summary>
    string? Describe(IEnumerable<string> paths);
}
