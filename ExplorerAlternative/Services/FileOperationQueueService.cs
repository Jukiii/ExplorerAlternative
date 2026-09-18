using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using ExplorerAlternative.Models;
using ExplorerAlternative.Services.Abstractions;

namespace ExplorerAlternative.Services;

/// <summary>
/// 仕様書26章「ファイル操作キュー」の実装。1本のバックグラウンドスレッドで項目を順番に
/// 処理する（同時並列実行はしない。ディスクI/Oの競合を避けるため）。進捗の粒度は
/// キューに入れたトップレベルの項目単位（フォルダ内部のファイル単位ではない）とする。
/// 一時停止・キャンセルも項目の切れ目でのみ反映される（1つの巨大なフォルダ/ファイルの
/// コピー中には効かない）。UIへの通知はすべてDispatcherへ委譲する。
/// </summary>
public sealed class FileOperationQueueService : IFileOperationQueueService
{
    private sealed class RunState
    {
        public ManualResetEventSlim PauseGate { get; } = new(initialState: true);
        public CancellationTokenSource Cts { get; } = new();
        public FileOperationConflictResolution? ResolutionForAll { get; set; }
    }

    private readonly BlockingCollection<FileOperationQueueItem> _pendingQueue = new();
    private readonly ConcurrentDictionary<Guid, RunState> _runStates = new();

    public FileOperationQueueService()
    {
        var worker = new Thread(ProcessQueue) { IsBackground = true, Name = "FileOperationQueue" };
        worker.Start();
    }

    public ObservableCollection<FileOperationQueueItem> Items { get; } = new();

    public Func<string, FileOperationConflictResolution>? ConflictResolver { get; set; }

    public event Action<FileOperationQueueItem>? ItemCompleted;

    public FileOperationQueueItem Enqueue(string kind, IReadOnlyList<string> sourcePaths, string destinationFolder, bool isMove)
    {
        var item = new FileOperationQueueItem
        {
            Id = Guid.NewGuid(),
            Kind = kind,
            SourcePaths = sourcePaths,
            DestinationFolder = destinationFolder,
            IsMove = isMove,
            TotalCount = sourcePaths.Count
        };

        _runStates[item.Id] = new RunState();
        RunOnUi(() => Items.Insert(0, item));
        _pendingQueue.Add(item);
        return item;
    }

    public void Pause(FileOperationQueueItem item)
    {
        if (_runStates.TryGetValue(item.Id, out var state))
        {
            state.PauseGate.Reset();
            item.Status = FileOperationQueueItemStatus.Paused;
        }
    }

    public void Resume(FileOperationQueueItem item)
    {
        if (_runStates.TryGetValue(item.Id, out var state))
        {
            state.PauseGate.Set();
            if (item.Status == FileOperationQueueItemStatus.Paused)
            {
                item.Status = FileOperationQueueItemStatus.Running;
            }
        }
    }

    public void Cancel(FileOperationQueueItem item)
    {
        if (_runStates.TryGetValue(item.Id, out var state))
        {
            state.Cts.Cancel();
            state.PauseGate.Set();
        }
    }

    private void ProcessQueue()
    {
        foreach (var item in _pendingQueue.GetConsumingEnumerable())
        {
            if (!_runStates.TryGetValue(item.Id, out var state))
            {
                continue;
            }

            ProcessItem(item, state);
            _runStates.TryRemove(item.Id, out _);
        }
    }

    private void ProcessItem(FileOperationQueueItem item, RunState state)
    {
        RunOnUi(() => item.Status = FileOperationQueueItemStatus.Running);

        try
        {
            for (var i = 0; i < item.SourcePaths.Count; i++)
            {
                state.PauseGate.Wait(state.Cts.Token);
                state.Cts.Token.ThrowIfCancellationRequested();

                var source = item.SourcePaths[i];
                ProcessSingleSource(item, state, source);

                var processed = i + 1;
                RunOnUi(() =>
                {
                    item.ProcessedCount = processed;
                    item.ProgressPercent = item.TotalCount == 0 ? 100 : processed * 100.0 / item.TotalCount;
                });
            }

            RunOnUi(() => item.Status = FileOperationQueueItemStatus.Completed);
        }
        catch (OperationCanceledException)
        {
            RunOnUi(() => item.Status = FileOperationQueueItemStatus.Cancelled);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            RunOnUi(() =>
            {
                item.ErrorMessage = ex.Message;
                item.Status = FileOperationQueueItemStatus.Failed;
            });
        }
        finally
        {
            RunOnUi(() => ItemCompleted?.Invoke(item));
        }
    }

