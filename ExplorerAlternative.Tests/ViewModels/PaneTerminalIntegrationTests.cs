using ExplorerAlternative.Models;
using ExplorerAlternative.Tests.TestDoubles;

namespace ExplorerAlternative.Tests.ViewModels;

// 仕様書19章：ターミナル連携。「SSHターミナルを開く」（右クリック）と、
// 「TerminalからExplorerへのドラッグ」（ターミナルで選んだパスの文字列をペインへドロップ）。
public sealed class PaneTerminalIntegrationTests : IDisposable
{
    private readonly PaneTestHost _host = new();

    public void Dispose() => _host.Dispose();

    private static SshConnectionProfile Profile(string name, string host, string? user = null) =>
        new() { DisplayName = name, Host = host, UserName = user };

    // ===== SSHターミナルを開く =====

    [Fact]
    public void OpenSshTerminal_WithNoProfiles_ExplainsHowToRegisterOne()
    {
        _host.Pane.OpenSshTerminalCommand.Execute(null);

        var message = (string)Assert.Single(_host.DialogControl.ArgsOf("ShowInfo"))[0]!;
        Assert.Contains("登録されていません", message);
        Assert.Equal(0, _host.DialogControl.CountOf("SelectFromList"));
    }

    [Fact]
    public void OpenSshTerminal_ListsRegisteredProfilesWithUserAndHost()
    {
        _host.Settings.Current.SshProfiles = new List<SshConnectionProfile>
        {
            Profile("本番", "prod.example.com", "deploy"),
            Profile("開発", "dev.example.com")
        };
        IReadOnlyList<string>? shown = null;
        _host.DialogControl.On("SelectFromList", args =>
        {
            shown = (IReadOnlyList<string>)args[2]!;
            return null;
        });

        _host.Pane.OpenSshTerminalCommand.Execute(null);

        Assert.Equal(new[] { "本番（deploy@prod.example.com）", "開発（dev.example.com）" }, shown);
    }

    [Fact]
    public void OpenSshTerminal_RequestsConnectionToTheChosenProfile()
    {
        var prod = Profile("本番", "prod.example.com", "deploy");
        var dev = Profile("開発", "dev.example.com");
        _host.Settings.Current.SshProfiles = new List<SshConnectionProfile> { prod, dev };
        _host.DialogControl.On("SelectFromList", args => "開発（dev.example.com）");
        SshConnectionProfile? requested = null;
        _host.Pane.SshTerminalRequested += profile => requested = profile;

        _host.Pane.OpenSshTerminalCommand.Execute(null);

        Assert.Same(dev, requested);
    }

    [Fact]
    public void OpenSshTerminal_Cancelled_DoesNotConnect()
    {
        _host.Settings.Current.SshProfiles = new List<SshConnectionProfile> { Profile("本番", "prod.example.com") };
        _host.DialogControl.On("SelectFromList", args => null);
        var raised = 0;
        _host.Pane.SshTerminalRequested += _ => raised++;

        _host.Pane.OpenSshTerminalCommand.Execute(null);

        Assert.Equal(0, raised);
    }

    // 表示名もホストも同じ接続先が複数あっても、区別して選べる。
    [Fact]
    public void OpenSshTerminal_DuplicateLabels_AreDisambiguated()
    {
        var first = Profile("同名", "same.example.com", "u");
        var second = Profile("同名", "same.example.com", "u");
        _host.Settings.Current.SshProfiles = new List<SshConnectionProfile> { first, second };
        IReadOnlyList<string>? shown = null;
        _host.DialogControl.On("SelectFromList", args =>
        {
            shown = (IReadOnlyList<string>)args[2]!;
            return shown[1]; // 2番目を選ぶ
        });
        SshConnectionProfile? requested = null;
        _host.Pane.SshTerminalRequested += profile => requested = profile;

        _host.Pane.OpenSshTerminalCommand.Execute(null);

        Assert.Equal(2, shown!.Distinct().Count());
        Assert.Same(second, requested);
    }

    // ===== TerminalからExplorerへのドラッグ =====

    [Fact]
    public void DroppedFolderPath_NavigatesThePaneToIt()
    {
        var target = _host.CreateFolder("target");

        var accepted = _host.Pane.NavigateToDroppedPath(target);

        Assert.True(accepted);
        Assert.Equal(target, _host.Pane.CurrentPath);
    }

    [Fact]
    public void DroppedQuotedFolderPathFromTerminalOutput_Navigates()
    {
        var target = _host.CreateFolder("with space");

        var accepted = _host.Pane.NavigateToDroppedPath($"  \"{target}\"  \r\n");

        Assert.True(accepted);
        Assert.Equal(target, _host.Pane.CurrentPath);
    }

    [Fact]
    public void DroppedRelativeFolderName_IsResolvedFromTheCurrentFolder()
    {
        var target = _host.CreateFolder("child");
        _host.Pane.RefreshCommand.Execute(null);

        _host.Pane.NavigateToDroppedPath("child");

        Assert.Equal(target, _host.Pane.CurrentPath);
    }

    [Fact]
    public void DroppedFilePath_OpensItsFolderAndSelectsTheFile()
    {
        var folder = _host.CreateFolder("docs");
        var file = Path.Combine(folder, "report.txt");
        File.WriteAllText(file, "x");

        var accepted = _host.Pane.NavigateToDroppedPath(file);

        Assert.True(accepted);
        Assert.Equal(folder, _host.Pane.CurrentPath);
        var node = _host.Pane.RootNodes.Single(n => n.Name == "report.txt");
        Assert.True(node.IsSelected);
    }

    [Fact]
    public void DroppedFileInTheCurrentFolder_JustSelectsIt_WithoutNavigating()
    {
        var file = _host.CreateFile("here.txt");
        _host.Pane.RefreshCommand.Execute(null);
        var before = _host.Pane.CurrentPath;

        _host.Pane.NavigateToDroppedPath(file);

        Assert.Equal(before, _host.Pane.CurrentPath);
        Assert.True(_host.Pane.RootNodes.Single(n => n.Name == "here.txt").IsSelected);
    }

    [Fact]
    public void DroppedTextThatIsNotAPath_ShowsAMessage_AndDoesNotMove()
    {
        var before = _host.Pane.CurrentPath;

        var accepted = _host.Pane.NavigateToDroppedPath("hello world");

        Assert.False(accepted);
        Assert.Equal(before, _host.Pane.CurrentPath);
        Assert.Equal(1, _host.DialogControl.CountOf("ShowInfo"));
    }

    [Fact]
    public void CanNavigateToDroppedPath_ReflectsWhetherThePathExists()
    {
        var target = _host.CreateFolder("exists");

        Assert.True(_host.Pane.CanNavigateToDroppedPath(target));
        Assert.False(_host.Pane.CanNavigateToDroppedPath(Path.Combine(_host.Root, "missing")));
        Assert.False(_host.Pane.CanNavigateToDroppedPath(null));
        Assert.False(_host.Pane.CanNavigateToDroppedPath("   "));
    }

    // 移動したあとも、戻る操作で、元のフォルダへ戻れる（通常のナビゲーションと同じ履歴に積まれる）。
    [Fact]
    public void NavigatingByDrop_CanBeUndoneWithGoBack()
    {
        var original = _host.Pane.CurrentPath;
        var target = _host.CreateFolder("target");

        _host.Pane.NavigateToDroppedPath(target);
        _host.Pane.GoBack();

        Assert.Equal(original, _host.Pane.CurrentPath);
    }
}
