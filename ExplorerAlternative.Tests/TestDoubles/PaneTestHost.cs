using System.Collections.ObjectModel;
using ExplorerAlternative.Models;
using ExplorerAlternative.Services;
using ExplorerAlternative.Services.Abstractions;
using ExplorerAlternative.ViewModels;

namespace ExplorerAlternative.Tests.TestDoubles;

/// <summary>設定を、ファイルに触れずにメモリ上だけで持つ偽物。</summary>
internal sealed class FakeSettingsService : ISettingsService
{
    public AppSettings Current { get; } = new();

    public void Load()
    {
    }

    public void Save()
    {
    }
}

/// <summary>ファイル操作履歴を、メモリ上だけで持つ偽物（新しい順）。</summary>
internal sealed class FakeFileOperationHistoryService : IFileOperationHistoryService
{
    private readonly List<FileOperationHistoryEntry> _entries = new();

    public IReadOnlyList<FileOperationHistoryEntry> GetAll() => _entries.ToList();

    public void Record(FileOperationHistoryEntry entry) => _entries.Insert(0, entry);

    public void Remove(FileOperationHistoryEntry entry) => _entries.Remove(entry);

    public void Clear() => _entries.Clear();
}

/// <summary>ファイル操作キューの偽物。実際にはコピー/移動せず、依頼された内容を記録する。</summary>
internal sealed class FakeFileOperationQueueService : IFileOperationQueueService
{
    public ObservableCollection<FileOperationQueueItem> Items { get; } = new();

    public Func<string, FileOperationConflictResolution>? ConflictResolver { get; set; }

    public event Action<FileOperationQueueItem>? ItemCompleted;

    /// <summary>Enqueueされた依頼（呼ばれた順）。</summary>
    public List<FileOperationQueueItem> Enqueued { get; } = new();

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

        Enqueued.Add(item);
        Items.Insert(0, item);
        return item;
    }

    public void Pause(FileOperationQueueItem item)
    {
    }

    public void Resume(FileOperationQueueItem item)
    {
    }

    public void Cancel(FileOperationQueueItem item)
    {
    }

    /// <summary>項目の処理が完了したことを、ペインへ通知する（テスト用）。</summary>
    public void Complete(FileOperationQueueItem item, IEnumerable<string>? completedSources = null)
    {
        foreach (var source in completedSources ?? item.SourcePaths)
        {
            item.CompletedSourcePaths.Add(source);
        }

        item.Status = FileOperationQueueItemStatus.Completed;
        ItemCompleted?.Invoke(item);
    }
}

/// <summary>
/// <see cref="PaneViewModel"/>を、実際のファイルシステム（一時フォルダ）の上で動かすためのテスト用ホスト。
/// ファイル・フォルダの読み取りは本物の<see cref="FileSystemService"/>、ダイアログ・キュー・履歴・設定は
/// 偽物にして、ダイアログの回答や、キューへの依頼を、テストから制御・確認できるようにする。
/// </summary>
internal sealed class PaneTestHost : IDisposable
{
    public PaneTestHost(string? initialPath = null)
    {
        Root = initialPath ?? Directory.CreateTempSubdirectory("eat_pane_").FullName;
        Dialog = StubProxy.Create<IDialogService>();
        DialogControl = StubProxy.Of(Dialog);
        Queue = new FakeFileOperationQueueService();
        History = new FakeFileOperationHistoryService();
        Settings = new FakeSettingsService();
        Undo = new UndoService();
        FileSystem = new FileSystemService();

        var versionControl = StubProxy.Create<IVersionControlService>();
        StubProxy.Of(versionControl).On("Detect", _ => VersionControlInfo.None);

        Pane = new PaneViewModel(
            FileSystem,
            Dialog,
            versionControl,
            StubProxy.Create<IExternalToolService>(),
            Settings,
            StubProxy.Create<IPatchService>(),
            StubProxy.Create<IVersionControlOperationsService>(),
            StubProxy.Create<IDiffService>(),
            StubProxy.Create<IProjectDetectionService>(),
            Undo,
            History,
            Queue,
            () => StubProxy.Create<IFolderWatcherService>(),
            Root,
            ViewMode.Detail);
    }

    public string Root { get; }

    public PaneViewModel Pane { get; }

    public IDialogService Dialog { get; }

    /// <summary>ダイアログの偽物の制御用（<see cref="StubProxy.On"/>で回答を差し替え、呼び出しを確認する）。</summary>
    public StubProxy DialogControl { get; }

    public FakeFileOperationQueueService Queue { get; }

    public FakeFileOperationHistoryService History { get; }

    public FakeSettingsService Settings { get; }

    public UndoService Undo { get; }

    public FileSystemService FileSystem { get; }

    /// <summary>ルート直下に空のファイルを作る。</summary>
    public string CreateFile(string name, string content = "x")
    {
        var path = Path.Combine(Root, name);
        File.WriteAllText(path, content);
        return path;
    }

    /// <summary>ルート直下にフォルダを作る。</summary>
    public string CreateFolder(string name) => Directory.CreateDirectory(Path.Combine(Root, name)).FullName;

    /// <summary>一覧に表示されている項目を、名前で選択状態にする（既存の選択は置き換える）。</summary>
    public void Select(params string[] names)
    {
        Pane.SelectedNodes.Clear();

        foreach (var name in names)
        {
            var node = Pane.VisibleNodes.Single(n => n.Name == name);
            Pane.SelectedNodes.Add(node);
        }
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