    private void ProcessSingleSource(FileOperationQueueItem item, RunState state, string source)
    {
        var isDirectory = Directory.Exists(source);
        if (!isDirectory && !File.Exists(source))
        {
            return;
        }

        RunOnUi(() => item.CurrentFileName = Path.GetFileName(source));

        var name = Path.GetFileName(source);
        var destination = Path.Combine(item.DestinationFolder, name);

        if (Directory.Exists(destination) || File.Exists(destination))
        {
            var resolution = state.ResolutionForAll ?? ResolveConflict(name);

            switch (resolution)
            {
                case FileOperationConflictResolution.Cancel:
                    state.Cts.Cancel();
                    state.Cts.Token.ThrowIfCancellationRequested();
                    return;

                case FileOperationConflictResolution.SkipAll:
                    state.ResolutionForAll = FileOperationConflictResolution.Skip;
                    return;

                case FileOperationConflictResolution.Skip:
                    return;

                case FileOperationConflictResolution.OverwriteAll:
                    state.ResolutionForAll = FileOperationConflictResolution.Overwrite;
                    DeleteExisting(destination);
                    break;

                case FileOperationConflictResolution.Overwrite:
                    DeleteExisting(destination);
                    break;

                case FileOperationConflictResolution.Rename:
                    destination = MakeUniqueName(item.DestinationFolder, name);
                    break;
            }
        }

        if (isDirectory)
        {
            if (item.IsMove && IsSameDrive(source, destination))
            {
                Directory.Move(source, destination);
            }
            else
            {
                CopyDirectoryRecursive(source, destination);
                if (item.IsMove)
                {
                    Directory.Delete(source, recursive: true);
                }
            }
        }
        else
        {
            if (item.IsMove && IsSameDrive(source, destination))
            {
                File.Move(source, destination);
            }
            else
            {
                File.Copy(source, destination, overwrite: false);
                if (item.IsMove)
                {
                    File.Delete(source);
                }
            }
        }

        item.CompletedSourcePaths.Add(source);
    }

    private FileOperationConflictResolution ResolveConflict(string name)
    {
        var resolver = ConflictResolver;
        if (resolver is null)
        {
            return FileOperationConflictResolution.Skip;
        }

        return Application.Current?.Dispatcher.Invoke(() => resolver(name)) ?? FileOperationConflictResolution.Skip;
    }

    private static void DeleteExisting(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
        else if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static string MakeUniqueName(string destinationFolder, string name)
    {
        var extension = Path.GetExtension(name);
        var baseName = Path.GetFileNameWithoutExtension(name);
        var counter = 2;
        string candidate;

        do
        {
            candidate = Path.Combine(destinationFolder, $"{baseName} ({counter}){extension}");
            counter++;
        } while (Directory.Exists(candidate) || File.Exists(candidate));

        return candidate;
    }

    private static bool IsSameDrive(string a, string b)
    {
        var rootA = Path.GetPathRoot(a);
        var rootB = Path.GetPathRoot(b);
        return string.Equals(rootA, rootB, StringComparison.OrdinalIgnoreCase);
    }

    private static void CopyDirectoryRecursive(string source, string destination)
    {
        Directory.CreateDirectory(destination);

        foreach (var directory in Directory.GetDirectories(source))
        {
            CopyDirectoryRecursive(directory, Path.Combine(destination, Path.GetFileName(directory)));
        }

        foreach (var file in Directory.GetFiles(source))
        {
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: false);
        }
    }

    private static void RunOnUi(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            dispatcher.Invoke(action);
        }
    }
}
