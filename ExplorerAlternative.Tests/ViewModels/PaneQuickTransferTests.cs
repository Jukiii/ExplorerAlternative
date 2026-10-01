using ExplorerAlternative.Models;
using ExplorerAlternative.Tests.TestDoubles;

namespace ExplorerAlternative.Tests.ViewModels;

// 仕様書60章：クイックコピー / クイック移動（最近のコピー先・移動先から選んで実行する）。
public sealed class PaneQuickTransferTests : IDisposable
{
    private const string BrowseLabel = "フォルダを参照...";

    private readonly PaneTestHost _host = new();

    public void Dispose() => _host.Dispose();

    private void AddHistory(string operation, string destination, bool success = true) =>
        _host.History.Record(new FileOperationHistoryEntry
        {
            Operation = operation,
            Target = "x",
            Destination = destination,
            Success = success,
            Timestamp = DateTime.Now
        });

    private string CreateSourceFile(string name = "a.txt")
    {
        var path = _host.CreateFile(name);
        _host.Pane.RefreshCommand.Execute(null);
        _host.Select(name);
        return path;
    }

    // ===== 実行できる状態 =====

    [Fact]
    public void Commands_AreDisabled_WhenNothingIsSelected()
    {
        Assert.False(_host.Pane.QuickCopyCommand.CanExecute(null));
        Assert.False(_host.Pane.QuickMoveCommand.CanExecute(null));
    }

    [Fact]
    public void Commands_AreEnabled_WhenSomethingIsSelected()
    {
        CreateSourceFile();

        Assert.True(_host.Pane.QuickCopyCommand.CanExecute(null));
        Assert.True(_host.Pane.QuickMoveCommand.CanExecute(null));
    }

    // ===== 宛先の候補 =====

    [Fact]
    public void Candidates_ListRecentCopyDestinations_ThenTheBrowseOption()
    {
        var destA = _host.CreateFolder("destA");
        var destB = _host.CreateFolder("destB");
        AddHistory("コピー", destB);
        AddHistory("コピー", destA); // 新しい（先頭）
        CreateSourceFile();
        IReadOnlyList<string>? shown = null;
        _host.DialogControl.On("SelectFromList", args =>
        {
            shown = (IReadOnlyList<string>)args[2]!;
            return null; // キャンセル
        });

        _host.Pane.QuickCopyCommand.Execute(null);

        Assert.Equal(new[] { destA, destB, BrowseLabel }, shown);
    }

    [Fact]
    public void Candidates_ForMove_UseMoveHistoryOnly()
    {
        var copyDest = _host.CreateFolder("copyDest");
        var moveDest = _host.CreateFolder("moveDest");
        AddHistory("コピー", copyDest);
        AddHistory("移動", moveDest);
        CreateSourceFile();
        IReadOnlyList<string>? shown = null;
        _host.DialogControl.On("SelectFromList", args =>
        {
            shown = (IReadOnlyList<string>)args[2]!;
            return null;
        });

        _host.Pane.QuickMoveCommand.Execute(null);

        Assert.Equal(new[] { moveDest, BrowseLabel }, shown);
    }

    [Fact]
    public void Candidates_ExcludeTheCurrentFolder_AndFailedOperations_AndMissingFolders()
    {
        var other = _host.CreateFolder("other");
        AddHistory("コピー", _host.Root);                                   // 現在のフォルダ
        AddHistory("コピー", Path.Combine(_host.Root, "gone"));            // 存在しない
        AddHistory("コピー", _host.CreateFolder("failed"), success: false); // 失敗
        AddHistory("コピー", other);
        CreateSourceFile();
        IReadOnlyList<string>? shown = null;
        _host.DialogControl.On("SelectFromList", args =>
        {
            shown = (IReadOnlyList<string>)args[2]!;
            return null;
        });

        _host.Pane.QuickCopyCommand.Execute(null);

        Assert.Equal(new[] { other, BrowseLabel }, shown);
    }

