using ExplorerAlternative.Services;

namespace ExplorerAlternative.Tests.Services;

// 仕様書62章（Undo）・30章（Redo）：操作履歴の元に戻す・やり直し。
public sealed class UndoServiceTests
{
    // 操作の実行順序を記録するための道具。
    private static (UndoService Service, List<string> Log) Create() => (new UndoService(), new List<string>());

    private static void Record(UndoService service, List<string> log, string name, bool redoable = true)
    {
        service.Record(
            name,
            () => log.Add($"undo {name}"),
            redoable ? () => log.Add($"redo {name}") : null);
    }

    // ===== Undo =====

    [Fact]
    public void Record_MakesUndoAvailable_WithDescription()
    {
        var (service, log) = Create();

        Record(service, log, "A");

        Assert.True(service.CanUndo);
        Assert.Equal("A", service.NextUndoDescription);
    }

    [Fact]
    public void Undo_RunsTheLatestOperationOnly_AndConsumesIt()
    {
        var (service, log) = Create();
        Record(service, log, "A");
        Record(service, log, "B");

        service.Undo();

        Assert.Equal(new[] { "undo B" }, log);
        Assert.Equal("A", service.NextUndoDescription);
    }

    [Fact]
    public void Undo_WithNothingRecorded_DoesNothing()
    {
        var (service, log) = Create();

        service.Undo();

        Assert.Empty(log);
        Assert.False(service.CanUndo);
    }

    [Fact]
    public void History_ListsOperationsOldestFirst()
    {
        var (service, log) = Create();
        Record(service, log, "A");
        Record(service, log, "B");

        Assert.Equal(new[] { "A", "B" }, service.History.Select(h => h.Description).ToArray());
    }

    [Fact]
    public void History_KeepsOnlyTheMostRecent50()
    {
        var (service, log) = Create();
        for (var i = 0; i < 60; i++)
        {
            Record(service, log, $"op{i}");
        }

        Assert.Equal(50, service.History.Count);
        Assert.Equal("op10", service.History[0].Description);
        Assert.Equal("op59", service.NextUndoDescription);
    }

    // ===== Redo =====

    [Fact]
    public void Redo_IsNotAvailable_BeforeAnythingIsUndone()
    {
        var (service, log) = Create();
        Record(service, log, "A");

        Assert.False(service.CanRedo);
        Assert.Null(service.NextRedoDescription);
    }

    [Fact]
    public void Undo_MakesTheOperationRedoable()
    {
        var (service, log) = Create();
        Record(service, log, "A");

        service.Undo();

        Assert.True(service.CanRedo);
        Assert.Equal("A", service.NextRedoDescription);
        Assert.False(service.CanUndo);
    }

    [Fact]
    public void Redo_RunsTheRedoAction_AndMakesItUndoableAgain()
    {
        var (service, log) = Create();
        Record(service, log, "A");
        service.Undo();

        service.Redo();

        Assert.Equal(new[] { "undo A", "redo A" }, log);
        Assert.False(service.CanRedo);
        Assert.True(service.CanUndo);
        Assert.Equal("A", service.NextUndoDescription);
    }

    [Fact]
    public void UndoRedoCycle_CanBeRepeated()
    {
        var (service, log) = Create();
        Record(service, log, "A");

        service.Undo();
        service.Redo();
        service.Undo();
        service.Redo();

        Assert.Equal(new[] { "undo A", "redo A", "undo A", "redo A" }, log);
    }

    // 3つ元に戻したら、最初に戻した方（最新の操作）が最後、最も古い操作から順にやり直される。
    [Fact]
    public void Redo_ReappliesInTheOriginalOrder()
    {
        var (service, log) = Create();
        Record(service, log, "A");
        Record(service, log, "B");
        Record(service, log, "C");
        service.Undo();
        service.Undo();
        service.Undo();
        log.Clear();

        service.Redo();
        service.Redo();
        service.Redo();

        Assert.Equal(new[] { "redo A", "redo B", "redo C" }, log);
    }

