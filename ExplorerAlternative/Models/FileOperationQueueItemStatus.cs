namespace ExplorerAlternative.Models;

/// <summary>仕様書26章「ファイル操作キュー」の1項目の状態。</summary>
public enum FileOperationQueueItemStatus
{
    Waiting,
    Running,
    Paused,
    Completed,
    Cancelled,
    Failed
}
