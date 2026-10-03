using System.Collections.Specialized;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using ExplorerAlternative.Tests.TestDoubles;
using ExplorerAlternative.ViewModels;
using ExplorerAlternative.Views;

namespace ExplorerAlternative.Tests.Views;

// 仕様書17・19章：ターミナル画面への、ファイルのドラッグ&ドロップ（パスの入力）と、Ctrl+C（中断）・Ctrl+Shift+C（コピー）。
public sealed class TerminalSurfaceInputTests
{
    private sealed class Fixture
    {
        public RichTextBox Box { get; } = new() { AllowDrop = true, IsReadOnly = true };

        public TerminalSurfaceController Controller { get; }

        public TerminalViewModel Terminal { get; }

        public FakeTerminalService Service { get; } = new();

        public List<string> Copied { get; } = new();

        public Fixture()
        {
            Controller = new TerminalSurfaceController(Box) { SetClipboardText = Copied.Add };
            Terminal = new TerminalViewModel(Service, syncByDefault: false, "PowerShell 1");
            Controller.Bind(Terminal);
        }

        // ターミナルの出力として表示した文字を、そのまま選択する（マウスで文字をなぞった状態と同じ）。
        public void Select(string text)
        {
            Service.RaiseOutput(text);

            var paragraph = Box.Document.Blocks.OfType<Paragraph>().First();
            var run = paragraph.Inlines.OfType<Run>().First(r => r.Text == text);
            Box.Selection.Select(run.ContentStart, run.ContentEnd);
        }
    }

