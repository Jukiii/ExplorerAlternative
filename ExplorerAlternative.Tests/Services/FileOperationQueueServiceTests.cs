using ExplorerAlternative.Models;
using ExplorerAlternative.Services;

namespace ExplorerAlternative.Tests.Services;

// 仕様書26章「ファイル操作キュー」：コピー/移動の実行、同名の競合の解決、一時停止・キャンセル、失敗の通知。
// 実際の一時フォルダで、バックグラウンドの処理を最後まで待って確認する。
public sealed class FileOperationQueueServiceTests : IDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    private readonly string _root = Directory.CreateTempSubdirectory("eat_queue_").FullName;
    private readonly FileOperationQueueService _sut = new();

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private string File_(string relative, string content = "x")
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    private string Dir_(string relative) => Directory.CreateDirectory(Path.Combine(_root, relative)).FullName;

    /// <summary>キューに入れ、その項目が終わる（完了・キャンセル・失敗）のを待つ。</summary>
    private FileOperationQueueItem Run(bool isMove, string destination, params string[] sources)
    {
        var done = new TaskCompletionSource<FileOperationQueueItem>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Handler(FileOperationQueueItem item) => done.TrySetResult(item);
        _sut.ItemCompleted += Handler;

        try
        {
            var queued = _sut.Enqueue(isMove ? "移動" : "コピー", sources, destination, isMove);
            Assert.True(done.Task.Wait(Timeout), "キューの項目が時間内に終わりませんでした。");
            Assert.Same(queued, done.Task.Result);
            return queued;
        }
        finally
        {
            _sut.ItemCompleted -= Handler;
        }
    }

    // ===== 基本 =====

    [Fact]
    public void Copy_File_KeepsTheSource_AndRecordsCompletion()
    {
        var src = File_("s/a.txt", "data");
        var dest = Dir_("d");

        var item = Run(false, dest, src);

        Assert.Equal(FileOperationQueueItemStatus.Completed, item.Status);
        Assert.Equal("data", File.ReadAllText(Path.Combine(dest, "a.txt")));
        Assert.True(File.Exists(src));
        Assert.Equal(new[] { src }, item.CompletedSourcePaths);
        Assert.Equal(100, item.ProgressPercent);
        Assert.Equal(1, item.ProcessedCount);
    }

    [Fact]
    public void Move_File_RemovesTheSource()
    {
        var src = File_("s/a.txt", "data");
        var dest = Dir_("d");

        var item = Run(true, dest, src);

        Assert.Equal(FileOperationQueueItemStatus.Completed, item.Status);
        Assert.False(File.Exists(src));
        Assert.Equal("data", File.ReadAllText(Path.Combine(dest, "a.txt")));
    }

    [Fact]
    public void Copy_AndMove_Folder_HandleTheWholeTree()
    {
        File_("tree/top.txt", "1");
        File_("tree/in/deep.txt", "2");
        var copyDest = Dir_("c");
        var moveDest = Dir_("m");

        Run(false, copyDest, Path.Combine(_root, "tree"));
        var moved = Run(true, moveDest, Path.Combine(_root, "tree"));

        Assert.Equal(FileOperationQueueItemStatus.Completed, moved.Status);
        Assert.Equal("2", File.ReadAllText(Path.Combine(copyDest, "tree", "in", "deep.txt")));
        Assert.Equal("1", File.ReadAllText(Path.Combine(moveDest, "tree", "top.txt")));
        Assert.False(Directory.Exists(Path.Combine(_root, "tree")));
    }

    [Fact]
    public void Several_Sources_AllProcessed_WithSummaryAndCount()
    {
        var a = File_("s/a.txt");
        var b = File_("s/b.txt");
        var dest = Dir_("d");

        var item = Run(false, dest, a, b);

        Assert.Equal(2, item.TotalCount);
        Assert.Equal(2, item.ProcessedCount);
        Assert.Equal("2件", item.Summary);
        Assert.True(File.Exists(Path.Combine(dest, "b.txt")));
    }

    [Fact]
    public void ASourceThatNoLongerExists_IsSkipped_NotAnError()
    {
        var real = File_("s/a.txt");
        var dest = Dir_("d");

        var item = Run(false, dest, Path.Combine(_root, "s", "gone.txt"), real);

        Assert.Equal(FileOperationQueueItemStatus.Completed, item.Status);
        Assert.Equal(new[] { real }, item.CompletedSourcePaths);
    }

    [Fact]
    public void NewItems_AreInsertedAtTheTopOfTheList()
    {
        var a = File_("s/a.txt");
        var b = File_("s/b.txt");
        var dest = Dir_("d");

        var first = Run(false, dest, a);
        var second = Run(false, dest, b);

        Assert.Same(second, _sut.Items[0]);
        Assert.Same(first, _sut.Items[1]);
    }

    [Fact]
    public void Items_AreProcessedInTheOrderQueued()
    {
        var dest = Dir_("d");
        var order = new List<string>();
        var done = new CountdownEvent(3);
        _sut.ItemCompleted += item =>
        {
            lock (order)
            {
                order.Add(Path.GetFileName(item.SourcePaths[0]));
            }

            done.Signal();
        };

        foreach (var name in new[] { "1.txt", "2.txt", "3.txt" })
        {
            _sut.Enqueue("コピー", new[] { File_("s/" + name) }, dest, false);
        }

        Assert.True(done.Wait(Timeout));
        Assert.Equal(new[] { "1.txt", "2.txt", "3.txt" }, order);
    }

    // ===== 同名の競合 =====

    private (string Src, string Dest) Conflict(string srcContent = "new", string destContent = "old")
    {
        var src = File_("s/a.txt", srcContent);
        var dest = Dir_("d");
        File.WriteAllText(Path.Combine(dest, "a.txt"), destContent);
        return (src, dest);
    }

    [Fact]
    public void Conflict_WithoutAResolver_SkipsAndKeepsTheExistingFile()
    {
        var (src, dest) = Conflict();

        var item = Run(false, dest, src);

        Assert.Equal(FileOperationQueueItemStatus.Completed, item.Status);
        Assert.Equal("old", File.ReadAllText(Path.Combine(dest, "a.txt")));
        Assert.Empty(item.CompletedSourcePaths);
    }

    [Fact]
    public void Conflict_Overwrite_ReplacesTheFile_AndTheResolverGetsTheName()
    {
        var (src, dest) = Conflict();
        string? asked = null;
        _sut.ConflictResolver = name =>
        {
            asked = name;
            return FileOperationConflictResolution.Overwrite;
        };

        Run(false, dest, src);

        Assert.Equal("a.txt", asked);
        Assert.Equal("new", File.ReadAllText(Path.Combine(dest, "a.txt")));
    }

    [Fact]
    public void Conflict_Skip_KeepsTheExistingFile()
    {
        var (src, dest) = Conflict();
        _sut.ConflictResolver = _ => FileOperationConflictResolution.Skip;

        Run(false, dest, src);

        Assert.Equal("old", File.ReadAllText(Path.Combine(dest, "a.txt")));
    }

    [Fact]
    public void Conflict_Rename_KeepsBothFiles()
    {
        var (src, dest) = Conflict();
        _sut.ConflictResolver = _ => FileOperationConflictResolution.Rename;

        var item = Run(false, dest, src);

        Assert.Equal("old", File.ReadAllText(Path.Combine(dest, "a.txt")));
        Assert.Equal("new", File.ReadAllText(Path.Combine(dest, "a (2).txt")));
        Assert.Equal(FileOperationQueueItemStatus.Completed, item.Status);
    }

    [Fact]
    public void Conflict_Cancel_StopsTheWholeItem_LeavingLaterSourcesUntouched()
    {
        var (first, dest) = Conflict();
        var second = File_("s/b.txt");
        _sut.ConflictResolver = _ => FileOperationConflictResolution.Cancel;

        var item = Run(false, dest, first, second);

        Assert.Equal(FileOperationQueueItemStatus.Cancelled, item.Status);
        Assert.False(File.Exists(Path.Combine(dest, "b.txt")));
    }

    [Fact]
    public void Conflict_OverwriteAll_AsksOnce_AndAppliesToTheRest()
    {
        var dest = Dir_("d");
        var sources = new[] { "1.txt", "2.txt", "3.txt" }.Select(n =>
        {
            File.WriteAllText(Path.Combine(dest, n), "old");
            return File_("s/" + n, "new");
        }).ToArray();
        var asked = 0;
        _sut.ConflictResolver = _ =>
        {
            asked++;
            return FileOperationConflictResolution.OverwriteAll;
        };

        Run(false, dest, sources);

        Assert.Equal(1, asked);
        Assert.All(new[] { "1.txt", "2.txt", "3.txt" }, n => Assert.Equal("new", File.ReadAllText(Path.Combine(dest, n))));
    }

    [Fact]
    public void Conflict_SkipAll_AsksOnce_AndSkipsTheRest()
    {
        var dest = Dir_("d");
        var sources = new[] { "1.txt", "2.txt" }.Select(n =>
        {
            File.WriteAllText(Path.Combine(dest, n), "old");
            return File_("s/" + n, "new");
        }).ToArray();
        var asked = 0;
        _sut.ConflictResolver = _ =>
        {
            asked++;
            return FileOperationConflictResolution.SkipAll;
        };

        Run(false, dest, sources);

        Assert.Equal(1, asked);
        Assert.All(new[] { "1.txt", "2.txt" }, n => Assert.Equal("old", File.ReadAllText(Path.Combine(dest, n))));
    }

    [Fact]
    public void Conflict_OverwriteOfAFolder_RemovesTheOldContents()
    {
        File_("s/dir/new.txt", "n");
        File_("d/dir/stale.txt", "s");
        _sut.ConflictResolver = _ => FileOperationConflictResolution.Overwrite;

        Run(false, Path.Combine(_root, "d"), Path.Combine(_root, "s", "dir"));

        Assert.True(File.Exists(Path.Combine(_root, "d", "dir", "new.txt")));
        Assert.False(File.Exists(Path.Combine(_root, "d", "dir", "stale.txt")));
    }

    // ===== 失敗 =====

    [Fact]
    public void LockedSource_Move_FailsWithAMessage_AndKeepsTheSource()
    {
        var src = File_("s/locked.txt", "data");
        var dest = Dir_("d");
        using var held = new FileStream(src, FileMode.Open, FileAccess.Read, FileShare.Read);

        var item = Run(true, dest, src);

        Assert.Equal(FileOperationQueueItemStatus.Failed, item.Status);
        Assert.False(string.IsNullOrWhiteSpace(item.ErrorMessage));
        Assert.True(File.Exists(src));
    }

    [Fact]
    public void AFailure_DoesNotStopLaterItems()
    {
        var locked = File_("s/locked.txt");
        var ok = File_("s/ok.txt", "fine");
        var dest = Dir_("d");
        using (new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Assert.Equal(FileOperationQueueItemStatus.Failed, Run(true, dest, locked).Status);
        }

        var next = Run(false, dest, ok);

        Assert.Equal(FileOperationQueueItemStatus.Completed, next.Status);
    }

    // ===== 一時停止・キャンセル =====

    [Fact]
    public void Cancel_BeforeItStarts_EndsAsCancelled_WithoutCopying()
    {
        var dest = Dir_("d");
        var done = new TaskCompletionSource<FileOperationQueueItem>(TaskCreationOptions.RunContinuationsAsynchronously);
        _sut.ItemCompleted += i => done.TrySetResult(i);

        // 先の項目を一時停止させて詰まらせ、その間に、後ろの項目を取り消す。
        var blockerDone = new ManualResetEventSlim();
        var gate = new ManualResetEventSlim();
        _sut.ConflictResolver = _ =>
        {
            gate.Wait(Timeout);
            return FileOperationConflictResolution.Skip;
        };
        var conflicting = File_("s/a.txt");
        File.WriteAllText(Path.Combine(dest, "a.txt"), "old");
        var blocker = _sut.Enqueue("コピー", new[] { conflicting }, dest, false);
        _sut.ItemCompleted += i =>
        {
            if (i == blocker)
            {
                blockerDone.Set();
            }
        };

        var victimSource = File_("s/v.txt");
        var victim = _sut.Enqueue("コピー", new[] { victimSource }, dest, false);
        _sut.Cancel(victim);
        gate.Set();

        Assert.True(blockerDone.Wait(Timeout));
        SpinWait.SpinUntil(() => victim.Status == FileOperationQueueItemStatus.Cancelled, Timeout);
        Assert.Equal(FileOperationQueueItemStatus.Cancelled, victim.Status);
        Assert.False(File.Exists(Path.Combine(dest, "v.txt")));
    }

    [Fact]
    public void Pause_HoldsTheItemBetweenSources_UntilResumed()
    {
        var dest = Dir_("d");
        var first = File_("s/1.txt");
        var second = File_("s/2.txt");
        var reachedFirstConflict = new ManualResetEventSlim();
        var releaseFirst = new ManualResetEventSlim();
        File.WriteAllText(Path.Combine(dest, "1.txt"), "old");

        // 1つ目の処理中（競合の確認で止まっている間）に、一時停止する。
        _sut.ConflictResolver = _ =>
        {
            reachedFirstConflict.Set();
            releaseFirst.Wait(Timeout);
            return FileOperationConflictResolution.Skip;
        };
        var done = new TaskCompletionSource<FileOperationQueueItem>(TaskCreationOptions.RunContinuationsAsynchronously);
        _sut.ItemCompleted += i => done.TrySetResult(i);

        var item = _sut.Enqueue("コピー", new[] { first, second }, dest, false);
        Assert.True(reachedFirstConflict.Wait(Timeout));
        _sut.Pause(item);
        releaseFirst.Set();

        // 一時停止中は、2つ目に進まない。
        Assert.False(done.Task.Wait(TimeSpan.FromMilliseconds(600)));
        Assert.Equal(FileOperationQueueItemStatus.Paused, item.Status);
        Assert.False(File.Exists(Path.Combine(dest, "2.txt")));

        _sut.Resume(item);

        Assert.True(done.Task.Wait(Timeout));
        Assert.Equal(FileOperationQueueItemStatus.Completed, item.Status);
        Assert.True(File.Exists(Path.Combine(dest, "2.txt")));
    }

    [Fact]
    public void Cancel_WhilePaused_EndsAsCancelled()
    {
        var dest = Dir_("d");
        var first = File_("s/1.txt");
        var second = File_("s/2.txt");
        File.WriteAllText(Path.Combine(dest, "1.txt"), "old");
        var reached = new ManualResetEventSlim();
        var release = new ManualResetEventSlim();
        _sut.ConflictResolver = _ =>
        {
            reached.Set();
            release.Wait(Timeout);
            return FileOperationConflictResolution.Skip;
        };
        var done = new TaskCompletionSource<FileOperationQueueItem>(TaskCreationOptions.RunContinuationsAsynchronously);
        _sut.ItemCompleted += i => done.TrySetResult(i);

        var item = _sut.Enqueue("コピー", new[] { first, second }, dest, false);
        Assert.True(reached.Wait(Timeout));
        _sut.Pause(item);
        release.Set();
        _sut.Cancel(item);

        Assert.True(done.Task.Wait(Timeout));
        Assert.Equal(FileOperationQueueItemStatus.Cancelled, item.Status);
        Assert.False(File.Exists(Path.Combine(dest, "2.txt")));
    }

    // ===== 項目の状態表示 =====

    [Theory]
    [InlineData(FileOperationQueueItemStatus.Waiting, false, false, true)]
    [InlineData(FileOperationQueueItemStatus.Running, true, false, true)]
    [InlineData(FileOperationQueueItemStatus.Paused, false, true, true)]
    [InlineData(FileOperationQueueItemStatus.Completed, false, false, false)]
    [InlineData(FileOperationQueueItemStatus.Cancelled, false, false, false)]
    [InlineData(FileOperationQueueItemStatus.Failed, false, false, false)]
    public void ItemStatus_DecidesWhichButtonsAreAvailable(FileOperationQueueItemStatus status, bool canPause, bool canResume, bool canCancel)
    {
        var item = new FileOperationQueueItem
        {
            Id = Guid.NewGuid(),
            Kind = "コピー",
            SourcePaths = new[] { "a" },
            DestinationFolder = "d",
            IsMove = false,
            Status = status
        };

        Assert.Equal(canPause, item.CanPause);
        Assert.Equal(canResume, item.CanResume);
        Assert.Equal(canCancel, item.CanCancel);
        Assert.False(string.IsNullOrEmpty(item.StatusDisplay));
    }
}

