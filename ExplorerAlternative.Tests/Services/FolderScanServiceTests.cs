using ExplorerAlternative.Services;

namespace ExplorerAlternative.Tests.Services;

public sealed class FolderScanServiceTests : IDisposable
{
    private readonly string _root;
    private readonly FolderScanService _sut = new();

    public FolderScanServiceTests()
    {
        _root = Directory.CreateTempSubdirectory("eat_scan_").FullName;
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

    // 仕様書59章：直下にファイルがなく、全サブフォルダも再帰的に空である場合のみ「空フォルダ」。
    // 空フォルダのみを含む親フォルダも「空フォルダ」として報告される（意図した仕様、Phase 7で確認済み）。
    [Fact]
    public async Task FindEmptyFoldersAsync_ReportsLeafAndParent_WhenParentOnlyContainsEmptySubfolders()
    {
        var parent = Directory.CreateDirectory(Path.Combine(_root, "parent")).FullName;
        var emptyChild = Directory.CreateDirectory(Path.Combine(parent, "emptyChild")).FullName;

        var result = await _sut.FindEmptyFoldersAsync(_root, CancellationToken.None);

        Assert.Contains(emptyChild, result);
        Assert.Contains(parent, result);
    }

    [Fact]
    public async Task FindEmptyFoldersAsync_DoesNotReportFolder_WhenItContainsAFile()
    {
        var withFile = Directory.CreateDirectory(Path.Combine(_root, "withFile")).FullName;
        File.WriteAllText(Path.Combine(withFile, "a.txt"), "x");

        var result = await _sut.FindEmptyFoldersAsync(_root, CancellationToken.None);

        Assert.DoesNotContain(withFile, result);
    }

    [Fact]
    public async Task FindEmptyFoldersAsync_DoesNotReportAncestor_WhenAnyDescendantHasAFile()
    {
        var parent = Directory.CreateDirectory(Path.Combine(_root, "parent2")).FullName;
        var childWithFile = Directory.CreateDirectory(Path.Combine(parent, "childWithFile")).FullName;
        File.WriteAllText(Path.Combine(childWithFile, "a.txt"), "x");
        Directory.CreateDirectory(Path.Combine(parent, "emptySibling"));

        var result = await _sut.FindEmptyFoldersAsync(_root, CancellationToken.None);

        Assert.DoesNotContain(parent, result);
        Assert.DoesNotContain(childWithFile, result);
        Assert.Contains(Path.Combine(parent, "emptySibling"), result);
    }

    // 仕様書58章：同一サイズ→同一ハッシュでグループ化する。
    [Fact]
    public async Task FindDuplicateFilesAsync_GroupsFilesWithIdenticalContent()
    {
        File.WriteAllText(Path.Combine(_root, "a.txt"), "same content");
        File.WriteAllText(Path.Combine(_root, "b.txt"), "same content");
        File.WriteAllText(Path.Combine(_root, "c.txt"), "different content!!");

        var result = await _sut.FindDuplicateFilesAsync(_root, CancellationToken.None);

        var group = Assert.Single(result);
        Assert.Equal(2, group.Paths.Count);
        Assert.Contains(Path.Combine(_root, "a.txt"), group.Paths);
        Assert.Contains(Path.Combine(_root, "b.txt"), group.Paths);
    }

    [Fact]
    public async Task FindDuplicateFilesAsync_ReturnsNoGroups_WhenAllFilesAreUnique()
    {
        File.WriteAllText(Path.Combine(_root, "a.txt"), "one");
        File.WriteAllText(Path.Combine(_root, "b.txt"), "two");

        var result = await _sut.FindDuplicateFilesAsync(_root, CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task FindDuplicateFilesAsync_DoesNotGroupSameSizeDifferentContent()
    {
        // サイズが同じでも内容が違えばグループ化されない（サイズだけでなくハッシュも見る）ことの検証。
        File.WriteAllText(Path.Combine(_root, "a.txt"), "AAAA");
        File.WriteAllText(Path.Combine(_root, "b.txt"), "BBBB");

        var result = await _sut.FindDuplicateFilesAsync(_root, CancellationToken.None);

        Assert.Empty(result);
    }

    // 仕様書38章：最小サイズ以上のファイルをサイズ降順で返す。
    [Fact]
    public async Task FindLargeFilesAsync_FiltersBySizeAndOrdersDescending()
    {
        File.WriteAllBytes(Path.Combine(_root, "small.bin"), new byte[10]);
        File.WriteAllBytes(Path.Combine(_root, "medium.bin"), new byte[100]);
        File.WriteAllBytes(Path.Combine(_root, "large.bin"), new byte[200]);

        var result = await _sut.FindLargeFilesAsync(_root, minSizeBytes: 50, CancellationToken.None);

        Assert.Equal(2, result.Count);
        Assert.Equal("large.bin", Path.GetFileName(result[0].FullPath));
        Assert.Equal("medium.bin", Path.GetFileName(result[1].FullPath));
    }

    // 仕様書12章：ファイル名・フォルダ名の部分一致検索（大文字小文字を区別しない）。
    [Fact]
    public async Task SearchAsync_MatchesFileAndFolderNamesCaseInsensitively()
    {
        Directory.CreateDirectory(Path.Combine(_root, "TargetFolder"));
        File.WriteAllText(Path.Combine(_root, "target_file.txt"), "x");
        File.WriteAllText(Path.Combine(_root, "unrelated.txt"), "x");

        var result = await _sut.SearchAsync(_root, "target", CancellationToken.None);

        Assert.Equal(2, result.Count);
        Assert.Contains(result, e => e.Name == "TargetFolder" && e.IsDirectory);
        Assert.Contains(result, e => e.Name == "target_file.txt" && !e.IsDirectory);
    }

    [Fact]
    public async Task FindDuplicateFilesAsync_CanBeCancelled()
    {
        for (var i = 0; i < 20; i++)
        {
            File.WriteAllText(Path.Combine(_root, $"f{i}.txt"), "same content for all files");
        }

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _sut.FindDuplicateFilesAsync(_root, cts.Token));
    }
}
