using ExplorerAlternative.Models;
using ExplorerAlternative.Services;
using ExplorerAlternative.Services.Abstractions;

namespace ExplorerAlternative.Tests.Services;

// 仕様書52章「ファイルロック」：削除・移動等に失敗した場合、使用中のプロセスを表示する。
public sealed class FileLockServiceTests : IDisposable
{
    private readonly string _root;
    private readonly FileLockService _sut = new();

    public FileLockServiceTests()
    {
        _root = Directory.CreateTempSubdirectory("eat_lock_").FullName;
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

    private string CreateFile(string name, string? subFolder = null)
    {
        var folder = subFolder is null ? _root : Directory.CreateDirectory(Path.Combine(_root, subFolder)).FullName;
        var path = Path.Combine(folder, name);
        File.WriteAllText(path, "test");
        return path;
    }

    [Fact]
    public void GetLockingProcesses_ReturnsCurrentProcess_WhenFileIsOpenedExclusively()
    {
        var path = CreateFile("locked.txt");
        using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var result = _sut.GetLockingProcesses(new[] { path });

        Assert.Contains(result, p => p.ProcessId == Environment.ProcessId);
    }

    [Fact]
    public void GetLockingProcesses_ReturnsEmpty_WhenFileIsNotInUse()
    {
        var path = CreateFile("free.txt");

        var result = _sut.GetLockingProcesses(new[] { path });

        Assert.Empty(result);
    }

    [Fact]
    public void GetLockingProcesses_ReturnsEmpty_WhenPathDoesNotExist()
    {
        var result = _sut.GetLockingProcesses(new[] { Path.Combine(_root, "missing.txt") });

        Assert.Empty(result);
    }

    // フォルダを指定した場合は、配下のファイルの使用状況を調べる（削除・移動の対象がフォルダのため）。
    [Fact]
    public void GetLockingProcesses_FindsLockedFileInsideFolder()
    {
        var path = CreateFile("inner.txt", subFolder: "sub");
        using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var result = _sut.GetLockingProcesses(new[] { Path.Combine(_root, "sub") });

        Assert.Contains(result, p => p.ProcessId == Environment.ProcessId);
    }

    [Fact]
    public void GetLockingProcesses_ListsEachProcessOnce_WhenSeveralFilesAreLockedBySameProcess()
    {
        var first = CreateFile("a.txt");
        var second = CreateFile("b.txt");
        using var streamA = new FileStream(first, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        using var streamB = new FileStream(second, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var result = _sut.GetLockingProcesses(new[] { first, second });

        Assert.Single(result, p => p.ProcessId == Environment.ProcessId);
    }

    [Fact]
    public void Describe_ReturnsNull_WhenNothingIsUsingTheFile()
    {
        var path = CreateFile("free.txt");

        Assert.Null(_sut.Describe(new[] { path }));
    }

    // 仕様書52章の書式：見出し、空行、プロセス名、PID。
    [Fact]
    public void Format_UsesSpecifiedLayout()
    {
        var text = FileLockService.Format(new[] { new LockingProcess(1234, "chrome.exe", string.Empty) });

        var expected = "このファイルを使用している可能性のあるプロセス：" + Environment.NewLine
            + Environment.NewLine
            + "chrome.exe" + Environment.NewLine
            + "PID: 1234";
        Assert.Equal(expected, text);
    }

    [Fact]
    public void Format_ShowsApplicationName_WhenItDiffersFromExecutable()
    {
        var text = FileLockService.Format(new[] { new LockingProcess(42, "chrome.exe", "Google Chrome") });

        Assert.Contains("chrome.exe（Google Chrome）", text);
    }

    [Fact]
    public void Format_ListsMultipleProcesses()
    {
        var text = FileLockService.Format(new[]
        {
            new LockingProcess(1, "a.exe", string.Empty),
            new LockingProcess(2, "b.exe", string.Empty)
        });

        Assert.Contains("a.exe", text);
        Assert.Contains("PID: 1", text);
        Assert.Contains("b.exe", text);
        Assert.Contains("PID: 2", text);
    }

    [Fact]
    public void Format_FallsBackToApplicationName_WhenExecutableIsUnknown()
    {
        var text = FileLockService.Format(new[] { new LockingProcess(7, string.Empty, "Some App") });

        Assert.Contains("Some App", text);
        Assert.DoesNotContain("（", text);
    }

    // --- FileSystemServiceへの組み込み ---

    [Fact]
    public void FileSystemService_Rename_AppendsLockingProcess_WhenFileIsInUse()
    {
        var path = CreateFile("busy.txt");
        var service = new FileSystemService(_sut);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var ex = Assert.Throws<AppOperationException>(() => service.Rename(path, "renamed.txt"));

        Assert.Contains("名前を変更できませんでした", ex.Message);
        Assert.Contains("このファイルを使用している可能性のあるプロセス", ex.Message);
        Assert.Contains($"PID: {Environment.ProcessId}", ex.Message);
    }

    [Fact]
    public void FileSystemService_Move_AppendsLockingProcess_WhenFileIsInUse()
    {
        var path = CreateFile("busy.txt");
        var destination = Directory.CreateDirectory(Path.Combine(_root, "dest")).FullName;
        var service = new FileSystemService(_sut);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var ex = Assert.Throws<AppOperationException>(() => service.Move(new[] { path }, destination));

        Assert.Contains("移動できませんでした", ex.Message);
        Assert.Contains($"PID: {Environment.ProcessId}", ex.Message);
    }

    // 使用中のプロセスが見つからない失敗では、元のメッセージだけを表示する（余計な文言を付けない）。
    [Fact]
    public void FileSystemService_Failure_KeepsOriginalMessage_WhenNoProcessIsFound()
    {
        var path = CreateFile("busy.txt");
        var service = new FileSystemService(new NoLockService());
        using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var ex = Assert.Throws<AppOperationException>(() => service.Rename(path, "renamed.txt"));

        Assert.Equal("「busy.txt」の名前を変更できませんでした。", ex.Message);
    }

    private sealed class NoLockService : IFileLockService
    {
        public IReadOnlyList<LockingProcess> GetLockingProcesses(IEnumerable<string> paths) => Array.Empty<LockingProcess>();

        public string? Describe(IEnumerable<string> paths) => null;
    }
}