    [Fact]
    public void Redo_DoesNotClearTheRemainingRedoStack()
    {
        var (service, log) = Create();
        Record(service, log, "A");
        Record(service, log, "B");
        service.Undo();
        service.Undo();

        service.Redo();

        Assert.True(service.CanRedo);
        Assert.Equal("B", service.NextRedoDescription);
    }

    [Fact]
    public void Redo_WithNothingToRedo_DoesNothing()
    {
        var (service, log) = Create();

        service.Redo();

        Assert.Empty(log);
    }

    // 新しい操作を行ったら、それまでやり直せた操作は、状態が合わなくなるため無効になる。
    [Fact]
    public void Record_AfterUndo_DiscardsTheRedoStack()
    {
        var (service, log) = Create();
        Record(service, log, "A");
        service.Undo();
        Assert.True(service.CanRedo);

        Record(service, log, "B");

        Assert.False(service.CanRedo);
    }

    // ===== やり直せない操作 =====

    [Fact]
    public void OperationWithoutRedo_CanBeUndone_ButNotRedone()
    {
        var (service, log) = Create();
        Record(service, log, "overwrite", redoable: false);

        service.Undo();

        Assert.Equal(new[] { "undo overwrite" }, log);
        Assert.False(service.CanRedo);
    }

    // 時間的に先に行った「やり直せない操作」を戻すと、あとに戻した操作のやり直しは、状態と合わなくなる。
    [Fact]
    public void UndoingANonRedoableOperation_DiscardsEarlierUndoneOperations()
    {
        var service = new UndoService();
        var log = new List<string>();
        service.Record("first", () => log.Add("undo first"), null);                              // やり直せない（古い）
        service.Record("second", () => log.Add("undo second"), () => log.Add("redo second")); // やり直せる（新しい）

        service.Undo(); // second → Redoスタックへ
        Assert.True(service.CanRedo);

        service.Undo(); // first（やり直せない）→ Redoスタックは捨てられる

        Assert.False(service.CanRedo);
    }

    // ===== 失敗 =====

    [Fact]
    public void Undo_Failure_PropagatesAndDiscardsTheRedoStack()
    {
        var (service, log) = Create();
        Record(service, log, "A");
        service.Record("B", () => throw new AppOperationException("戻せません"), () => log.Add("redo B"));
        Record(service, log, "C");
        service.Undo(); // C → redo可能
        Assert.True(service.CanRedo);

        var ex = Assert.Throws<AppOperationException>(() => service.Undo()); // B が失敗

        Assert.Equal("戻せません", ex.Message);
        Assert.False(service.CanRedo);
        Assert.Equal("A", service.NextUndoDescription); // 失敗した操作は履歴から外れる
    }

    [Fact]
    public void Redo_Failure_PropagatesAndDiscardsTheRemainingRedoStack()
    {
        var service = new UndoService();
        var log = new List<string>();
        service.Record("A", () => log.Add("undo A"), () => throw new AppOperationException("やり直せません"));
        service.Record("B", () => log.Add("undo B"), () => log.Add("redo B"));
        service.Undo();
        service.Undo();

        var ex = Assert.Throws<AppOperationException>(() => service.Redo()); // 最初のredo(A)が失敗

        Assert.Equal("やり直せません", ex.Message);
        Assert.False(service.CanRedo);
        Assert.False(service.CanUndo); // 失敗した操作は、元に戻せる履歴へ戻らない
    }

    // ===== UndoTo（まとめて元に戻す） =====

    [Fact]
    public void UndoTo_UndoesNewestFirst_IncludingTheChosenOperation()
    {
        var (service, log) = Create();
        Record(service, log, "A");
        Record(service, log, "B");
        Record(service, log, "C");
        var b = service.History[1].Id;

        var failed = service.UndoTo(b);

        Assert.Empty(failed);
        Assert.Equal(new[] { "undo C", "undo B" }, log);
        Assert.Equal("A", service.NextUndoDescription);
    }

    [Fact]
    public void UndoTo_MakesTheUndoneOperationsRedoableInOriginalOrder()
    {
        var (service, log) = Create();
        Record(service, log, "A");
        Record(service, log, "B");
        Record(service, log, "C");
        service.UndoTo(service.History[0].Id);
        log.Clear();

        service.Redo();
        service.Redo();
        service.Redo();

        Assert.Equal(new[] { "redo A", "redo B", "redo C" }, log);
    }

