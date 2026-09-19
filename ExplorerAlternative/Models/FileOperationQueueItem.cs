using ExplorerAlternative.Mvvm;

namespace ExplorerAlternative.Models;

/// <summary>仕様書26章「ファイル操作キュー」の1項目（1回の移動/コピー操作）。</summary>
public sealed class FileOperationQueueItem : ObservableObject
{
    private FileOperationQueueItemStatus _status = FileOperationQueueItemStatus.Waiting;
    private double _progressPercent;
    private string _currentFileName = string.Empty;
    private int _processedCount;
    private int _totalCount;
    private string? _errorMessage;

    public required Guid Id { get; init; }

    /// <summary>「移動」または「コピー」。</summary>
    public required string Kind { get; init; }

    public required IReadOnlyList<string> SourcePaths { get; init; }

    public required string DestinationFolder { get; init; }

    public required bool IsMove { get; init; }

    public string Summary => SourcePaths.Count == 1
        ? System.IO.Path.GetFileName(SourcePaths[0])
        : $"{SourcePaths.Count}件";

    public FileOperationQueueItemStatus Status
    {
        get => _status;
        set
        {
            if (SetProperty(ref _status, value))
            {
                OnPropertyChanged(nameof(StatusDisplay));
                OnPropertyChanged(nameof(CanPause));
                OnPropertyChanged(nameof(CanResume));
                OnPropertyChanged(nameof(CanCancel));
            }
        }
    }

    public string StatusDisplay => Status switch
    {
        FileOperationQueueItemStatus.Waiting => "待機中",
        FileOperationQueueItemStatus.Running => "実行中",
        FileOperationQueueItemStatus.Paused => "一時停止",
        FileOperationQueueItemStatus.Completed => "完了",
        FileOperationQueueItemStatus.Cancelled => "キャンセル",
        FileOperationQueueItemStatus.Failed => "失敗",
        _ => string.Empty
    };

    public bool CanPause => Status == FileOperationQueueItemStatus.Running;

    public bool CanResume => Status == FileOperationQueueItemStatus.Paused;

    public bool CanCancel => Status is FileOperationQueueItemStatus.Waiting or FileOperationQueueItemStatus.Running or FileOperationQueueItemStatus.Paused;

    public double ProgressPercent
    {
        get => _progressPercent;
        set => SetProperty(ref _progressPercent, value);
    }

    public string CurrentFileName
    {
        get => _currentFileName;
        set => SetProperty(ref _currentFileName, value);
    }

    public int ProcessedCount
    {
        get => _processedCount;
        set => SetProperty(ref _processedCount, value);
    }

    public int TotalCount
    {
        get => _totalCount;
        set => SetProperty(ref _totalCount, value);
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        set => SetProperty(ref _errorMessage, value);
    }

    /// <summary>実際に処理された（コピー/移動が完了した）ファイルのフルパス一覧。Undo記録に使う。</summary>
    public List<string> CompletedSourcePaths { get; } = new();
}