public sealed class FileOperationHistoryServiceTests
{
    private readonly ExplorerAlternative.Tests.TestDoubles.FakeSettingsService _settings = new();
    private readonly FileOperationHistoryService _sut;

    public FileOperationHistoryServiceTests()
    {
        _sut = new FileOperationHistoryService(_settings);
    }

    private static FileOperationHistoryEntry Entry(string target, DateTime when) =>
        new() { Operation = "コピー", Target = target, Timestamp = when };

    [Fact]
    public void GetAll_IsNewestFirst()
    {
        var t = new DateTime(2026, 1, 1);
        _sut.Record(Entry("old", t));
        _sut.Record(Entry("new", t.AddHours(1)));
        _sut.Record(Entry("mid", t.AddMinutes(30)));

        Assert.Equal(new[] { "new", "mid", "old" }, _sut.GetAll().Select(e => e.Target));
    }

    [Fact]
    public void Record_Persists()
    {
        _sut.Record(Entry("a", DateTime.Now));

        Assert.Equal(1, _settings.SaveCount);
    }

    [Fact]
    public void Record_KeepsAtMost500_DroppingTheOldest()
    {
        var start = new DateTime(2026, 1, 1);
        for (var i = 0; i < 505; i++)
        {
            _sut.Record(Entry($"e{i}", start.AddMinutes(i)));
        }

        var all = _sut.GetAll();

        Assert.Equal(500, all.Count);
        Assert.Equal("e504", all[0].Target);
        Assert.DoesNotContain(all, e => e.Target is "e0" or "e4");
        Assert.Contains(all, e => e.Target == "e5");
    }

    [Fact]
    public void Remove_DeletesThatEntry_AndPersists()
    {
        var keep = Entry("keep", DateTime.Now);
        var drop = Entry("drop", DateTime.Now);
        _sut.Record(keep);
        _sut.Record(drop);

        _sut.Remove(drop);

        Assert.Equal(new[] { "keep" }, _sut.GetAll().Select(e => e.Target));
        Assert.Equal(3, _settings.SaveCount);
    }

    [Fact]
    public void Clear_EmptiesTheHistory_AndPersists()
    {
        _sut.Record(Entry("a", DateTime.Now));

        _sut.Clear();

        Assert.Empty(_sut.GetAll());
        Assert.Equal(2, _settings.SaveCount);
    }

    [Fact]
    public void Failed_Entries_ShowFailure()
    {
        var entry = new FileOperationHistoryEntry { Operation = "削除", Target = "x", Success = false, ErrorMessage = "使用中" };

        Assert.Equal("失敗", entry.SuccessDisplay);
        Assert.Equal("成功", Entry("y", DateTime.Now).SuccessDisplay);
    }
}
