using ExplorerAlternative.Models;
using ExplorerAlternative.Services;
using ExplorerAlternative.Tests.TestDoubles;

namespace ExplorerAlternative.Tests.ViewModels;

// 仕様書34章：「常にこのアプリで開く」（アプリ内だけの関連付け）。開く操作での使われ方。
public sealed class PaneAppAssociationTests : IDisposable
{
    private readonly PaneTestHost _host = new();

    public void Dispose() => _host.Dispose();

    // 関連付けた「アプリ」として使う、実在するファイル（中身は関係ない。存在確認だけに使う）。
    private string CreateFakeApp(string name = "editor.exe") => _host.CreateFile(name);

    private string AddFile(string name)
    {
        var path = _host.CreateFile(name);
        _host.Pane.RefreshCommand.Execute(null);
        _host.Select(name);
        return path;
    }

    private void Associate(string extension, string exe) =>
        _host.Settings.Current.AppAssociations.Add(new AppAssociation { Extension = extension, ExecutablePath = exe });

    private List<(ExternalToolDefinition Tool, string Target)> ToolRuns() =>
        _host.ExternalToolsControl.ArgsOf("Run").Select(a => ((ExternalToolDefinition)a[0]!, (string)a[1]!)).ToList();

    // ===== 開く（Enter・ダブルクリック） =====

    [Fact]
    public void Open_UsesTheAssociatedApp_WhenOneIsRegisteredForTheExtension()
    {
        var app = CreateFakeApp();
        Associate(".txt", app);
        var file = AddFile("notes.txt");

        _host.Pane.OpenCommand.Execute(null);

        var run = Assert.Single(ToolRuns());
        Assert.Equal(app, run.Tool.ExecutablePath);
        Assert.Equal(file, run.Target);
        Assert.Empty(_host.SystemOpened); // Windowsの既定のアプリでは開かない
    }

    [Fact]
    public void Open_FallsBackToTheSystemDefault_WhenNothingIsAssociated()
    {
        var file = AddFile("notes.txt");

        _host.Pane.OpenCommand.Execute(null);

        Assert.Equal(new[] { file }, _host.SystemOpened);
        Assert.Empty(ToolRuns());
    }

    [Fact]
    public void Open_IsCaseInsensitiveAboutTheExtension()
    {
        var app = CreateFakeApp();
        Associate(".txt", app);
        AddFile("LOUD.TXT");

        _host.Pane.OpenCommand.Execute(null);

        Assert.Equal(app, Assert.Single(ToolRuns()).Tool.ExecutablePath);
    }

    [Fact]
    public void Open_OnlyAppliesToTheAssociatedExtension()
    {
        Associate(".md", CreateFakeApp());
        var file = AddFile("notes.txt");

        _host.Pane.OpenCommand.Execute(null);

        Assert.Equal(new[] { file }, _host.SystemOpened);
        Assert.Empty(ToolRuns());
    }

    // 関連付けたアプリが消えていたら、その旨を伝えたうえで、既定のアプリで開く（開けないままにしない）。
    [Fact]
    public void Open_WhenTheAssociatedAppIsMissing_ExplainsAndOpensWithTheSystemDefault()
    {
        var missing = Path.Combine(_host.Root, "gone", "editor.exe");
        Associate(".txt", missing);
        var file = AddFile("notes.txt");

        _host.Pane.OpenCommand.Execute(null);

        var message = (string)Assert.Single(_host.DialogControl.ArgsOf("ShowInfo"))[0]!;
        Assert.Contains("見つからない", message);
        Assert.Contains(".txt", message);
        Assert.Contains(missing, message);
        Assert.Empty(ToolRuns());
        Assert.Equal(new[] { file }, _host.SystemOpened);
    }

    // アプリの起動に失敗した場合は、エラーを表示して終わる（さらに既定のアプリでも開いたりしない）。
    [Fact]
    public void Open_WhenLaunchingTheAssociatedAppFails_ShowsTheError_WithoutFallingBack()
    {
        Associate(".txt", CreateFakeApp());
        AddFile("notes.txt");
        _host.ExternalToolsControl.On("Run", args => throw new AppOperationException("起動できませんでした"));

        _host.Pane.OpenCommand.Execute(null);

        var message = (string)Assert.Single(_host.DialogControl.ArgsOf("ShowError"))[0]!;
        Assert.Equal("起動できませんでした", message);
        Assert.Empty(_host.SystemOpened);
    }

    [Fact]
    public void Open_Folder_NavigatesAndNeverUsesAnAssociation()
    {
        Associate(".txt", CreateFakeApp());
        var folder = _host.CreateFolder("sub");
        _host.Pane.RefreshCommand.Execute(null);
        _host.Select("sub");

        _host.Pane.OpenCommand.Execute(null);

        Assert.Equal(folder, _host.Pane.CurrentPath);
        Assert.Empty(ToolRuns());
        Assert.Empty(_host.SystemOpened);
    }

