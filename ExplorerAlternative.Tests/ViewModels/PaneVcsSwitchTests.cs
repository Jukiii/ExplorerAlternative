using ExplorerAlternative.Models;
using ExplorerAlternative.Tests.TestDoubles;

namespace ExplorerAlternative.Tests.ViewModels;

// 仕様書20章：GitとSVNの両方がある場所で、ペインが表示・操作する方を切り替える。
public sealed class PaneVcsSwitchTests : IDisposable
{
    private readonly PaneTestHost _host = new();

    public void Dispose() => _host.Dispose();

    // 判定の偽物：GitとSVNの両方がある場所として振る舞い、指定された方を主として返す。
    private void MakeBothAvailable(List<VersionControlKind>? preferredLog = null)
    {
        _host.VersionControlControl.On("Detect", args =>
        {
            var preferred = (VersionControlKind)args[1]!;
            preferredLog?.Add(preferred);

            return preferred == VersionControlKind.Svn
                ? new VersionControlInfo { Kind = VersionControlKind.Svn, RootPath = _host.Root, OtherKind = VersionControlKind.Git, OtherRootPath = _host.Root }
                : new VersionControlInfo { Kind = VersionControlKind.Git, RootPath = _host.Root, OtherKind = VersionControlKind.Svn, OtherRootPath = _host.Root };
        });

        _host.Pane.RefreshCommand.Execute(null);
    }

    [Fact]
    public void WithoutAnOtherVcs_TheSwitchIsHiddenAndDisabled()
    {
        Assert.False(_host.Pane.HasOtherVcs);
        Assert.Equal(string.Empty, _host.Pane.OtherVcsSwitchLabel);
        Assert.False(_host.Pane.SwitchVcsCommand.CanExecute(null));
    }

    [Fact]
    public void WithBoth_TheSwitchIsShown_WithTheLabelOfTheOtherOne()
    {
        MakeBothAvailable();

        Assert.True(_host.Pane.HasOtherVcs);
        Assert.Equal(VersionControlKind.Git, _host.Pane.VcsInfo.Kind); // 既定はGit
        Assert.Equal("SVNに切り替え", _host.Pane.OtherVcsSwitchLabel);
        Assert.True(_host.Pane.SwitchVcsCommand.CanExecute(null));
    }

    [Fact]
    public void Switching_ShowsTheOtherOne_AndTheLabelFlips()
    {
        MakeBothAvailable();

        _host.Pane.SwitchVcsCommand.Execute(null);

        Assert.Equal(VersionControlKind.Svn, _host.Pane.VcsInfo.Kind);
        Assert.Equal("Gitに切り替え", _host.Pane.OtherVcsSwitchLabel);

        _host.Pane.SwitchVcsCommand.Execute(null);

        Assert.Equal(VersionControlKind.Git, _host.Pane.VcsInfo.Kind);
        Assert.Equal("SVNに切り替え", _host.Pane.OtherVcsSwitchLabel);
    }

    // 切り替えたあとの再読み込み（F5・外部変更の検知・フォルダ移動）でも、選んだ方が保たれる。
    [Fact]
    public void TheChoice_IsKeptAcrossRefreshAndNavigation()
    {
        var preferred = new List<VersionControlKind>();
        MakeBothAvailable(preferred);
        var child = _host.CreateFolder("child");
        _host.Pane.RefreshCommand.Execute(null);

        _host.Pane.SwitchVcsCommand.Execute(null);
        _host.Pane.RefreshCommand.Execute(null);
        _host.Pane.NavigateTo(child);

        Assert.Equal(VersionControlKind.Svn, _host.Pane.VcsInfo.Kind);
        Assert.Equal(VersionControlKind.Svn, preferred.Last());
    }

    [Fact]
    public void TheChoice_IsPerPane()
    {
        MakeBothAvailable();
        using var other = new PaneTestHost();
        other.VersionControlControl.On("Detect", args =>
            (VersionControlKind)args[1]! == VersionControlKind.Svn
                ? new VersionControlInfo { Kind = VersionControlKind.Svn, RootPath = other.Root, OtherKind = VersionControlKind.Git, OtherRootPath = other.Root }
                : new VersionControlInfo { Kind = VersionControlKind.Git, RootPath = other.Root, OtherKind = VersionControlKind.Svn, OtherRootPath = other.Root });
        other.Pane.RefreshCommand.Execute(null);

        _host.Pane.SwitchVcsCommand.Execute(null);

        Assert.Equal(VersionControlKind.Svn, _host.Pane.VcsInfo.Kind);
        Assert.Equal(VersionControlKind.Git, other.Pane.VcsInfo.Kind); // もう一方のペインには影響しない
    }

    [Fact]
    public void Switch_RaisesPropertyChangedForTheSwitchProperties()
    {
        MakeBothAvailable();
        var changed = new List<string?>();
        _host.Pane.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        _host.Pane.SwitchVcsCommand.Execute(null);

        Assert.Contains(nameof(_host.Pane.VcsInfo), changed);
        Assert.Contains(nameof(_host.Pane.HasOtherVcs), changed);
        Assert.Contains(nameof(_host.Pane.OtherVcsSwitchLabel), changed);
    }

    [Fact]
    public void Switch_WithoutAnOther_DoesNothing()
    {
        var before = _host.Pane.VcsInfo;

        _host.Pane.SwitchVcsCommand.Execute(null);

        Assert.Same(before, _host.Pane.VcsInfo);
    }
}