    [Fact]
    public void Candidates_WithoutHistory_ShowOnlyTheBrowseOption()
    {
        CreateSourceFile();
        IReadOnlyList<string>? shown = null;
        _host.DialogControl.On("SelectFromList", args =>
        {
            shown = (IReadOnlyList<string>)args[2]!;
            return null;
        });

        _host.Pane.QuickCopyCommand.Execute(null);

        Assert.Equal(new[] { BrowseLabel }, shown);
    }

    // ===== 実行 =====

    [Fact]
    public void QuickCopy_ToARecentDestination_EnqueuesACopy()
    {
        var dest = _host.CreateFolder("dest");
        AddHistory("コピー", dest);
        var source = CreateSourceFile();
        _host.DialogControl.On("SelectFromList", args => dest);

        _host.Pane.QuickCopyCommand.Execute(null);

        var item = Assert.Single(_host.Queue.Enqueued);
        Assert.Equal("コピー", item.Kind);
        Assert.False(item.IsMove);
        Assert.Equal(dest, item.DestinationFolder);
        Assert.Equal(new[] { source }, item.SourcePaths);
    }

    [Fact]
    public void QuickMove_ToARecentDestination_EnqueuesAMove()
    {
        var dest = _host.CreateFolder("dest");
        AddHistory("移動", dest);
        var source = CreateSourceFile();
        _host.DialogControl.On("SelectFromList", args => dest);

        _host.Pane.QuickMoveCommand.Execute(null);

        var item = Assert.Single(_host.Queue.Enqueued);
        Assert.Equal("移動", item.Kind);
        Assert.True(item.IsMove);
        Assert.Equal(dest, item.DestinationFolder);
        Assert.Equal(new[] { source }, item.SourcePaths);
    }

    [Fact]
    public void QuickCopy_WithSeveralSelectedItems_EnqueuesAllOfThem()
    {
        var dest = _host.CreateFolder("dest");
        AddHistory("コピー", dest);
        _host.CreateFile("a.txt");
        _host.CreateFile("b.txt");
        _host.Pane.RefreshCommand.Execute(null);
        _host.Select("a.txt", "b.txt");
        _host.DialogControl.On("SelectFromList", args => dest);

        _host.Pane.QuickCopyCommand.Execute(null);

        var item = Assert.Single(_host.Queue.Enqueued);
        Assert.Equal(2, item.SourcePaths.Count);
    }

    [Fact]
    public void QuickCopy_Browse_UsesTheFolderChosenInTheDialog()
    {
        var dest = _host.CreateFolder("browsed");
        CreateSourceFile();
        _host.DialogControl.On("SelectFromList", args => BrowseLabel);
        _host.DialogControl.On("ShowOpenFolderDialog", args => dest);

        _host.Pane.QuickCopyCommand.Execute(null);

        var item = Assert.Single(_host.Queue.Enqueued);
        Assert.Equal(dest, item.DestinationFolder);
    }

    [Fact]
    public void QuickCopy_BrowseCancelled_DoesNothing()
    {
        CreateSourceFile();
        _host.DialogControl.On("SelectFromList", args => BrowseLabel);
        _host.DialogControl.On("ShowOpenFolderDialog", args => null);

        _host.Pane.QuickCopyCommand.Execute(null);

        Assert.Empty(_host.Queue.Enqueued);
        Assert.Equal(0, _host.DialogControl.CountOf("ShowError"));
    }

    [Fact]
    public void QuickCopy_SelectionCancelled_DoesNothing()
    {
        CreateSourceFile();
        _host.DialogControl.On("SelectFromList", args => null);

        _host.Pane.QuickCopyCommand.Execute(null);

        Assert.Empty(_host.Queue.Enqueued);
        Assert.Equal(0, _host.DialogControl.CountOf("ShowOpenFolderDialog"));
    }

    // ===== 異常系 =====

    [Fact]
    public void QuickCopy_ToAFolderThatNoLongerExists_ShowsAnErrorAndDoesNotCopy()
    {
        var missing = Path.Combine(_host.Root, "missing");
        CreateSourceFile();
        _host.DialogControl.On("SelectFromList", args => BrowseLabel);
        _host.DialogControl.On("ShowOpenFolderDialog", args => missing);

        _host.Pane.QuickCopyCommand.Execute(null);

        Assert.Empty(_host.Queue.Enqueued);
        var message = (string)Assert.Single(_host.DialogControl.ArgsOf("ShowError"))[0]!;
        Assert.Contains("見つかりません", message);
    }

