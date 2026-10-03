using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using ExplorerAlternative.Services;
using ExplorerAlternative.Services.Abstractions;
using ExplorerAlternative.Tests.TestDoubles;
using ExplorerAlternative.Views;

namespace ExplorerAlternative.Tests.ViewModels;

// 運営者の依頼（#81）：ファイル・フォルダの Ctrl+C（コピー）・Ctrl+X（切り取り）・Ctrl+V（貼り付け）。
// クリップボードは偽物に差し替えて、利用者の実際のクリップボードを書き換えずに確かめる。
public sealed class PaneClipboardTests : IDisposable
{
    private readonly PaneTestHost _host = new();

    public void Dispose() => _host.Dispose();

    private string Dest() => _host.CreateFolder("dest");

    // ===== コピー・切り取り =====

    [Fact]
    public void Copy_PutsTheSelectedPathsOnTheClipboard_AsACopy()
    {
        var a = _host.CreateFile("a.txt");
        var b = _host.CreateFile("b.txt");
        _host.CreateFile("c.txt");
        _host.Pane.RefreshCurrentFolder();
        _host.Select("a.txt", "b.txt");

        _host.Pane.CopyCommand.Execute(null);

        var content = _host.Clipboard.Content!;
        Assert.False(content.IsMove);
        Assert.Equal(new[] { a, b }.OrderBy(x => x), content.Files.OrderBy(x => x));
    }