    // ===== 「既定のアプリで開く」は、関連付けに関係なく、Windowsの既定 =====

    [Fact]
    public void OpenWithDefaultApp_IgnoresTheAssociation()
    {
        Associate(".txt", CreateFakeApp());
        var file = AddFile("notes.txt");

        _host.Pane.OpenWithDefaultAppCommand.Execute(null);

        Assert.Equal(new[] { file }, _host.SystemOpened);
        Assert.Empty(ToolRuns());
    }

    // ===== 常にこのアプリで開く =====

    [Fact]
    public void OpenWithAlways_RegistersTheAssociation_SavesIt_AndOpensWithThatApp()
    {
        var app = CreateFakeApp();
        var file = AddFile("notes.TXT");
        _host.DialogControl.On("ShowOpenFileDialog", args => app);

        _host.Pane.OpenWithAlwaysCommand.Execute(null);

        var association = Assert.Single(_host.Settings.Current.AppAssociations);
        Assert.Equal(".txt", association.Extension); // 正規化（小文字・ドット付き）
        Assert.Equal(app, association.ExecutablePath);
        Assert.Equal(1, _host.Settings.SaveCount);

        var run = Assert.Single(ToolRuns());
        Assert.Equal(app, run.Tool.ExecutablePath);
        Assert.Equal(file, run.Target);
    }

    [Fact]
    public void OpenWithAlways_ThenOpeningAnotherFileOfTheSameType_UsesTheApp()
    {
        var app = CreateFakeApp();
        AddFile("first.txt");
        _host.DialogControl.On("ShowOpenFileDialog", args => app);
        _host.Pane.OpenWithAlwaysCommand.Execute(null);
        var second = AddFile("second.txt");

        _host.Pane.OpenCommand.Execute(null);

        Assert.Equal(2, ToolRuns().Count);
        Assert.Equal(second, ToolRuns()[1].Target);
    }

    [Fact]
    public void OpenWithAlways_ReplacesAnExistingAssociationForTheSameExtension()
    {
        var oldApp = CreateFakeApp("old.exe");
        var newApp = CreateFakeApp("new.exe");
        Associate(".txt", oldApp);
        AddFile("notes.txt");
        _host.DialogControl.On("ShowOpenFileDialog", args => newApp);

        _host.Pane.OpenWithAlwaysCommand.Execute(null);

        var only = Assert.Single(_host.Settings.Current.AppAssociations);
        Assert.Equal(newApp, only.ExecutablePath);
    }

    [Fact]
    public void OpenWithAlways_Cancelled_ChangesNothing()
    {
        AddFile("notes.txt");
        _host.DialogControl.On("ShowOpenFileDialog", args => null);

        _host.Pane.OpenWithAlwaysCommand.Execute(null);

        Assert.Empty(_host.Settings.Current.AppAssociations);
        Assert.Equal(0, _host.Settings.SaveCount);
        Assert.Empty(ToolRuns());
        Assert.Empty(_host.SystemOpened);
    }

    [Fact]
    public void OpenWithAlways_ForAFileWithoutExtension_ExplainsAndDoesNotAsk()
    {
        AddFile("Makefile");

        _host.Pane.OpenWithAlwaysCommand.Execute(null);

        var message = (string)Assert.Single(_host.DialogControl.ArgsOf("ShowInfo"))[0]!;
        Assert.Contains("拡張子がない", message);
        Assert.Equal(0, _host.DialogControl.CountOf("ShowOpenFileDialog"));
        Assert.Empty(_host.Settings.Current.AppAssociations);
    }

    [Fact]
    public void OpenWithAlways_IsNotAvailable_ForFolders_OrWithoutSelection()
    {
        Assert.False(_host.Pane.OpenWithAlwaysCommand.CanExecute(null));

        _host.CreateFolder("sub");
        _host.Pane.RefreshCommand.Execute(null);
        _host.Select("sub");

        Assert.False(_host.Pane.OpenWithAlwaysCommand.CanExecute(null));
    }

    // 設定の保存に失敗しても、エラーを伝えたうえで、関連付けはこの実行中は有効で、ファイルも開く。
    [Fact]
    public void OpenWithAlways_WhenSavingFails_ShowsTheError_ButStillOpensTheFile()
    {
        var app = CreateFakeApp();
        var file = AddFile("notes.txt");
        _host.DialogControl.On("ShowOpenFileDialog", args => app);
        _host.Settings.SaveException = new AppOperationException("設定の保存に失敗しました。");

        _host.Pane.OpenWithAlwaysCommand.Execute(null);

        var message = (string)Assert.Single(_host.DialogControl.ArgsOf("ShowError"))[0]!;
        Assert.Equal("設定の保存に失敗しました。", message);
        Assert.Single(_host.Settings.Current.AppAssociations);
        Assert.Equal(file, Assert.Single(ToolRuns()).Target);
    }
}