    // 選んだ項目がすでにそのフォルダにある場合は、何も起きないのではなく、理由を伝える。
    [Fact]
    public void QuickMove_ToTheFolderTheItemIsAlreadyIn_ShowsAnInfoAndDoesNothing()
    {
        CreateSourceFile();
        _host.DialogControl.On("SelectFromList", args => BrowseLabel);
        _host.DialogControl.On("ShowOpenFolderDialog", args => _host.Root);

        _host.Pane.QuickMoveCommand.Execute(null);

        Assert.Empty(_host.Queue.Enqueued);
        var message = (string)Assert.Single(_host.DialogControl.ArgsOf("ShowInfo"))[0]!;
        Assert.Contains("移動できません", message);
    }

    [Fact]
    public void QuickCopy_FolderIntoItself_ShowsAnInfoAndDoesNothing()
    {
        var folder = _host.CreateFolder("parent");
        var child = Directory.CreateDirectory(Path.Combine(folder, "child")).FullName;
        _host.Pane.RefreshCommand.Execute(null);
        _host.Select("parent");
        _host.DialogControl.On("SelectFromList", args => BrowseLabel);
        _host.DialogControl.On("ShowOpenFolderDialog", args => child);

        _host.Pane.QuickCopyCommand.Execute(null);

        Assert.Empty(_host.Queue.Enqueued);
        Assert.Equal(1, _host.DialogControl.CountOf("ShowInfo"));
    }

    // 実行の確認ダイアログ（設定で有効にした場合）で「いいえ」なら、実行しない（既存の移動・コピーと同じ）。
    [Fact]
    public void QuickCopy_RespectsTheConfirmBeforeMoveAndCopySetting()
    {
        _host.Settings.Current.View.ConfirmMoveAndCopy = true;
        var dest = _host.CreateFolder("dest");
        AddHistory("コピー", dest);
        CreateSourceFile();
        _host.DialogControl.On("SelectFromList", args => dest);
        _host.DialogControl.On("Confirm", args => false);

        _host.Pane.QuickCopyCommand.Execute(null);

        Assert.Empty(_host.Queue.Enqueued);
        Assert.Equal(1, _host.DialogControl.CountOf("Confirm"));
    }

    // ===== 実行後の記録（Undo・履歴）につながること =====

    [Fact]
    public void QuickMove_WhenQueueItemCompletes_RecordsUndoAndHistory_SoItAppearsAsARecentDestinationNextTime()
    {
        var dest = _host.CreateFolder("dest");
        var source = CreateSourceFile();
        _host.DialogControl.On("SelectFromList", args => BrowseLabel);
        _host.DialogControl.On("ShowOpenFolderDialog", args => dest);

        _host.Pane.QuickMoveCommand.Execute(null);
        _host.Queue.Complete(_host.Queue.Enqueued[0]);

        // 履歴に「移動」が記録され、元に戻せる操作にもなる。
        Assert.Contains(_host.History.GetAll(), e => e.Operation == "移動" && e.Destination == dest && e.Success);
        Assert.True(_host.Undo.CanUndo);

        // 次回のクイック移動では、今回の宛先が最近の移動先として候補に出る。
        IReadOnlyList<string>? shown = null;
        _host.DialogControl.On("SelectFromList", args =>
        {
            shown = (IReadOnlyList<string>)args[2]!;
            return null;
        });
        _host.Pane.RefreshCommand.Execute(null);
        _host.CreateFile("next.txt");
        _host.Pane.RefreshCommand.Execute(null);
        _host.Select("next.txt");
        _host.Pane.QuickMoveCommand.Execute(null);

        Assert.Equal(new[] { dest, BrowseLabel }, shown);
        Assert.False(string.IsNullOrEmpty(source));
    }
}
