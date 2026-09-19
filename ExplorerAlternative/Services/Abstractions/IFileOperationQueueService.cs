using System.Collections.ObjectModel;
using ExplorerAlternative.Models;

namespace ExplorerAlternative.Services.Abstractions;

/// <summary>
/// 仕様書26章「ファイル操作キュー」：大量コピー/移動をUIをブロックせずに実行し、
/// 一時停止・再開・キャンセル、複数操作のキュー管理、同名ファイル競合の解決に対応する。
/// </summary>
public interface IFileOperationQueueService
{
    /// <summary>UIスレッドから購読するキュー項目一覧（新しい順）。</summary>
    ObservableCollection<FileOperationQueueItem> Items { get; }

    /// <summary>同名ファイル競合時に呼び出す解決コールバック。UIスレッドへ委譲して結果を待つ。
    /// MainWindowViewModel起動時に一度だけ設定する。</summary>
    Func<string, FileOperationConflictResolution>? ConflictResolver { get; set; }

    /// <summary>1件のキュー項目が完了（成功・キャンセル・失敗いずれか）した際に、UIスレッドで発火する。
    /// Undo記録・ファイル操作履歴記録・一覧の再読み込みに使う。</summary>
    event Action<FileOperationQueueItem>? ItemCompleted;

    FileOperationQueueItem Enqueue(string kind, IReadOnlyList<string> sourcePaths, string destinationFolder, bool isMove);

    void Pause(FileOperationQueueItem item);

    void Resume(FileOperationQueueItem item);

    void Cancel(FileOperationQueueItem item);
}
