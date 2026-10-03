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

    /// <summary>Saveが呼ばれた回数（設定が保存されたことの確認用）。</summary>
    public int SaveCount { get; private set; }

    public void Load()
    {
    }

    /// <summary>設定すると、Saveがこの例外を投げる（保存に失敗する状況の再現用）。</summary>
    public Exception? SaveException { get; set; }

    public void Save()
    {
        SaveCount++;

        if (SaveException is not null)
        {
            throw SaveException;
        }
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

/// <summary>ファイルのクリップボードを、メモリ上だけで持つ偽物（利用者の実際のクリップボードを書き換えない）。</summary>
internal sealed class FakeFileClipboard : IFileClipboard
{
    public FileClipboardContent? Content { get; set; }

    /// <summary>設定すると、読み書きがこの例外を投げる（別のアプリがクリップボードを使用中の状況の再現用）。</summary>
    public Exception? Failure { get; set; }

    public int SetCount { get; private set; }

    public void SetFiles(IReadOnlyList<string> paths, bool isCut)
    {
        if (Failure is not null)
        {
            throw Failure;
        }

        SetCount++;
        Content = new FileClipboardContent(paths.ToList(), isCut);
    }

    public FileClipboardContent? GetFiles()
    {
        if (Failure is not null)
        {
            throw Failure;
        }

        return Content;
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

        VersionControl = StubProxy.Create<IVersionControlService>();
        VersionControlControl = StubProxy.Of(VersionControl);
        VersionControlControl.On("Detect", _ => VersionControlInfo.None);

        ExternalTools = StubProxy.Create<IExternalToolService>();
        ExternalToolsControl = StubProxy.Of(ExternalTools);
        SystemOpened = new List<string>();
        Clipboard = new FakeFileClipboard();

        Pane = new PaneViewModel(
            FileSystem,
            Dialog,
            VersionControl,
            ExternalTools,
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
            ViewMode.Detail)
        {
            // 実際にWindowsの既定のアプリを起動せず、開こうとしたファイルを記録する。
            SystemOpenFile = SystemOpened.Add,
            // 実際のクリップボード（利用者のもの）を書き換えない。
            FileClipboard = Clipboard
        };
    }

    public string Root { get; }

    /// <summary>ファイルのコピー/切り取り用の、偽のクリップボード。</summary>
    public FakeFileClipboard Clipboard { get; }

    public PaneViewModel Pane { get; }

    public IDialogService Dialog { get; }

    /// <summary>ダイアログの偽物の制御用（<see cref="StubProxy.On"/>で回答を差し替え、呼び出しを確認する）。</summary>
    public StubProxy DialogControl { get; }

    /// <summary>Git/SVN判定の偽物。既定では、どこでも「管理なし」を返す（<see cref="VersionControlControl"/>で差し替える）。</summary>
    public IVersionControlService VersionControl { get; }

    public StubProxy VersionControlControl { get; }

    /// <summary>外部ツール（アプリ）起動の偽物。起動の依頼は<see cref="ExternalToolsControl"/>で確認する（Runの引数）。</summary>
    public IExternalToolService ExternalTools { get; }

    public StubProxy ExternalToolsControl { get; }

    /// <summary>Windowsの既定のアプリで開こうとしたファイル（実際には起動しない）。</summary>
    public List<string> SystemOpened { get; }

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
        // 画面の選択操作と同じ経路（SelectedNodes と PrimarySelectedNode の両方が更新される）を使う。
        Pane.UpdateSelection(names.Select(name => Pane.VisibleNodes.Single(n => n.Name == name)).ToList());
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
