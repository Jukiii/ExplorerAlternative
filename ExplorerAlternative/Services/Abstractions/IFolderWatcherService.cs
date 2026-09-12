namespace ExplorerAlternative.Services.Abstractions;

/// <summary>
/// 仕様書64章「FileSystemWatcher」：現在表示中のフォルダに対する外部からの変更
/// （他アプリ・ターミナル・Git操作等によるファイル作成/削除/変更/リネーム）を監視する。
/// イベントはFileSystemWatcherの背景スレッドから発火するため、購読側でUIスレッドへの
/// マーシャリングが必要な点に注意。
/// </summary>
public interface IFolderWatcherService : IDisposable
{
    /// <summary>監視対象フォルダを切り替える。nullまたは存在しないフォルダを指定すると監視を止める。</summary>
    void SetPath(string? path);

    event Action? Changed;
}
