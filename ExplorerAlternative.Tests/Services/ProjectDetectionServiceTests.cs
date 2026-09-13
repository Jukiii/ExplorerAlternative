using ExplorerAlternative.Services;

namespace ExplorerAlternative.Tests.Services;

// 仕様書54章「プロジェクト認識」・56章「プロジェクトルート」。
public sealed class ProjectDetectionServiceTests : IDisposable
{
    private readonly string _root;
    private readonly ProjectDetectionService _sut = new();

    public ProjectDetectionServiceTests()
    {
        _root = Directory.CreateTempSubdirectory("eat_proj_").FullName;
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
    public void Detect_FindsNearestCsprojAncestor()
    {
        var projectDir = Directory.CreateDirectory(Path.Combine(_root, "MyApp")).FullName;
        File.WriteAllText(Path.Combine(projectDir, "MyApp.csproj"), "<Project />");
        var deep = Directory.CreateDirectory(Path.Combine(projectDir, "src", "sub")).FullName;

        var result = _sut.Detect(deep);

        Assert.NotNull(result);
        Assert.Equal(projectDir, result!.RootPath);
        Assert.Equal("MyApp.csproj", result.MarkerFile);
        Assert.Equal("MyApp", result.Name);
    }

    [Fact]
    public void Detect_FindsPackageJson()
    {
        File.WriteAllText(Path.Combine(_root, "package.json"), "{}");

        var result = _sut.Detect(_root);

        Assert.NotNull(result);
        Assert.Equal("package.json", result!.MarkerFile);
    }

    [Fact]
    public void Detect_ReturnsNull_WhenNoMarkerAnywhere()
    {
        var leaf = Directory.CreateDirectory(Path.Combine(_root, "x", "y")).FullName;

        var result = _sut.Detect(leaf);

        Assert.Null(result);
    }

    [Fact]
    public void Detect_DoesNotSearchDescendants()
    {
        var current = Directory.CreateDirectory(Path.Combine(_root, "current")).FullName;
        var nested = Directory.CreateDirectory(Path.Combine(current, "nested")).FullName;
        File.WriteAllText(Path.Combine(nested, "go.mod"), "module x");

        var result = _sut.Detect(current);

        Assert.Null(result);
    }

    [Fact]
    public void FindSolutionRoot_FindsClassicSlnFile()
    {
        File.WriteAllText(Path.Combine(_root, "MyApp.sln"), "");
        var deep = Directory.CreateDirectory(Path.Combine(_root, "src")).FullName;

        var result = _sut.FindSolutionRoot(deep);

        Assert.Equal(_root, result);
    }

    [Fact]
    public void FindSolutionRoot_FindsNewSlnxFile()
    {
        // 本プロジェクト自身が採用している新形式（.sln未満のケースをカバーするための拡張）。
        File.WriteAllText(Path.Combine(_root, "MyApp.slnx"), "");
        var deep = Directory.CreateDirectory(Path.Combine(_root, "src")).FullName;

        var result = _sut.FindSolutionRoot(deep);

        Assert.Equal(_root, result);
    }

    [Fact]
    public void FindSolutionRoot_ReturnsNull_WhenNoSolutionFileExists()
    {
        File.WriteAllText(Path.Combine(_root, "MyApp.csproj"), "<Project />");

        var result = _sut.FindSolutionRoot(_root);

        Assert.Null(result);
    }
}
