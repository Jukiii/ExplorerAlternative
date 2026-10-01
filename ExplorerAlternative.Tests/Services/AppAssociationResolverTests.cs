using ExplorerAlternative.Models;
using ExplorerAlternative.Services;

namespace ExplorerAlternative.Tests.Services;

// 仕様書34章：「常にこのアプリで開く」（アプリ内だけの、拡張子ごとの関連付け）。
public sealed class AppAssociationResolverTests
{
    private static AppAssociation Assoc(string extension, string exe) => new() { Extension = extension, ExecutablePath = exe };

    // ===== 拡張子の正規化 =====

    [Theory]
    [InlineData("cs", ".cs")]
    [InlineData(".cs", ".cs")]
    [InlineData(".CS", ".cs")]
    [InlineData("  .Md  ", ".md")]
    [InlineData("Main.CS", ".cs")]
    [InlineData(@"C:\work\src\Main.Cs", ".cs")]
    [InlineData("C:/work/readme.TXT", ".txt")]
    [InlineData("archive.tar.gz", ".gz")]
    public void NormalizeExtension_GivesLowercaseWithLeadingDot(string input, string expected)
    {
        Assert.Equal(expected, AppAssociationResolver.NormalizeExtension(input));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("a b")]
    [InlineData("tar.gz.")]
    [InlineData(".a.b")]
    [InlineData("c:")]
    [InlineData("a?b")]
    [InlineData("a*b")]
    [InlineData(@"C:\work\noextension")]
    public void NormalizeExtension_RejectsInputsThatAreNotExtensions(string? input)
    {
        Assert.Null(AppAssociationResolver.NormalizeExtension(input));
    }

    // ===== 検索 =====

    [Fact]
    public void Find_MatchesByExtension_IgnoringCase()
    {
        var list = new List<AppAssociation> { Assoc(".cs", @"C:\Tools\editor.exe") };

        var found = AppAssociationResolver.Find(list, @"C:\src\Program.CS");

        Assert.NotNull(found);
        Assert.Equal(@"C:\Tools\editor.exe", found!.ExecutablePath);
    }

    [Fact]
    public void Find_ReturnsNull_WhenNoAssociationExists()
    {
        var list = new List<AppAssociation> { Assoc(".cs", @"C:\Tools\editor.exe") };

        Assert.Null(AppAssociationResolver.Find(list, @"C:\src\notes.txt"));
        Assert.Null(AppAssociationResolver.Find(new List<AppAssociation>(), @"C:\src\notes.txt"));
    }

    [Fact]
    public void Find_ReturnsNull_ForFilesWithoutExtension()
    {
        var list = new List<AppAssociation> { Assoc(".cs", @"C:\Tools\editor.exe") };

        Assert.Null(AppAssociationResolver.Find(list, @"C:\src\Makefile"));
    }

    // 保存データの拡張子が、大文字・ドット無しで書かれていても（手で編集された場合など）見つかる。
    [Fact]
    public void Find_ToleratesUnnormalizedStoredExtensions()
    {
        var list = new List<AppAssociation> { Assoc("MD", @"C:\Tools\md.exe") };

        Assert.NotNull(AppAssociationResolver.Find(list, @"C:\docs\README.md"));
    }

    [Fact]
    public void Find_UsesOnlyTheLastExtension()
    {
        var list = new List<AppAssociation> { Assoc(".gz", @"C:\Tools\zip.exe") };

        Assert.NotNull(AppAssociationResolver.Find(list, @"C:\data\archive.tar.gz"));
        Assert.Null(AppAssociationResolver.Find(list, @"C:\data\archive.tar"));
    }

    // ===== 登録 =====

    [Fact]
    public void Set_AddsANewAssociation_WithANormalizedExtension()
    {
        var list = new List<AppAssociation>();

        var extension = AppAssociationResolver.Set(list, "CS", @"C:\Tools\editor.exe");

        Assert.Equal(".cs", extension);
        var added = Assert.Single(list);
        Assert.Equal(".cs", added.Extension);
        Assert.Equal(@"C:\Tools\editor.exe", added.ExecutablePath);
    }

    [Fact]
    public void Set_AcceptsAFilePath_AndUsesItsExtension()
    {
        var list = new List<AppAssociation>();

        AppAssociationResolver.Set(list, @"C:\src\Program.CS", @"C:\Tools\editor.exe");

        Assert.Equal(".cs", Assert.Single(list).Extension);
    }

    [Fact]
    public void Set_ReplacesTheExistingAssociationForTheSameExtension()
    {
        var list = new List<AppAssociation> { Assoc(".cs", @"C:\Old\old.exe") };

        AppAssociationResolver.Set(list, ".CS", @"C:\New\new.exe");

        var only = Assert.Single(list);
        Assert.Equal(@"C:\New\new.exe", only.ExecutablePath);
    }

    [Fact]
    public void Set_KeepsOtherExtensionsUntouched()
    {
        var list = new List<AppAssociation> { Assoc(".md", @"C:\Tools\md.exe") };

        AppAssociationResolver.Set(list, ".cs", @"C:\Tools\editor.exe");

        Assert.Equal(2, list.Count);
        Assert.Equal(@"C:\Tools\md.exe", list.Single(a => a.Extension == ".md").ExecutablePath);
    }

    [Theory]
    [InlineData("", @"C:\Tools\editor.exe")]
    [InlineData("a b", @"C:\Tools\editor.exe")]
    [InlineData(".cs", "")]
    [InlineData(".cs", "   ")]
    public void Set_RejectsInvalidInput_AndChangesNothing(string extension, string exe)
    {
        var list = new List<AppAssociation>();

        var result = AppAssociationResolver.Set(list, extension, exe);

        Assert.Null(result);
        Assert.Empty(list);
    }

    // ===== 解除 =====

    [Fact]
    public void Remove_DeletesTheAssociation_IgnoringCase()
    {
        var list = new List<AppAssociation> { Assoc(".cs", @"C:\Tools\editor.exe"), Assoc(".md", @"C:\Tools\md.exe") };

        var removed = AppAssociationResolver.Remove(list, ".CS");

        Assert.True(removed);
        Assert.Equal(".md", Assert.Single(list).Extension);
    }

    [Fact]
    public void Remove_ReturnsFalse_WhenThereIsNothingToRemove()
    {
        var list = new List<AppAssociation> { Assoc(".md", @"C:\Tools\md.exe") };

        Assert.False(AppAssociationResolver.Remove(list, ".cs"));
        Assert.False(AppAssociationResolver.Remove(list, "a b"));
        Assert.Single(list);
    }
}
