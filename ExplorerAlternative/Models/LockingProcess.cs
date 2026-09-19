namespace ExplorerAlternative.Models;

/// <summary>
/// ファイルを使用している（ロックしている）プロセス（仕様書52章）。
/// </summary>
/// <param name="ProcessId">プロセスID。</param>
/// <param name="ExecutableName">実行ファイル名（例：chrome.exe）。取得できない場合は空文字。</param>
/// <param name="ApplicationName">Windowsが把握しているアプリ名（例：Google Chrome）。実行ファイル名と同じ場合や不明な場合は空文字。</param>
public sealed record LockingProcess(int ProcessId, string ExecutableName, string ApplicationName);