    // DragEventArgsのコンストラクターは非公開のため、リフレクションで作る（実際のドラッグと同じ引数）。
    private static DragEventArgs MakeDragArgs(IDataObject data, RoutedEvent routedEvent, DependencyObject target)
    {
        var constructor = typeof(DragEventArgs).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic).Single();
        var args = (DragEventArgs)constructor.Invoke(new object?[]
        {
            data, DragDropKeyStates.None, DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link, target, new Point(5, 5)
        });
        args.RoutedEvent = routedEvent;
        return args;
    }

    private static DataObject Files(params string[] paths)
    {
        var data = new DataObject();
        data.SetFileDropList(new StringCollection { paths.Length == 0 ? @"C:\x" : paths[0] });
        return data;
    }

    // ===== ドラッグ&ドロップ =====

    // 読み取り専用のRichTextBoxは、通常のDragOverで、内蔵の処理が先に「不可（None）」と答えて処理済みにする。
    // その結果、ドラッグ中に禁止マークになっていた（運営者の指摘）。Previewのイベントで、先に受ける。
    [Fact]
    public void DraggingFilesOver_ShowsACopyCursor_NotTheForbiddenOne()
    {
        StaTest.Run(() =>
        {
            var fixture = new Fixture();
            var args = MakeDragArgs(Files(), UIElement.PreviewDragOverEvent, fixture.Box);

            fixture.Box.RaiseEvent(args);

            Assert.Equal(DragDropEffects.Copy, args.Effects);
            Assert.True(args.Handled);
        });
    }

    [Fact]
    public void DragEnter_AlsoShowsACopyCursor()
    {
        StaTest.Run(() =>
        {
            var fixture = new Fixture();
            var args = MakeDragArgs(Files(), UIElement.PreviewDragEnterEvent, fixture.Box);

            fixture.Box.RaiseEvent(args);

            Assert.Equal(DragDropEffects.Copy, args.Effects);
        });
    }

    [Fact]
    public void DraggingSomethingThatIsNotAFile_IsRefused()
    {
        StaTest.Run(() =>
        {
            var fixture = new Fixture();
            var args = MakeDragArgs(new DataObject(DataFormats.Text, "text"), UIElement.PreviewDragOverEvent, fixture.Box);

            fixture.Box.RaiseEvent(args);

            Assert.Equal(DragDropEffects.None, args.Effects);
        });
    }

    [Fact]
    public void DroppingAFile_TypesItsPathIntoTheInputLine()
    {
        StaTest.Run(() =>
        {
            var fixture = new Fixture();
            var args = MakeDragArgs(Files(@"C:\work\notes.txt"), UIElement.PreviewDropEvent, fixture.Box);

            fixture.Box.RaiseEvent(args);

            Assert.Equal(@"C:\work\notes.txt", fixture.Terminal.Input.Text);
            Assert.True(args.Handled);
        });
    }

    [Fact]
    public void DroppingAPathWithSpacesAndJapanese_IsQuoted_AndKeepsTheName()
    {
        StaTest.Run(() =>
        {
            var fixture = new Fixture();
            var args = MakeDragArgs(Files(@"C:\作業 フォルダ\日本語の 資料.txt"), UIElement.PreviewDropEvent, fixture.Box);

            fixture.Box.RaiseEvent(args);

            Assert.Equal("\"C:\\作業 フォルダ\\日本語の 資料.txt\"", fixture.Terminal.Input.Text);
        });
    }

    [Fact]
    public void DroppingNonFileData_DoesNothing()
    {
        StaTest.Run(() =>
        {
            var fixture = new Fixture();
            var args = MakeDragArgs(new DataObject(DataFormats.Text, "text"), UIElement.PreviewDropEvent, fixture.Box);

            fixture.Box.RaiseEvent(args);

            Assert.Equal(string.Empty, fixture.Terminal.Input.Text);
        });
    }

    // ===== Ctrl+C（中断）・Ctrl+Shift+C（コピー） =====

    // 運営者の指示：ターミナルの中でアプリを使っているとき、コピーのつもりのCtrl+Cで中断してしまわないよう、
    // Ctrl+Cは、選択の有無に関わらず、常に中断。コピーは、Ctrl+Shift+C。
    [Fact]
    public void CtrlC_AlwaysInterrupts_EvenWhileTextIsSelected_AndDoesNotCopy()
    {
        StaTest.Run(() =>
        {
            var fixture = new Fixture();
            fixture.Select("選択している文字");

            var handled = fixture.Controller.TryHandleCopyOrInterrupt(Key.C, ModifierKeys.Control);

            Assert.True(handled);
            Assert.Equal(1, fixture.Service.InterruptCount);
            Assert.Empty(fixture.Copied);
        });
    }

    [Fact]
    public void CtrlC_WithoutSelection_Interrupts_AndClearsTheTypedInput()
    {
        StaTest.Run(() =>
        {
            var fixture = new Fixture();
            fixture.Terminal.InsertInput("typing");

            fixture.Controller.TryHandleCopyOrInterrupt(Key.C, ModifierKeys.Control);

            Assert.Equal(1, fixture.Service.InterruptCount);
            Assert.Equal(string.Empty, fixture.Terminal.Input.Text);
        });
    }

    [Fact]
    public void CtrlShiftC_CopiesTheSelection_AndDoesNotInterrupt()
    {
        StaTest.Run(() =>
        {
            var fixture = new Fixture();
            fixture.Select("コピーする文字");

            var handled = fixture.Controller.TryHandleCopyOrInterrupt(Key.C, ModifierKeys.Control | ModifierKeys.Shift);

            Assert.True(handled);
            Assert.Equal(new[] { "コピーする文字" }, fixture.Copied);
            Assert.Equal(0, fixture.Service.InterruptCount);
        });
    }

    [Fact]
    public void CtrlShiftC_WithoutSelection_DoesNothing_ButIsStillHandled()
    {
        StaTest.Run(() =>
        {
            var fixture = new Fixture();
            fixture.Service.RaiseOutput("何も選択していない");
            Assert.True(fixture.Box.Selection.IsEmpty);

            var handled = fixture.Controller.TryHandleCopyOrInterrupt(Key.C, ModifierKeys.Control | ModifierKeys.Shift);

            Assert.True(handled);
            Assert.Empty(fixture.Copied);
            Assert.Equal(0, fixture.Service.InterruptCount);
        });
    }

    [Fact]
    public void CtrlShiftC_WhenTheClipboardIsBusy_DoesNotCrash()
    {
        StaTest.Run(() =>
        {
            var fixture = new Fixture
            {
                Controller = { SetClipboardText = _ => throw new System.Runtime.InteropServices.COMException("busy") }
            };
            fixture.Select("text");

            var handled = fixture.Controller.TryHandleCopyOrInterrupt(Key.C, ModifierKeys.Control | ModifierKeys.Shift);

            Assert.True(handled);
        });
    }

    [Theory]
    [InlineData(Key.C, ModifierKeys.None)]
    [InlineData(Key.C, ModifierKeys.Shift)]
    [InlineData(Key.C, ModifierKeys.Alt)]
    [InlineData(Key.V, ModifierKeys.Control)]
    [InlineData(Key.A, ModifierKeys.Control)]
    [InlineData(Key.X, ModifierKeys.Control | ModifierKeys.Shift)]
    public void OtherKeys_AreLeftAlone(Key key, ModifierKeys modifiers)
    {
        StaTest.Run(() =>
        {
            var fixture = new Fixture();

            var handled = fixture.Controller.TryHandleCopyOrInterrupt(key, modifiers);

            Assert.False(handled);
            Assert.Equal(0, fixture.Service.InterruptCount);
        });
    }

    [Fact]
    public void WithoutABoundTerminal_NothingHappens()
    {
        StaTest.Run(() =>
        {
            var controller = new TerminalSurfaceController(new RichTextBox());

            Assert.False(controller.TryHandleCopyOrInterrupt(Key.C, ModifierKeys.Control));
        });
    }
}