    [Fact]
    public void Cut_PutsTheSelectedPathsOnTheClipboard_AsAMove_WithoutMovingAnything()
    {
        var a = _host.CreateFile("a.txt");
        _host.Pane.RefreshCurrentFolder();
        _host.Select("a.txt");

        _host.Pane.CutCommand.Execute(null);

        Assert.True(_host.Clipboard.Content!.IsMove);
        Assert.Equal(new[] { a }, _host.Clipboard.Content.Files);
        Assert.True(File.Exists(a)); // 貼り付けるまで、何も動かない
        Assert.Empty(_host.Queue.Enqueued);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CopyAndCut_AreDisabled_WithoutASelection(bool cut)
    {
        _host.Pane.RefreshCurrentFolder();

        var command = cut ? _host.Pane.CutCommand : _host.Pane.CopyCommand;

        Assert.False(command.CanExecute(null));
    }

    [Fact]
    public void Copy_WhenTheClipboardIsBusy_ShowsAJapaneseError_NotACrash()
    {
        _host.CreateFile("a.txt");
        _host.Pane.RefreshCurrentFolder();
        _host.Select("a.txt");
        _host.Clipboard.Failure = new COMException("busy");

        _host.Pane.CopyCommand.Execute(null);

        var message = (string)Assert.Single(_host.DialogControl.ArgsOf("ShowError"))[0]!;
        Assert.Contains("クリップボード", message);
    }

    // ===== 貼り付け =====

    [Fact]
    public void Paste_AfterCopy_EnqueuesACopyIntoTheCurrentFolder()
    {
        var source = _host.CreateFile("a.txt");
        var destination = Dest();
        _host.Pane.RefreshCurrentFolder();
        _host.Select("a.txt");
        _host.Pane.CopyCommand.Execute(null);
        _host.Pane.NavigateTo(destination);

        _host.Pane.PasteCommand.Execute(null);

        var item = Assert.Single(_host.Queue.Enqueued);
        Assert.False(item.IsMove);
        Assert.Equal(new[] { source }, item.SourcePaths);
        Assert.Equal(destination, item.DestinationFolder);
    }

    [Fact]
    public void Paste_AfterCut_EnqueuesAMove()
    {
        var source = _host.CreateFile("a.txt");
        var destination = Dest();
        _host.Pane.RefreshCurrentFolder();
        _host.Select("a.txt");
        _host.Pane.CutCommand.Execute(null);
        _host.Pane.NavigateTo(destination);

        _host.Pane.PasteCommand.Execute(null);

        var item = Assert.Single(_host.Queue.Enqueued);
        Assert.True(item.IsMove);
        Assert.Equal(new[] { source }, item.SourcePaths);
    }

    [Fact]
    public void Paste_WithNothingOnTheClipboard_DoesNothing()
    {
        _host.Pane.PasteCommand.Execute(null);

        Assert.Empty(_host.Queue.Enqueued);
        Assert.Equal(0, _host.DialogControl.CountOf("ShowError"));
    }

    [Fact]
    public void Paste_WhenTheClipboardIsBusy_ShowsAJapaneseError_NotACrash()
    {
        _host.Clipboard.Failure = new COMException("busy");

        _host.Pane.PasteCommand.Execute(null);

        Assert.Empty(_host.Queue.Enqueued);
        Assert.Contains("クリップボード", (string)Assert.Single(_host.DialogControl.ArgsOf("ShowError"))[0]!);
    }

    [Fact]
    public void Paste_FilesCopiedElsewhere_AreAcceptedAsACopy()
    {
        // エクスプローラーなど、ほかのアプリがコピーしたファイル。
        var source = _host.CreateFile("from-explorer.txt");
        _host.Clipboard.Content = new FileClipboardContent(new[] { source }, IsMove: false);
        var destination = Dest();
        _host.Pane.NavigateTo(destination);

        _host.Pane.PasteCommand.Execute(null);

        Assert.False(Assert.Single(_host.Queue.Enqueued).IsMove);
    }

    [Fact]
    public void Paste_CanBeRepeated_ForTheSameCopy()
    {
        var source = _host.CreateFile("a.txt");
        _host.Pane.RefreshCurrentFolder();
        _host.Select("a.txt");
        _host.Pane.CopyCommand.Execute(null);
        var one = Dest();
        var two = _host.CreateFolder("dest2");

        _host.Pane.NavigateTo(one);
        _host.Pane.PasteCommand.Execute(null);
        _host.Pane.NavigateTo(two);
        _host.Pane.PasteCommand.Execute(null);

        Assert.Equal(2, _host.Queue.Enqueued.Count);
        Assert.Equal(new[] { one, two }, _host.Queue.Enqueued.Select(i => i.DestinationFolder).OrderBy(x => x));
        Assert.True(File.Exists(source));
    }

    // ===== キーの割り当て =====

    [Fact]
    public void Shortcuts_CtrlC_CtrlX_CtrlV_MapToCopyCutPaste()
    {
        var pane = _host.Pane;

        Assert.Same(pane.CopyCommand, FileListShortcuts.ResolveClipboardCommand(pane, Key.C, ModifierKeys.Control));
        Assert.Same(pane.CutCommand, FileListShortcuts.ResolveClipboardCommand(pane, Key.X, ModifierKeys.Control));
        Assert.Same(pane.PasteCommand, FileListShortcuts.ResolveClipboardCommand(pane, Key.V, ModifierKeys.Control));
    }

    [Theory]
    [InlineData(Key.C, ModifierKeys.None)]
    [InlineData(Key.C, ModifierKeys.Shift)]
    [InlineData(Key.C, ModifierKeys.Control | ModifierKeys.Shift)]
    [InlineData(Key.V, ModifierKeys.Control | ModifierKeys.Alt)]
    [InlineData(Key.A, ModifierKeys.Control)]
    [InlineData(Key.D, ModifierKeys.Control)]
    [InlineData(Key.Z, ModifierKeys.Control)]
    public void Shortcuts_OtherKeys_AreNotClipboardCommands(Key key, ModifierKeys modifiers)
    {
        Assert.Null(FileListShortcuts.ResolveClipboardCommand(_host.Pane, key, modifiers));
    }

    // ===== クリップボードの形式（エクスプローラーと同じ） =====

    [Fact]
    public void Format_RoundTrips_PathsAndTheCutFlag()
    {
        var paths = new[] { @"C:\a b\c.txt", @"D:\日本語\資料.docx" };

        var copy = FileClipboardFormat.Read(FileClipboardFormat.CreateDataObject(paths, isCut: false))!;
        var cut = FileClipboardFormat.Read(FileClipboardFormat.CreateDataObject(paths, isCut: true))!;

        Assert.Equal(paths, copy.Files);
        Assert.False(copy.IsMove);
        Assert.Equal(paths, cut.Files);
        Assert.True(cut.IsMove);
    }

    [Fact]
    public void Format_DataFromAnotherApp_WithoutDropEffect_IsTreatedAsACopy()
    {
        var data = new DataObject();
        data.SetFileDropList(new System.Collections.Specialized.StringCollection { @"C:\x.txt" });

        var content = FileClipboardFormat.Read(data)!;

        Assert.False(content.IsMove);
        Assert.Equal(new[] { @"C:\x.txt" }, content.Files);
    }

    [Fact]
    public void Format_DropEffectMove_FromAnotherApp_IsAMove()
    {
        // エクスプローラーで「切り取り」したときの形式（Preferred DropEffect = Move(2)）。
        var data = new DataObject();
        data.SetFileDropList(new System.Collections.Specialized.StringCollection { @"C:\x.txt" });
        data.SetData(FileClipboardFormat.DropEffectFormat, new MemoryStream(BitConverter.GetBytes((int)DragDropEffects.Move)));

        Assert.True(FileClipboardFormat.Read(data)!.IsMove);
    }

    [Fact]
    public void Format_NoFiles_GivesNull()
    {
        Assert.Null(FileClipboardFormat.Read(null));
        Assert.Null(FileClipboardFormat.Read(new DataObject(DataFormats.Text, "text")));
    }

    [Fact]
    public void Format_ADamagedDropEffect_FallsBackToACopy_NotACrash()
    {
        var data = new DataObject();
        data.SetFileDropList(new System.Collections.Specialized.StringCollection { @"C:\x.txt" });
        data.SetData(FileClipboardFormat.DropEffectFormat, new MemoryStream(new byte[] { 1 }));

        Assert.False(FileClipboardFormat.Read(data)!.IsMove);
    }
}