    [Fact]
    public void UndoTo_UnknownId_DoesNothing()
    {
        var (service, log) = Create();
        Record(service, log, "A");

        var failed = service.UndoTo(Guid.NewGuid());

        Assert.Empty(failed);
        Assert.Empty(log);
        Assert.True(service.CanUndo);
    }

    [Fact]
    public void UndoTo_ContinuesAfterAFailure_AndReportsIt_AndDiscardsRedo()
    {
        var (service, log) = Create();
        Record(service, log, "A");
        service.Record("B", () => throw new AppOperationException("x"), () => log.Add("redo B"));
        Record(service, log, "C");

        var failed = service.UndoTo(service.History[0].Id);

        Assert.Equal(new[] { "B" }, failed);
        Assert.Equal(new[] { "undo C", "undo A" }, log);
        Assert.False(service.CanRedo);
    }

    // ===== 通知 =====

    [Fact]
    public void Changed_IsRaisedByRecordUndoAndRedo()
    {
        var (service, log) = Create();
        var raised = 0;
        service.Changed += () => raised++;

        Record(service, log, "A");
        service.Undo();
        service.Redo();

        Assert.Equal(3, raised);
    }

    // ===== 実ファイルでの確認（名前変更・移動。ごみ箱を使う削除は対象にしない） =====

    [Fact]
    public void RealFiles_RenameUndoRedo_RoundTrips()
    {
        var root = Directory.CreateTempSubdirectory("eat_undo_").FullName;
        try
        {
            var fs = new FileSystemService();
            var oldPath = Path.Combine(root, "a.txt");
            var newPath = Path.Combine(root, "b.txt");
            File.WriteAllText(oldPath, "x");
            var service = new UndoService();

            fs.Rename(oldPath, "b.txt");
            service.Record("rename", () => fs.Rename(newPath, "a.txt"), () => fs.Rename(oldPath, "b.txt"));

            service.Undo();
            Assert.True(File.Exists(oldPath));
            Assert.False(File.Exists(newPath));

            service.Redo();
            Assert.False(File.Exists(oldPath));
            Assert.True(File.Exists(newPath));

            service.Undo();
            Assert.True(File.Exists(oldPath));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void RealFiles_MoveUndoRedo_RoundTrips()
    {
        var root = Directory.CreateTempSubdirectory("eat_undo_").FullName;
        try
        {
            var fs = new FileSystemService();
            var source = Path.Combine(root, "src");
            var destination = Path.Combine(root, "dst");
            Directory.CreateDirectory(source);
            Directory.CreateDirectory(destination);
            var original = Path.Combine(source, "f.txt");
            var moved = Path.Combine(destination, "f.txt");
            File.WriteAllText(original, "x");
            var service = new UndoService();

            fs.Move(new[] { original }, destination);
            service.Record("move", () => fs.Move(new[] { moved }, source), () => fs.Move(new[] { original }, destination));

            service.Undo();
            Assert.True(File.Exists(original));

            service.Redo();
            Assert.True(File.Exists(moved));
            Assert.False(File.Exists(original));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void RealFiles_CopyRedo_RecreatesTheCopy_AfterTheCopyIsRemoved()
    {
        var root = Directory.CreateTempSubdirectory("eat_undo_").FullName;
        try
        {
            var fs = new FileSystemService();
            var source = Path.Combine(root, "src");
            var destination = Path.Combine(root, "dst");
            Directory.CreateDirectory(source);
            Directory.CreateDirectory(destination);
            var original = Path.Combine(source, "f.txt");
            var copied = Path.Combine(destination, "f.txt");
            File.WriteAllText(original, "data");
            var service = new UndoService();

            fs.Copy(new[] { original }, destination);
            // Undoの実際の処理はごみ箱への削除だが、テストではごみ箱を汚さないよう、通常の削除で代用する。
            service.Record("copy", () => File.Delete(copied), () => fs.Copy(new[] { original }, destination));

            service.Undo();
            Assert.False(File.Exists(copied));

            service.Redo();
            Assert.Equal("data", File.ReadAllText(copied));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
