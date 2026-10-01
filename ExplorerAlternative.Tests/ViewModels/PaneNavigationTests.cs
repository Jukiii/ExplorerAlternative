using ExplorerAlternative.Models;
using ExplorerAlternative.Tests.TestDoubles;
using ExplorerAlternative.ViewModels;

namespace ExplorerAlternative.Tests.ViewModels;

// 仕様書4・6・7・8章：メインペインの、フォルダ移動（履歴・親フォルダ・アドレスバー）、並べ替え、
// 階層表示の展開、選択、隠しファイルの表示。実際の一時フォルダの上で確認する。
public sealed class PaneNavigationTests : IDisposable
{
    private readonly PaneTestHost _host = new();

    public void Dispose() => _host.Dispose();

    private PaneViewModel Pane => _host.Pane;

    private string[] Visible() => Pane.VisibleNodes.Select(n => n.Name).ToArray();

    private string Dir(string name) => _host.CreateFolder(name);

    private string File_(string relative, string content = "x")
    {
        var path = Path.Combine(_host.Root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    // ===== 一覧 =====

    [Fact]
    public void InitialPath_ListsItsContents_FoldersFirst()
    {
        File_("b.txt");
        Dir("zdir");
        File_("a.txt");
        Dir("adir");

        Pane.RefreshCurrentFolder();

        Assert.Equal(new[] { "adir", "zdir", "a.txt", "b.txt" }, Visible());
    }

    [Fact]
    public void Refresh_PicksUpNewFiles()
    {
        Pane.RefreshCurrentFolder();
        Assert.Empty(Visible());

        File_("new.txt");
        Pane.RefreshCurrentFolder();

        Assert.Equal(new[] { "new.txt" }, Visible());
    }

    // ===== 隠しファイル =====

    [Fact]
    public void HiddenFiles_AreHiddenByDefault_AndShownByTheToggle()
    {
        File_("visible.txt");
        File_(".hidden");
        Pane.RefreshCurrentFolder();
        Assert.Equal(new[] { "visible.txt" }, Visible());

        Pane.ToggleShowHiddenFilesCommand.Execute(null);

        Assert.Contains(".hidden", Visible());
        Assert.True(_host.Settings.Current.View.ShowHiddenFiles);

        Pane.ToggleShowHiddenFilesCommand.Execute(null);

        Assert.DoesNotContain(".hidden", Visible());
    }

    // ===== 並べ替え =====

    [Fact]
    public void SortByName_TogglesBetweenAscendingAndDescending_KeepingFoldersOnTop()
    {
        File_("a.txt");
        File_("c.txt");
        Dir("dir");
        Pane.RefreshCurrentFolder();

        Pane.SortByColumnCommand.Execute("Name");
        Assert.Contains("▼", Pane.SortIndicatorName); // 最初から名前の昇順なので、同じ列の再クリックは降順
        Assert.Equal(new[] { "dir", "c.txt", "a.txt" }, Visible());

        Pane.SortByColumnCommand.Execute("Name");
        Assert.Contains("▲", Pane.SortIndicatorName);
        Assert.Equal(new[] { "dir", "a.txt", "c.txt" }, Visible());
    }

    [Fact]
    public void SortBySize_StartsAscending_AndMovesTheIndicator()
    {
        File_("big.txt", new string('x', 100));
        File_("small.txt", "x");
        Pane.RefreshCurrentFolder();

        Pane.SortByColumnCommand.Execute("Size");

        Assert.Equal(new[] { "small.txt", "big.txt" }, Visible());
        Assert.Contains("▲", Pane.SortIndicatorSize);
        Assert.Equal(string.Empty, Pane.SortIndicatorName);

        Pane.SortByColumnCommand.Execute("Size");

        Assert.Equal(new[] { "big.txt", "small.txt" }, Visible());
    }

    [Fact]
    public void SortByName_IsCaseInsensitive()
    {
        File_("b.txt");
        File_("A.txt");
        File_("c.txt");
        Pane.RefreshCurrentFolder();

        Assert.Equal(new[] { "A.txt", "b.txt", "c.txt" }, Visible());
    }

    [Fact]
    public void SortByLastModified_OldestFirst()
    {
        var older = File_("older.txt");
        var newer = File_("newer.txt");
        File.SetLastWriteTime(older, new DateTime(2020, 1, 1));
        File.SetLastWriteTime(newer, new DateTime(2024, 1, 1));
        Pane.RefreshCurrentFolder();

        Pane.SortByColumnCommand.Execute("LastModified");

        Assert.Equal(new[] { "older.txt", "newer.txt" }, Visible());
    }

    [Fact]
    public void SortChoice_SurvivesAReload()
    {
        File_("a.txt", "x");
        File_("b.txt", new string('x', 50));
        Pane.RefreshCurrentFolder();
        Pane.SortByColumnCommand.Execute("Size");
        Pane.SortByColumnCommand.Execute("Size"); // 降順

        Pane.RefreshCurrentFolder();

        Assert.Equal(new[] { "b.txt", "a.txt" }, Visible());
    }

    // ===== 階層表示の展開 =====

    [Fact]
    public void Expanding_AFolder_ShowsItsChildrenRightBelowIt_AndCollapsingRemovesThem()
    {
        File_("dir/inner.txt");
        File_("z.txt");
        Pane.RefreshCurrentFolder();
        var dir = Pane.VisibleNodes.Single(n => n.Name == "dir");
        Assert.Equal(">", dir.ExpandSymbol);

        Pane.ToggleExpandCommand.Execute(dir);

        Assert.Equal(new[] { "dir", "inner.txt", "z.txt" }, Visible());
        Assert.Equal("v", dir.ExpandSymbol);
        Assert.Equal(1, Pane.VisibleNodes.Single(n => n.Name == "inner.txt").Depth);

        Pane.ToggleExpandCommand.Execute(dir);

        Assert.Equal(new[] { "dir", "z.txt" }, Visible());
        Assert.Equal(">", dir.ExpandSymbol);
    }

    [Fact]
    public void Collapsing_AParent_HidesExpandedGrandchildren_AndReExpandingRestoresThem()
    {
        File_("a/b/leaf.txt");
        Pane.RefreshCurrentFolder();
        var a = Pane.VisibleNodes.Single(n => n.Name == "a");
        Pane.ToggleExpandCommand.Execute(a);
        var b = Pane.VisibleNodes.Single(n => n.Name == "b");
        Pane.ToggleExpandCommand.Execute(b);
        Assert.Equal(new[] { "a", "b", "leaf.txt" }, Visible());

        Pane.ToggleExpandCommand.Execute(a);
        Assert.Equal(new[] { "a" }, Visible());

        Pane.ToggleExpandCommand.Execute(a);
        Assert.Equal(new[] { "a", "b", "leaf.txt" }, Visible());
    }

    [Fact]
    public void ExpandedFolders_AreKeptAcrossARefresh()
    {
        File_("dir/inner.txt");
        Pane.RefreshCurrentFolder();
        Pane.ToggleExpandCommand.Execute(Pane.VisibleNodes.Single(n => n.Name == "dir"));

        Pane.RefreshCurrentFolder();

        Assert.Equal(new[] { "dir", "inner.txt" }, Visible());
    }

    [Fact]
    public void GetAndRestoreExpandedFolders_RoundTrip_IgnoringMissingPaths()
    {
        File_("one/x.txt");
        File_("two/y.txt");
        Pane.RefreshCurrentFolder();
        Pane.ToggleExpandCommand.Execute(Pane.VisibleNodes.Single(n => n.Name == "one"));
        var saved = Pane.GetExpandedFolderPaths();
        Assert.Equal(new[] { Path.Combine(_host.Root, "one") }, saved);

        Pane.NavigateTo(Path.Combine(_host.Root, "two"));
        Pane.NavigateTo(_host.Root);
        Assert.Equal(new[] { "one", "two" }, Visible()); // 別のフォルダへ移ると、展開状態は戻らない

        Pane.RestoreExpandedFolders(saved.Concat(new[] { Path.Combine(_host.Root, "gone") }));

        Assert.Equal(new[] { "one", "x.txt", "two" }, Visible());
    }

    // ===== 移動・履歴 =====

    [Fact]
    public void NavigateTo_ChangesThePath_RaisesTheEvent_AndRecordsHistory()
    {
        var sub = Dir("sub");
        var raised = new List<string>();
        Pane.PathChanged += raised.Add;

        Pane.NavigateTo(sub);

        Assert.Equal(sub, Pane.CurrentPath);
        Assert.Equal(new[] { sub }, raised);
        Assert.True(Pane.CanGoBack);
        Assert.False(Pane.CanGoForward);
    }

    [Fact]
    public void NavigateTo_TheSamePath_ReloadsWithoutAddingHistory()
    {
        Pane.NavigateTo(_host.Root);

        Assert.False(Pane.CanGoBack);
    }

    [Fact]
    public void BackAndForward_WalkTheHistory()
    {
        var a = Dir("a");
        var b = Dir("b");
        Pane.NavigateTo(a);
        Pane.NavigateTo(b);

        Pane.GoBack();
        Assert.Equal(a, Pane.CurrentPath);
        Pane.GoBack();
        Assert.Equal(_host.Root, Pane.CurrentPath);
        Assert.False(Pane.CanGoBack);
        Assert.True(Pane.CanGoForward);

        Pane.GoForward();
        Assert.Equal(a, Pane.CurrentPath);
        Pane.GoForward();
        Assert.Equal(b, Pane.CurrentPath);
        Assert.False(Pane.CanGoForward);
    }

    [Fact]
    public void NavigatingAfterGoingBack_ClearsTheForwardHistory()
    {
        var a = Dir("a");
        var b = Dir("b");
        Pane.NavigateTo(a);
        Pane.GoBack();

        Pane.NavigateTo(b);

        Assert.False(Pane.CanGoForward);
        Assert.False(Pane.GoForwardCommand.CanExecute(null));
    }

    [Fact]
    public void GoBack_WithNoHistory_DoesNothing()
    {
        Pane.GoBack();
        Pane.GoForward();

        Assert.Equal(_host.Root, Pane.CurrentPath);
    }

    [Fact]
    public void GoUp_MovesToTheParent_AndBackReturns()
    {
        var sub = Dir("sub");
        Pane.NavigateTo(sub);

        Pane.GoUpCommand.Execute(null);

        Assert.Equal(_host.Root, Pane.CurrentPath);
        Pane.GoBack();
        Assert.Equal(sub, Pane.CurrentPath);
    }

    [Fact]
    public void GoUp_FromADriveRoot_GoesToThePcLevel_AndStaysThere()
    {
        var driveRoot = Path.GetPathRoot(_host.Root)!;
        Pane.NavigateTo(driveRoot);

        Pane.GoUpCommand.Execute(null);

        Assert.True(Pane.IsAtComputerRoot);
        Assert.NotEmpty(Pane.VisibleNodes); // ドライブの一覧

        Pane.GoUpCommand.Execute(null);

        Assert.True(Pane.IsAtComputerRoot);
    }

    [Fact]
    public void OpeningAFolder_Navigates_IntoIt()
    {
        Dir("sub");
        Pane.RefreshCurrentFolder();
        Select("sub");

        Pane.OpenCommand.Execute(null);

        Assert.Equal(Path.Combine(_host.Root, "sub"), Pane.CurrentPath);
    }

    private void Select(params string[] names) => _host.Select(names);

    // ===== パンくず =====

    [Fact]
    public void Breadcrumb_ListsPcDriveAndEachFolder()
    {
        var deep = Path.Combine(_host.Root, "x", "y");
        Directory.CreateDirectory(deep);

        Pane.NavigateTo(deep);

        var segments = Pane.BreadcrumbSegments.ToList();
        Assert.Equal("PC", segments[0].DisplayName);
        Assert.Equal("y", segments[^1].DisplayName);
        Assert.Equal(deep, segments[^1].Path);
        Assert.Equal("x", segments[^2].DisplayName);
        Assert.Equal(Path.Combine(_host.Root, "x"), segments[^2].Path);
        Assert.EndsWith(":", segments[1].DisplayName);
    }

    [Fact]
    public void Breadcrumb_Click_Navigates()
    {
        var sub = Dir("sub");
        Pane.NavigateTo(sub);
        var rootSegment = Pane.BreadcrumbSegments.Single(s => s.Path == _host.Root);

        rootSegment.NavigateCommand.Execute(null);

        Assert.Equal(_host.Root, Pane.CurrentPath);
    }

    // ===== アドレスバーの編集 =====

    [Fact]
    public void AddressEdit_Commit_NavigatesToAnExistingFolder()
    {
        var sub = Dir("sub");

        Pane.BeginAddressEditCommand.Execute(null);
        Assert.True(Pane.IsAddressEditing);
        Assert.Equal(_host.Root, Pane.AddressEditText);

        Pane.AddressEditText = sub;
        Pane.CommitAddressEditCommand.Execute(null);

        Assert.False(Pane.IsAddressEditing);
        Assert.Equal(sub, Pane.CurrentPath);
    }

    [Fact]
    public void AddressEdit_Commit_WithAMissingFolder_ShowsAJapaneseError_AndStays()
    {
        Pane.BeginAddressEditCommand.Execute(null);
        Pane.AddressEditText = Path.Combine(_host.Root, "nowhere");

        Pane.CommitAddressEditCommand.Execute(null);

        Assert.Equal(_host.Root, Pane.CurrentPath);
        var message = (string)Assert.Single(_host.DialogControl.ArgsOf("ShowError"))[0]!;
        Assert.Contains("見つかりません", message);
    }

    [Fact]
    public void AddressEdit_Commit_WithBlankText_DoesNothing()
    {
        Pane.BeginAddressEditCommand.Execute(null);
        Pane.AddressEditText = "   ";

        Pane.CommitAddressEditCommand.Execute(null);

        Assert.Equal(_host.Root, Pane.CurrentPath);
        Assert.Equal(0, _host.DialogControl.CountOf("ShowError"));
        Assert.False(Pane.IsAddressEditing);
    }

    [Fact]
    public void AddressEdit_Cancel_LeavesThePathAlone()
    {
        Pane.BeginAddressEditCommand.Execute(null);
        Pane.AddressEditText = "garbage";

        Pane.CancelAddressEditCommand.Execute(null);

        Assert.False(Pane.IsAddressEditing);
        Assert.Equal(_host.Root, Pane.CurrentPath);
    }

    // ===== 読めないフォルダ =====

    [Fact]
    public void NavigatingToAMissingFolder_ShowsAnError_InsteadOfCrashing()
    {
        Pane.NavigateTo(Path.Combine(_host.Root, "vanished"));

        Assert.Equal(1, _host.DialogControl.CountOf("ShowError"));
    }

    // ===== 選択 =====

    [Fact]
    public void Selection_SinglePrimaryIsThatItem_MultiplePrimaryIsTheLast()
    {
        File_("a.txt");
        File_("b.txt");
        File_("c.txt");
        Pane.RefreshCurrentFolder();

        Select("a.txt");
        Assert.Equal("a.txt", Pane.PrimarySelectedNode!.Name);

        Select("a.txt", "c.txt");
        Assert.Equal("c.txt", Pane.PrimarySelectedNode!.Name);
        Assert.Equal(2, Pane.SelectedNodes.Count);
    }

    [Fact]
    public void Selection_Empty_ClearsThePrimary()
    {
        File_("a.txt");
        Pane.RefreshCurrentFolder();
        Select("a.txt");

        Pane.UpdateSelection(Array.Empty<FileSystemNodeViewModel>());

        Assert.Null(Pane.PrimarySelectedNode);
        Assert.Empty(Pane.SelectedNodes);
    }

    [Fact]
    public void Selection_RaisesTheEvent_EachTime()
    {
        File_("a.txt");
        Pane.RefreshCurrentFolder();
        var count = 0;
        Pane.SelectionChanged += () => count++;

        Select("a.txt");
        Pane.UpdateSelection(Array.Empty<FileSystemNodeViewModel>());

        Assert.Equal(2, count);
    }

    // ===== 表示モード =====

    [Fact]
    public void SetViewMode_ChangesTheCurrentMode()
    {
        Pane.SetViewModeCommand.Execute(ViewMode.Tree);

        Assert.Equal(ViewMode.Tree, Pane.CurrentViewMode);

        Pane.SetViewModeCommand.Execute(ViewMode.Detail);

        Assert.Equal(ViewMode.Detail, Pane.CurrentViewMode);
    }
}
