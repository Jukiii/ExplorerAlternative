using ExplorerAlternative.Models;
using ExplorerAlternative.Services;
using ExplorerAlternative.Tests.TestDoubles;

namespace ExplorerAlternative.Tests.Services;

// 仕様書25・27章：設定の保存・読み込み（不正な設定値でも落ちない）と、17章：ワークスペースの保存。
// 利用者の実際の設定（%AppData%）を触らないよう、一時フォルダの設定ファイルで確認する。
public sealed class SettingsServiceTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("eat_settings_").FullName;

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

    private string SettingsPath => Path.Combine(_root, "sub", "settings.json");

    private SettingsService Create() => new(SettingsPath);

    [Fact]
    public void Constructor_CreatesTheFolder_ButNotTheFile()
    {
        Create();

        Assert.True(Directory.Exists(Path.GetDirectoryName(SettingsPath)));
        Assert.False(File.Exists(SettingsPath));
    }

    [Fact]
    public void Load_WithoutAFile_GivesDefaults()
    {
        var sut = Create();

        sut.Load();

        Assert.Empty(sut.Current.TextFileExtensions);
        Assert.Empty(sut.Current.Workspaces);
    }

    [Fact]
    public void SaveThenLoad_RoundTripsValues()
    {
        var first = Create();
        first.Current.TextFileExtensions.Add("xyz");
        first.Current.RecentPlaces.Add(@"C:\work");
        first.Current.Favorites.Add(new FavoriteEntry { Name = "お気に入り", Path = @"D:\data" });
        first.Save();

        var second = Create();
        second.Load();

        Assert.Equal(new[] { "xyz" }, second.Current.TextFileExtensions);
        Assert.Equal(new[] { @"C:\work" }, second.Current.RecentPlaces);
        Assert.Equal("お気に入り", Assert.Single(second.Current.Favorites).Name);
    }

    [Fact]
    public void Save_WritesEnumsByName_NotByNumber()
    {
        var sut = Create();
        sut.Current.View.DefaultViewMode = ViewMode.Tree;
        sut.Save();

        var json = File.ReadAllText(SettingsPath);

        Assert.Contains("\"Tree\"", json);
    }

    [Fact]
    public void Load_BrokenJson_FallsBackToDefaults_WithoutThrowing()
    {
        Create();
        File.WriteAllText(SettingsPath, "{ this is not json");
        var sut = Create();
        sut.Current.TextFileExtensions.Add("stale");

        sut.Load();

        Assert.Empty(sut.Current.TextFileExtensions);
    }

    [Fact]
    public void Load_UnknownEnumName_FallsBackToDefaults()
    {
        Create();
        File.WriteAllText(SettingsPath, "{ \"View\": { \"DefaultViewMode\": \"NoSuchMode\" } }");
        var sut = Create();

        sut.Load();

        Assert.NotNull(sut.Current);
        Assert.Empty(sut.Current.TextFileExtensions);
    }

    [Fact]
    public void Load_WrongTypeForAValue_FallsBackToDefaults()
    {
        Create();
        File.WriteAllText(SettingsPath, "{ \"TextFileExtensions\": 5 }");
        var sut = Create();

        sut.Load();

        Assert.Empty(sut.Current.TextFileExtensions);
    }

    [Fact]
    public void Load_NullDocument_FallsBackToDefaults()
    {
        Create();
        File.WriteAllText(SettingsPath, "null");
        var sut = Create();

        sut.Load();

        Assert.NotNull(sut.Current);
    }

    [Fact]
    public void Load_EmptyFile_FallsBackToDefaults()
    {
        Create();
        File.WriteAllText(SettingsPath, string.Empty);
        var sut = Create();

        sut.Load();

        Assert.NotNull(sut.Current);
    }

    [Fact]
    public void Load_MissingProperties_KeepTheirDefaults()
    {
        Create();
        File.WriteAllText(SettingsPath, "{ \"TextFileExtensions\": [\"abc\"] }");
        var sut = Create();

        sut.Load();

        Assert.Equal(new[] { "abc" }, sut.Current.TextFileExtensions);
        Assert.NotNull(sut.Current.Terminal);
        Assert.NotNull(sut.Current.Appearance);
    }

    [Fact]
    public void Save_WhenTheFileIsLocked_ThrowsAJapaneseAppException()
    {
        var sut = Create();
        sut.Save();
        using var held = new FileStream(SettingsPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var ex = Assert.Throws<AppOperationException>(() => sut.Save());

        Assert.Contains("設定の保存に失敗", ex.Message);
    }

    [Fact]
    public void Save_Overwrites_SoOldItemsDoNotComeBack()
    {
        var sut = Create();
        sut.Current.TextFileExtensions.AddRange(new[] { "a", "b", "c" });
        sut.Save();
        sut.Current.TextFileExtensions.Clear();
        sut.Save();

        var reloaded = Create();
        reloaded.Load();

        Assert.Empty(reloaded.Current.TextFileExtensions);
    }
}

public sealed class WorkspaceServiceTests
{
    private readonly FakeSettingsService _settings = new();
    private readonly WorkspaceService _sut;

    public WorkspaceServiceTests()
    {
        _sut = new WorkspaceService(_settings);
    }

    private static WorkspaceState State(string name, double width = 800) => new() { Name = name, WindowWidth = width };

    [Fact]
    public void Initially_Empty()
    {
        Assert.Empty(_sut.GetWorkspaceNames());
        Assert.Null(_sut.GetWorkspace("none"));
    }

    [Fact]
    public void Save_AddsANewWorkspace_AndPersists()
    {
        _sut.SaveWorkspace(State("作業A"));

        Assert.Equal(new[] { "作業A" }, _sut.GetWorkspaceNames());
        Assert.Equal(1, _settings.SaveCount);
    }

    [Fact]
    public void Save_WithTheSameName_ReplacesInsteadOfDuplicating()
    {
        _sut.SaveWorkspace(State("A", 800));
        _sut.SaveWorkspace(State("B"));
        _sut.SaveWorkspace(State("A", 1200));

        Assert.Equal(new[] { "A", "B" }, _sut.GetWorkspaceNames());
        Assert.Equal(1200, _sut.GetWorkspace("A")!.WindowWidth);
    }

    [Fact]
    public void Names_AreCaseSensitive()
    {
        _sut.SaveWorkspace(State("work"));
        _sut.SaveWorkspace(State("Work"));

        Assert.Equal(2, _sut.GetWorkspaceNames().Count);
    }

    [Fact]
    public void Delete_RemovesOnlyThatWorkspace_AndPersists()
    {
        _sut.SaveWorkspace(State("A"));
        _sut.SaveWorkspace(State("B"));

        _sut.DeleteWorkspace("A");

        Assert.Equal(new[] { "B" }, _sut.GetWorkspaceNames());
        Assert.Equal(3, _settings.SaveCount);
    }

    [Fact]
    public void Delete_UnknownName_ChangesNothing()
    {
        _sut.SaveWorkspace(State("A"));

        _sut.DeleteWorkspace("zzz");

        Assert.Equal(new[] { "A" }, _sut.GetWorkspaceNames());
    }

    [Fact]
    public void Save_WhenPersistingFails_ThePersistenceErrorReachesTheCaller()
    {
        _settings.SaveException = new AppOperationException("設定の保存に失敗しました。");

        Assert.Throws<AppOperationException>(() => _sut.SaveWorkspace(State("A")));
    }
}
