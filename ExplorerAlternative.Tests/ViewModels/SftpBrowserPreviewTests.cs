using ExplorerAlternative.Models;
using ExplorerAlternative.Services;
using ExplorerAlternative.Services.Abstractions;
using ExplorerAlternative.Tests.TestDoubles;
using ExplorerAlternative.ViewModels;

namespace ExplorerAlternative.Tests.ViewModels;

// 仕様書44章「SFTP・プレビュー」：リモートのファイルを、一時フォルダへダウンロードして、通常のプレビューで表示する。
// SFTPのセッションと画面は偽物、一時ファイルの作成・削除は実際のフォルダで確認する。
public sealed class SftpBrowserPreviewTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("eat_sftp_").FullName;
    private readonly string _previewRoot;
    private readonly IDialogService _dialog = StubProxy.Create<IDialogService>();
    private readonly StubProxy _dialogControl;
    private readonly StubProxy _sessionControl;
    private readonly SftpBrowserViewModel _sut;
    private readonly List<string> _factoryCalls = new();
    private readonly List<PreviewViewModel> _previews = new();
    private bool _factoryReturnsNull;
    private string _downloadContent = "リモートの内容";

    public SftpBrowserPreviewTests()
    {
        _previewRoot = Path.Combine(_root, "previews");
        _dialogControl = StubProxy.Of(_dialog);

        var session = StubProxy.Create<ISftpSession>();
        _sessionControl = StubProxy.Of(session);
        _sessionControl.On("get_HomeDirectory", _ => "/home/user");
        _sessionControl.On("ListDirectory", _ => (IReadOnlyList<RemoteFileEntry>)new[]
        {
            Remote("notes.txt", 100),
            Remote("big.bin", SftpBrowserViewModel.MaxPreviewBytes + 1),
            Remote("folder", 0, isDirectory: true),
            Remote("evil\\name.txt", 10)
        });
        _sessionControl.On("DownloadFile", args =>
        {
            var remote = (string)args[0]!;
            var localDirectory = (string)args[1]!;
            File.WriteAllText(Path.Combine(localDirectory, SftpService.GetSafeLocalFileName(remote)), _downloadContent);
            return null;
        });

        var service = StubProxy.Create<ISftpService>();
        StubProxy.Of(service).On("Connect", _ => session);

        _sut = new SftpBrowserViewModel(
            new SshConnectionProfile { DisplayName = "test", Host = "example", Port = 22 },
            service, password: null, _dialog, CreatePreview, _previewRoot);
    }

    public void Dispose()
    {
        _sut.Dispose();

        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private static RemoteFileEntry Remote(string name, long size, bool isDirectory = false) => new()
    {
        Name = name,
        FullPath = "/home/user/" + name,
        IsDirectory = isDirectory,
        SizeBytes = size,
        LastModifiedUtc = DateTime.UtcNow
    };

    private PreviewViewModel? CreatePreview(string localPath)
    {
        _factoryCalls.Add(localPath);

        if (_factoryReturnsNull)
        {
            return null;
        }

        var fileSystem = new FileSystemService();
        var entry = fileSystem.GetChildren(Path.GetDirectoryName(localPath)!).Single(e => e.FullPath == localPath);
        var node = new FileSystemNodeViewModel(entry, 0, fileSystem, _dialog, new FakeSettingsService());
        var preview = PreviewViewModel.Create(
            node,
            fileSystem,
            StubProxy.Create<IVersionControlService>(),
            new FakeSettingsService(),
            StubProxy.Create<IFolderScanService>(),
            StubProxy.Create<IPdfRenderService>());
        _previews.Add(preview);
        return preview;
    }

    private void Select(string name) => _sut.SelectedEntry = _sut.Entries.Single(e => e.Name == name);

    private string[] PreviewFolders() =>
        Directory.Exists(_previewRoot) ? Directory.GetDirectories(_previewRoot) : Array.Empty<string>();

    // ===== 表示 =====

    [Fact]
    public void Preview_DownloadsToATemporaryFolder_AndShowsTheLocalCopy()
    {
        Select("notes.txt");

        _sut.PreviewCommand.Execute(null);

        var local = Assert.Single(_factoryCalls);
        Assert.StartsWith(_previewRoot, local);
        Assert.Equal("notes.txt", Path.GetFileName(local));
        Assert.Equal("リモートの内容", File.ReadAllText(local));
        var shown = (PreviewViewModel)Assert.Single(_dialogControl.ArgsOf("ShowPreview"))[0]!;
        Assert.Equal(PreviewKind.Text, shown.Kind);
        Assert.Contains("リモートの内容", shown.TextContent);
    }

    [Fact]
    public void EachPreview_UsesItsOwnFolder_SoSameNamedFilesDoNotCollide()
    {
        Select("notes.txt");

        _sut.PreviewCommand.Execute(null);
        _downloadContent = "二回目";
        _sut.PreviewCommand.Execute(null);

        Assert.Equal(2, PreviewFolders().Length);
        Assert.NotEqual(_factoryCalls[0], _factoryCalls[1]);
    }

    // ===== できないとき =====

    [Fact]
    public void Command_IsDisabled_ForFolders_AndWithoutASelection()
    {
        Assert.False(_sut.PreviewCommand.CanExecute(null));

        Select("folder");
        Assert.False(_sut.PreviewCommand.CanExecute(null));

        Select("notes.txt");
        Assert.True(_sut.PreviewCommand.CanExecute(null));
    }

    [Fact]
    public void Command_IsDisabled_WhenNoPreviewFactoryWasGiven()
    {
        var service = StubProxy.Create<ISftpService>();
        StubProxy.Of(service).On("Connect", _ => throw new AppOperationException("接続できません"));
        using var plain = new SftpBrowserViewModel(
            new SshConnectionProfile { DisplayName = "x", Host = "h", Port = 22 }, service, null, _dialog);

        Assert.False(plain.PreviewCommand.CanExecute(null));
    }

    [Fact]
    public void BigFile_IsNotDownloaded_AndTheUserIsToldToDownloadIt()
    {
        Select("big.bin");

        _sut.PreviewCommand.Execute(null);

        Assert.Empty(_factoryCalls);
        Assert.Equal(0, _sessionControl.CountOf("DownloadFile"));
        var message = (string)Assert.Single(_dialogControl.ArgsOf("ShowInfo"))[0]!;
        Assert.Contains("ダウンロードしてから", message);
        Assert.Empty(PreviewFolders());
    }

    [Fact]
    public void DownloadFailure_ShowsTheError_AndLeavesNoTemporaryFolder()
    {
        _sessionControl.On("DownloadFile", _ => throw new AppOperationException("ダウンロードに失敗しました。"));
        Select("notes.txt");

        _sut.PreviewCommand.Execute(null);

        Assert.Equal("ダウンロードに失敗しました。", (string)Assert.Single(_dialogControl.ArgsOf("ShowError"))[0]!);
        Assert.Equal(0, _dialogControl.CountOf("ShowPreview"));
        Assert.Empty(PreviewFolders());
    }

    [Fact]
    public void FileNameThatCouldEscapeTheFolder_IsRefused()
    {
        Select("evil\\name.txt");

        _sut.PreviewCommand.Execute(null);

        Assert.Equal(1, _dialogControl.CountOf("ShowError"));
        Assert.Equal(0, _dialogControl.CountOf("ShowPreview"));
        Assert.False(File.Exists(Path.Combine(_previewRoot, "name.txt")));
        Assert.Empty(PreviewFolders());
    }

    [Fact]
    public void FactoryReturningNull_ShowsAnError_AndRemovesTheDownload()
    {
        _factoryReturnsNull = true;
        Select("notes.txt");

        _sut.PreviewCommand.Execute(null);

        Assert.Equal(1, _dialogControl.CountOf("ShowError"));
        Assert.Empty(PreviewFolders());
    }

    // ===== 後始末 =====

    [Fact]
    public void ClosingThePreview_FromTheKeyboard_ClosesTheWindow_AndDeletesTheTemporaryFile()
    {
        Select("notes.txt");
        _sut.PreviewCommand.Execute(null);
        var shown = (PreviewViewModel)Assert.Single(_dialogControl.ArgsOf("ShowPreview"))[0]!;
        Assert.Single(PreviewFolders());

        shown.RequestClose!.Invoke();

        Assert.Equal(1, _dialogControl.CountOf("ClosePreview"));
        Assert.Empty(PreviewFolders());
    }

    [Fact]
    public void ClosingTheWindow_WithTheCloseButton_AlsoDeletesTheTemporaryFile()
    {
        Select("notes.txt");
        _sut.PreviewCommand.Execute(null);
        var shown = (PreviewViewModel)Assert.Single(_dialogControl.ArgsOf("ShowPreview"))[0]!;

        shown.Closed!.Invoke();

        Assert.Empty(PreviewFolders());
    }

    [Fact]
    public void ClosingTheBrowser_DeletesTemporaryFilesOfPreviewsStillOpen()
    {
        Select("notes.txt");
        _sut.PreviewCommand.Execute(null);
        _sut.PreviewCommand.Execute(null);
        Assert.Equal(2, PreviewFolders().Length);

        _sut.Dispose();

        Assert.Empty(PreviewFolders());
    }

    [Fact]
    public void TemporaryFileStillOpenElsewhere_DoesNotCrashTheCleanup()
    {
        Select("notes.txt");
        _sut.PreviewCommand.Execute(null);
        using var held = new FileStream(_factoryCalls[0], FileMode.Open, FileAccess.Read, FileShare.Read);
        var shown = (PreviewViewModel)Assert.Single(_dialogControl.ArgsOf("ShowPreview"))[0]!;

        shown.Closed!.Invoke(); // 消せなくても、例外にしない

        Assert.True(File.Exists(_factoryCalls[0]));
    }

    // ===== ダウンロード先のファイル名 =====

    [Theory]
    [InlineData("/home/user/notes.txt", "notes.txt")]
    [InlineData("/a/b/日本語.md", "日本語.md")]
    [InlineData("name-only.txt", "name-only.txt")]
    public void SafeLocalFileName_TakesTheLastSegment(string remote, string expected)
    {
        Assert.Equal(expected, SftpService.GetSafeLocalFileName(remote));
    }

    [Theory]
    [InlineData("/home/user/..\\..\\evil.txt")]
    [InlineData("/home/user/a\\b.txt")]
    [InlineData("/home/user/")]
    [InlineData("/home/user/..")]
    [InlineData("/home/user/.")]
    [InlineData("/home/user/con:fig")]
    [InlineData("")]
    public void SafeLocalFileName_RefusesNamesThatCannotBeSavedSafely(string remote)
    {
        Assert.Throws<AppOperationException>(() => SftpService.GetSafeLocalFileName(remote));
    }
}
