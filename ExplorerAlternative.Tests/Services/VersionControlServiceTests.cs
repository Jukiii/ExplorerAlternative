using ExplorerAlternative.Models;
using ExplorerAlternative.Services;

namespace ExplorerAlternative.Tests.Services;

// 仕様書20章「現在パスから親方向へ探索」。過去に下位階層を探索してしまう不具合があったため、
// 祖先方向の探索と、下位階層を誤検出しないことの両方を検証する。
public sealed class VersionControlServiceTests : IDisposable
{
    private readonly string _root;
    private readonly VersionControlService _sut = new();

    public VersionControlServiceTests()
    {
        _root = Directory.CreateTempSubdirectory("eat_vcs_").FullName;
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

    [Fact]
    public void Detect_FindsGitRoot_WhenCurrentPathIsDeepDescendant()
    {
        Directory.CreateDirectory(Path.Combine(_root, ".git"));
        var deep = Directory.CreateDirectory(Path.Combine(_root, "a", "b", "c")).FullName;

        var result = _sut.Detect(deep);

        Assert.Equal(VersionControlKind.Git, result.Kind);
        Assert.Equal(_root, result.RootPath);
    }

    [Fact]
    public void Detect_ReturnsNone_WhenGitOnlyExistsInDescendant()
    {
        // 祖先方向のみ探索する（下位階層は探索しない）ことの検証。
        var current = Directory.CreateDirectory(Path.Combine(_root, "current")).FullName;
        Directory.CreateDirectory(Path.Combine(current, "nested", ".git"));

        var result = _sut.Detect(current);

        Assert.Equal(VersionControlKind.None, result.Kind);
    }

    [Fact]
    public void Detect_ReturnsNone_WhenNoMarkerAnywhere()
    {
        var leaf = Directory.CreateDirectory(Path.Combine(_root, "x", "y")).FullName;

        var result = _sut.Detect(leaf);

        Assert.Equal(VersionControlKind.None, result.Kind);
    }

    [Fact]
    public void Detect_PrefersGitOverSvn_WhenBothMarkersExistAtSameLevel()
    {
        Directory.CreateDirectory(Path.Combine(_root, ".git"));
        Directory.CreateDirectory(Path.Combine(_root, ".svn"));

        var result = _sut.Detect(_root);

        Assert.Equal(VersionControlKind.Git, result.Kind);
    }

    [Fact]
    public void Detect_FindsSvnRoot_WhenCurrentPathIsDeepDescendant()
    {
        Directory.CreateDirectory(Path.Combine(_root, ".svn"));
        var deep = Directory.CreateDirectory(Path.Combine(_root, "a", "b")).FullName;

        var result = _sut.Detect(deep);

        Assert.Equal(VersionControlKind.Svn, result.Kind);
        Assert.Equal(_root, result.RootPath);
    }
}
