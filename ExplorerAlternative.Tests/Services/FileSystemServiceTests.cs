using ExplorerAlternative.Services;

namespace ExplorerAlternative.Tests.Services;

// 仕様書20・27・45・49章：ファイル操作の基本動作と、失敗時に例外ではなく日本語の通知になること。
// 実際の一時フォルダで確認する。
public sealed class FileSystemServiceTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("eat_fs_").FullName;
    private readonly FileSystemService _sut = new();

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

    private string File_(string relative, string content = "x")
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    private string Dir_(string relative) => Directory.CreateDirectory(Path.Combine(_root, relative)).FullName;

    // ===== 一覧 =====

    [Fact]
    public void GetChildren_ListsFoldersAndFiles_WithDetails()
    {
        Dir_("sub");
        File_("a.txt", "12345");

        var children = _sut.GetChildren(_root);

        var folder = Assert.Single(children, c => c.IsDirectory);
        Assert.Equal("sub", folder.Name);
        var file = Assert.Single(children, c => !c.IsDirectory);
        Assert.Equal("a.txt", file.Name);
        Assert.Equal(5, file.SizeBytes);
        Assert.NotNull(file.LastModified);
        Assert.NotNull(file.Created);
    }

    [Fact]
    public void GetChildren_DotPrefixedAndHiddenAttributeItems_AreMarkedHidden()
    {
        File_(".env");
        var hidden = File_("h.txt");
        File.SetAttributes(hidden, FileAttributes.Hidden);
        File_("normal.txt");

        var children = _sut.GetChildren(_root).ToDictionary(c => c.Name);

        Assert.True(children[".env"].IsHidden);
        Assert.True(children["h.txt"].IsHidden);
        Assert.False(children["normal.txt"].IsHidden);
    }

    [Fact]
    public void GetChildren_MissingFolder_ThrowsAJapaneseAppException()
    {
        var ex = Assert.Throws<AppOperationException>(() => _sut.GetChildren(Path.Combine(_root, "none")));

        Assert.Contains("見つかりません", ex.Message);
    }

    [Fact]
    public void GetParent_ReturnsTheParent_AndNullForARoot()
    {
        var child = Dir_("c");

        Assert.Equal(_root, _sut.GetParent(child));
        Assert.Null(_sut.GetParent(Path.GetPathRoot(_root)!));
    }

    // ===== 作成 =====

    [Fact]
    public void CreateFile_CreatesAnEmptyFile()
    {
        _sut.CreateFile(_root, "new.txt");

        Assert.True(File.Exists(Path.Combine(_root, "new.txt")));
    }

    [Fact]
    public void CreateFile_WhenItExists_ThrowsAndKeepsTheContent()
    {
        var path = File_("dup.txt", "keep");

        var ex = Assert.Throws<AppOperationException>(() => _sut.CreateFile(_root, "dup.txt"));

        Assert.Contains("既に存在", ex.Message);
        Assert.Equal("keep", File.ReadAllText(path));
    }

    [Fact]
    public void CreateFile_WithInvalidName_ThrowsAnAppException()
    {
        Assert.Throws<AppOperationException>(() => _sut.CreateFile(_root, "bad|name?.txt"));
    }

    [Fact]
    public void CreateDirectory_CreatesTheFolder()
    {
        _sut.CreateDirectory(_root, "made");

        Assert.True(Directory.Exists(Path.Combine(_root, "made")));
    }

    // ===== 名前の変更 =====

    [Fact]
    public void Rename_File_AndFolder()
    {
        var file = File_("old.txt");
        var folder = Dir_("oldDir");

        _sut.Rename(file, "new.txt");
        _sut.Rename(folder, "newDir");

        Assert.True(File.Exists(Path.Combine(_root, "new.txt")));
        Assert.False(File.Exists(file));
        Assert.True(Directory.Exists(Path.Combine(_root, "newDir")));
    }

    [Fact]
    public void Rename_ToAnExistingName_ThrowsAndLeavesBothUntouched()
    {
        var a = File_("a.txt", "A");
        File_("b.txt", "B");

        var ex = Assert.Throws<AppOperationException>(() => _sut.Rename(a, "b.txt"));

        Assert.Contains("名前を変更できませんでした", ex.Message);
        Assert.Equal("A", File.ReadAllText(a));
        Assert.Equal("B", File.ReadAllText(Path.Combine(_root, "b.txt")));
    }

    // ===== コピー =====

    [Fact]
    public void Copy_File_KeepsTheSource()
    {
        var src = File_("s/a.txt", "data");
        var dest = Dir_("d");

        _sut.Copy(new[] { src }, dest);

        Assert.Equal("data", File.ReadAllText(Path.Combine(dest, "a.txt")));
        Assert.True(File.Exists(src));
    }

    [Fact]
    public void Copy_Folder_CopiesTheWholeTree()
    {
        File_("tree/top.txt", "1");
        File_("tree/inner/deep/leaf.txt", "2");
        var dest = Dir_("d");

        _sut.Copy(new[] { Path.Combine(_root, "tree") }, dest);

        Assert.Equal("1", File.ReadAllText(Path.Combine(dest, "tree", "top.txt")));
        Assert.Equal("2", File.ReadAllText(Path.Combine(dest, "tree", "inner", "deep", "leaf.txt")));
    }

    [Fact]
    public void Copy_ToAnExistingName_DoesNotOverwrite_AndThrows()
    {
        var src = File_("s/a.txt", "new");
        var dest = Dir_("d");
        File.WriteAllText(Path.Combine(dest, "a.txt"), "old");

        Assert.Throws<AppOperationException>(() => _sut.Copy(new[] { src }, dest));

        Assert.Equal("old", File.ReadAllText(Path.Combine(dest, "a.txt")));
    }

    [Fact]
    public void Copy_Several_StopsAtTheFirstFailure_KeepingEarlierCopies()
    {
        var first = File_("s/1.txt", "1");
        var second = File_("s/2.txt", "2");
        var dest = Dir_("d");
        File.WriteAllText(Path.Combine(dest, "2.txt"), "exists");

        Assert.Throws<AppOperationException>(() => _sut.Copy(new[] { first, second }, dest));

        Assert.True(File.Exists(Path.Combine(dest, "1.txt")));
    }

    // ===== 移動 =====

    [Fact]
    public void Move_File_AndFolder_RemovesTheSource()
    {
        var file = File_("s/a.txt", "data");
        File_("s/dir/in.txt", "in");
        var dest = Dir_("d");

        _sut.Move(new[] { file, Path.Combine(_root, "s", "dir") }, dest);

        Assert.False(File.Exists(file));
        Assert.Equal("data", File.ReadAllText(Path.Combine(dest, "a.txt")));
        Assert.Equal("in", File.ReadAllText(Path.Combine(dest, "dir", "in.txt")));
    }

    [Fact]
    public void Move_ToAnExistingName_ThrowsAndKeepsTheSource()
    {
        var src = File_("s/a.txt", "new");
        var dest = Dir_("d");
        File.WriteAllText(Path.Combine(dest, "a.txt"), "old");

        Assert.Throws<AppOperationException>(() => _sut.Move(new[] { src }, dest));

        Assert.True(File.Exists(src));
        Assert.Equal("old", File.ReadAllText(Path.Combine(dest, "a.txt")));
    }

    // ===== 複製 =====

    [Fact]
    public void Duplicate_File_NumbersTheNameBeforeTheExtension()
    {
        var src = File_("report.txt", "r");

        var created = _sut.Duplicate(new[] { src });

        Assert.Equal(new[] { Path.Combine(_root, "report (2).txt") }, created);
        Assert.Equal("r", File.ReadAllText(created[0]));
    }

    [Fact]
    public void Duplicate_Twice_GivesTheNextFreeNumber()
    {
        var src = File_("report.txt");

        _sut.Duplicate(new[] { src });
        var second = _sut.Duplicate(new[] { src });

        Assert.Equal(Path.Combine(_root, "report (3).txt"), second[0]);
    }

    [Fact]
    public void Duplicate_Folder_CopiesContents_AndDoesNotTreatTheDotAsAnExtension()
    {
        File_("my.dir/inner.txt", "i");

        var created = _sut.Duplicate(new[] { Path.Combine(_root, "my.dir") });

        Assert.Equal(Path.Combine(_root, "my.dir (2)"), created[0]);
        Assert.True(File.Exists(Path.Combine(created[0], "inner.txt")));
    }

    // ===== 名前を変えてコピー・上書きコピー（仕様書20章） =====

    [Fact]
    public void CopyRenamed_UsesCopySuffix_AndNumbersWhenTaken()
    {
        var src = File_("s/a.txt", "v");
        var dest = Dir_("d");

        var first = _sut.CopyRenamed(src, dest);
        var second = _sut.CopyRenamed(src, dest);

        Assert.Equal(Path.Combine(dest, "a_copy.txt"), first);
        Assert.Equal(Path.Combine(dest, "a_copy2.txt"), second);
        Assert.Equal("v", File.ReadAllText(second));
    }

    [Fact]
    public void CopyReplacing_OverwritesAFile()
    {
        var src = File_("s/a.txt", "new");
        var dest = Dir_("d");
        File.WriteAllText(Path.Combine(dest, "a.txt"), "old");

        _sut.CopyReplacing(src, dest);

        Assert.Equal("new", File.ReadAllText(Path.Combine(dest, "a.txt")));
    }

    [Fact]
    public void CopyReplacing_Folder_RemovesStaleFilesOfTheOldFolder()
    {
        File_("s/dir/new.txt", "n");
        File_("d/dir/stale.txt", "s");

        _sut.CopyReplacing(Path.Combine(_root, "s", "dir"), Path.Combine(_root, "d"));

        Assert.True(File.Exists(Path.Combine(_root, "d", "dir", "new.txt")));
        Assert.False(File.Exists(Path.Combine(_root, "d", "dir", "stale.txt")));
    }

    // ===== フォルダ同期用のコピー =====

    [Fact]
    public void CopyFileTo_CreatesMissingParents_AndOverwrites()
    {
        var src = File_("s.txt", "new");
        var target = Path.Combine(_root, "x", "y", "t.txt");

        _sut.CopyFileTo(src, target);
        File.WriteAllText(src, "newer");
        _sut.CopyFileTo(src, target);

        Assert.Equal("newer", File.ReadAllText(target));
    }

    [Fact]
    public void CopyFileTo_MissingSource_ThrowsAnAppException()
    {
        Assert.Throws<AppOperationException>(() => _sut.CopyFileTo(Path.Combine(_root, "none.txt"), Path.Combine(_root, "t.txt")));
    }

    // ===== ショートカット（.lnk） =====

    [Fact]
    public void CreateShortcuts_MakesLnkFiles_WithUniqueNames()
    {
        var target = File_("s/doc.txt");
        var dest = Dir_("d");

        var first = _sut.CreateShortcuts(new[] { target }, dest);
        var second = _sut.CreateShortcuts(new[] { target }, dest);

        Assert.Equal(Path.Combine(dest, "doc.lnk"), first[0]);
        Assert.Equal(Path.Combine(dest, "doc (2).lnk"), second[0]);
        Assert.True(File.Exists(first[0]));
    }

    // ===== テキストプレビュー =====

    [Fact]
    public void ReadTextPreview_ShortFile_IsNotTruncated()
    {
        var path = File_("t.txt", "hello");

        var text = _sut.ReadTextPreview(path, 100, out var truncated);

        Assert.Equal("hello", text);
        Assert.False(truncated);
    }

    [Fact]
    public void ReadTextPreview_LongFile_IsCutAtTheLimit()
    {
        var path = File_("t.txt", new string('a', 500));

        var text = _sut.ReadTextPreview(path, 100, out var truncated);

        Assert.Equal(100, text.Length);
        Assert.True(truncated);
    }

    [Fact]
    public void ReadTextPreview_FileOpenForWriting_CanStillBeRead()
    {
        var path = File_("busy.txt", "live");
        using var writer = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);

        var text = _sut.ReadTextPreview(path, 100, out _);

        Assert.Equal("live", text);
    }

    [Fact]
    public void ReadTextPreview_MissingFile_ThrowsAnAppException()
    {
        Assert.Throws<AppOperationException>(() => _sut.ReadTextPreview(Path.Combine(_root, "none.txt"), 10, out _));
    }

    // ===== 存在確認・ドライブ =====

    [Fact]
    public void ExistenceChecks_DistinguishFilesAndFolders()
    {
        var file = File_("f.txt");
        var dir = Dir_("d");

        Assert.True(_sut.FileExists(file));
        Assert.False(_sut.FileExists(dir));
        Assert.True(_sut.DirectoryExists(dir));
        Assert.False(_sut.DirectoryExists(file));
    }

    [Fact]
    public void GetDrives_ReturnsReadyDrives_WithTheRootPath()
    {
        var drives = _sut.GetDrives();

        Assert.NotEmpty(drives);
        Assert.All(drives, d =>
        {
            Assert.True(d.IsDirectory);
            Assert.EndsWith("\\", d.FullPath);
            Assert.False(d.Name.EndsWith('\\'));
        });
    }
}
