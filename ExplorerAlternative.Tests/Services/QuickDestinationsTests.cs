using ExplorerAlternative.Models;
using ExplorerAlternative.Services;

namespace ExplorerAlternative.Tests.Services;

// 仕様書60章：クイックコピー/移動の「最近の場所」を、操作履歴から求める。
public sealed class QuickDestinationsTests
{
    private static FileOperationHistoryEntry Entry(string operation, string? destination, bool success = true) =>
        new() { Operation = operation, Target = "x", Destination = destination, Success = success, Timestamp = DateTime.Now };

    private static bool AllExist(string _) => true;

    [Fact]
    public void GetRecent_ReturnsCopyDestinationsNewestFirst()
    {
        var history = new[]
        {
            Entry("コピー", @"C:\Work"),
            Entry("コピー", @"D:\Backup"),
            Entry("コピー", @"C:\Project")
        };

        var result = QuickDestinations.GetRecent(history, isMove: false, directoryExists: AllExist);

        Assert.Equal(new[] { @"C:\Work", @"D:\Backup", @"C:\Project" }, result);
    }

    [Fact]
    public void GetRecent_SeparatesCopyFromMove()
    {
        var history = new[]
        {
            Entry("コピー", @"C:\CopyHere"),
            Entry("移動", @"C:\MoveHere")
        };

        Assert.Equal(new[] { @"C:\CopyHere" }, QuickDestinations.GetRecent(history, isMove: false, directoryExists: AllExist));
        Assert.Equal(new[] { @"C:\MoveHere" }, QuickDestinations.GetRecent(history, isMove: true, directoryExists: AllExist));
    }

    [Fact]
    public void GetRecent_IgnoresOtherOperations()
    {
        var history = new[]
        {
            Entry("名前変更", @"C:\a"),
            Entry("複製", @"C:\b"),
            Entry("削除", @"C:\c"),
            Entry("新規作成", @"C:\d")
        };

        Assert.Empty(QuickDestinations.GetRecent(history, isMove: false, directoryExists: AllExist));
        Assert.Empty(QuickDestinations.GetRecent(history, isMove: true, directoryExists: AllExist));
    }

    [Fact]
    public void GetRecent_SkipsFailedOperations()
    {
        var history = new[]
        {
            Entry("コピー", @"C:\Failed", success: false),
            Entry("コピー", @"C:\Ok")
        };

        Assert.Equal(new[] { @"C:\Ok" }, QuickDestinations.GetRecent(history, isMove: false, directoryExists: AllExist));
    }

    [Fact]
    public void GetRecent_MergesDuplicatesIgnoringCaseAndTrailingSeparator()
    {
        var history = new[]
        {
            Entry("コピー", @"C:\Work"),
            Entry("コピー", @"c:\work\"),
            Entry("コピー", @"C:\WORK"),
            Entry("コピー", @"D:\Other")
        };

        var result = QuickDestinations.GetRecent(history, isMove: false, directoryExists: AllExist);

        // 最初に出てきた表記（最新の履歴）を採用し、1件にまとめる。
        Assert.Equal(new[] { @"C:\Work", @"D:\Other" }, result);
    }

    [Fact]
    public void GetRecent_ExcludesCurrentFolder()
    {
        var history = new[]
        {
            Entry("コピー", @"C:\Current"),
            Entry("コピー", @"C:\Elsewhere")
        };

        var result = QuickDestinations.GetRecent(history, isMove: false, excludeFolder: @"c:\current\", directoryExists: AllExist);

        Assert.Equal(new[] { @"C:\Elsewhere" }, result);
    }

    [Fact]
    public void GetRecent_SkipsFoldersThatNoLongerExist()
    {
        var history = new[]
        {
            Entry("コピー", @"C:\Gone"),
            Entry("コピー", @"C:\Here")
        };

        var result = QuickDestinations.GetRecent(history, isMove: false, directoryExists: path => path == @"C:\Here");

        Assert.Equal(new[] { @"C:\Here" }, result);
    }

    [Fact]
    public void GetRecent_RespectsMaxCount()
    {
        var history = Enumerable.Range(0, 20).Select(i => Entry("コピー", $@"C:\Dir{i}")).ToList();

        var result = QuickDestinations.GetRecent(history, isMove: false, maxCount: 5, directoryExists: AllExist);

        Assert.Equal(5, result.Count);
        Assert.Equal(@"C:\Dir0", result[0]);
        Assert.Equal(@"C:\Dir4", result[4]);
    }

    [Fact]
    public void GetRecent_DefaultLimitIsTen()
    {
        var history = Enumerable.Range(0, 25).Select(i => Entry("移動", $@"C:\Dir{i}")).ToList();

        Assert.Equal(QuickDestinations.DefaultMaxCount, QuickDestinations.GetRecent(history, isMove: true, directoryExists: AllExist).Count);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void GetRecent_IgnoresEntriesWithoutDestination(string? destination)
    {
        var history = new[] { Entry("コピー", destination), Entry("コピー", @"C:\Ok") };

        Assert.Equal(new[] { @"C:\Ok" }, QuickDestinations.GetRecent(history, isMove: false, directoryExists: AllExist));
    }

    [Fact]
    public void GetRecent_EmptyHistory_ReturnsEmpty()
    {
        Assert.Empty(QuickDestinations.GetRecent(Array.Empty<FileOperationHistoryEntry>(), isMove: false, directoryExists: AllExist));
    }

    [Fact]
    public void GetRecent_KeepsDriveRootIntact()
    {
        var history = new[] { Entry("コピー", @"D:\") };

        var result = QuickDestinations.GetRecent(history, isMove: false, directoryExists: AllExist);

        Assert.Equal(new[] { @"D:\" }, result);
    }

    // 実在確認の既定動作（Directory.Exists）：実際にあるフォルダだけが残る。
    [Fact]
    public void GetRecent_DefaultExistenceCheck_UsesTheRealFileSystem()
    {
        var existing = Directory.CreateTempSubdirectory("eat_quick_").FullName;
        try
        {
            var history = new[]
            {
                Entry("コピー", Path.Combine(existing, "missing")),
                Entry("コピー", existing)
            };

            var result = QuickDestinations.GetRecent(history, isMove: false);

            Assert.Equal(new[] { existing }, result);
        }
        finally
        {
            Directory.Delete(existing, recursive: true);
        }
    }
}
