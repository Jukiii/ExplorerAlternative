using ExplorerAlternative.Models;
using ExplorerAlternative.Services;

namespace ExplorerAlternative.Tests.Services;

// 仕様書20章「両方存在する場合は両方を認識する」：.gitと.svnの併存の検出と、主とする方の選択。
public sealed class VersionControlBothTests : IDisposable
{
    private readonly string _root;
    private readonly VersionControlService _sut = new();

    public VersionControlBothTests()
    {
        _root = Directory.CreateTempSubdirectory("eat_vcs_both_").FullName;
    }

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

    private void Marker(string folder, string name) => Directory.CreateDirectory(Path.Combine(folder, name));

    // ===== 両方ある場合 =====

    [Fact]
    public void Both_WithNoPreference_GitIsPrimary_AndSvnIsReportedAsOther()
    {
        Marker(_root, ".git");
        Marker(_root, ".svn");

        var info = _sut.Detect(_root);

        Assert.Equal(VersionControlKind.Git, info.Kind);
        Assert.Equal(_root, info.RootPath);
        Assert.True(info.HasOther);
        Assert.Equal(VersionControlKind.Svn, info.OtherKind);
        Assert.Equal(_root, info.OtherRootPath);
    }

    [Fact]
    public void Both_PreferringSvn_SvnIsPrimary_AndGitIsReportedAsOther()
    {
        Marker(_root, ".git");
        Marker(_root, ".svn");

        var info = _sut.Detect(_root, VersionControlKind.Svn);

        Assert.Equal(VersionControlKind.Svn, info.Kind);
        Assert.Equal(VersionControlKind.Git, info.OtherKind);
        Assert.Equal(_root, info.OtherRootPath);
    }

    [Fact]
    public void Both_PreferringGit_GitIsPrimary()
    {
        Marker(_root, ".git");
        Marker(_root, ".svn");

        var info = _sut.Detect(_root, VersionControlKind.Git);

        Assert.Equal(VersionControlKind.Git, info.Kind);
        Assert.Equal(VersionControlKind.Svn, info.OtherKind);
    }

    // 別々の祖先フォルダにある場合も、両方を認識する（それぞれのルートを持つ）。
    [Fact]
    public void Both_AtDifferentAncestors_KeepTheirOwnRoots()
    {
        var sub = Directory.CreateDirectory(Path.Combine(_root, "sub")).FullName;
        Marker(_root, ".git");
        Marker(sub, ".svn");
        var deep = Directory.CreateDirectory(Path.Combine(sub, "deep")).FullName;

        var gitPrimary = _sut.Detect(deep);
        var svnPrimary = _sut.Detect(deep, VersionControlKind.Svn);

        Assert.Equal(VersionControlKind.Git, gitPrimary.Kind);
        Assert.Equal(_root, gitPrimary.RootPath);
        Assert.Equal(VersionControlKind.Svn, gitPrimary.OtherKind);
        Assert.Equal(sub, gitPrimary.OtherRootPath);

        Assert.Equal(VersionControlKind.Svn, svnPrimary.Kind);
        Assert.Equal(sub, svnPrimary.RootPath);
        Assert.Equal(VersionControlKind.Git, svnPrimary.OtherKind);
        Assert.Equal(_root, svnPrimary.OtherRootPath);
    }

    // ===== 片方だけの場合：従来どおり。「もう一方」は無い =====

    [Fact]
    public void OnlyGit_HasNoOther_EvenWhenSvnIsPreferred()
    {
        Marker(_root, ".git");

        var info = _sut.Detect(_root, VersionControlKind.Svn);

        Assert.Equal(VersionControlKind.Git, info.Kind);
        Assert.False(info.HasOther);
        Assert.Equal(VersionControlKind.None, info.OtherKind);
        Assert.Null(info.OtherRootPath);
    }

    [Fact]
    public void OnlySvn_HasNoOther_EvenWhenGitIsPreferred()
    {
        Marker(_root, ".svn");

        var info = _sut.Detect(_root, VersionControlKind.Git);

        Assert.Equal(VersionControlKind.Svn, info.Kind);
        Assert.False(info.HasOther);
    }

    [Fact]
    public void Neither_IsNone()
    {
        var info = _sut.Detect(_root, VersionControlKind.Svn);

        Assert.Equal(VersionControlKind.None, info.Kind);
        Assert.False(info.HasOther);
    }

    [Fact]
    public void NonexistentPath_IsNone()
    {
        var info = _sut.Detect(Path.Combine(_root, "missing"));

        Assert.Equal(VersionControlKind.None, info.Kind);
    }

    [Fact]
    public void None_HasNoOther()
    {
        Assert.False(VersionControlInfo.None.HasOther);
        Assert.Equal(VersionControlKind.None, VersionControlInfo.None.OtherKind);
    }
}
