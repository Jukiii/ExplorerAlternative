using System.Windows.Controls;
using System.Windows.Documents;
using ExplorerAlternative.Tests.TestDoubles;
using ExplorerAlternative.ViewModels;
using ExplorerAlternative.Views;

namespace ExplorerAlternative.Tests.Views;

// 仕様書17章：ターミナル画面（RichTextBox）への描画。WPFの要素はSTAスレッドで扱う。
public sealed class TerminalSurfaceControllerTests
{
    private static void RunOnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure is not null)
        {
            throw new Exception("STAスレッド上のテストが失敗しました。", failure);
        }
    }

    private static string SurfaceText(RichTextBox box) =>
        new TextRange(box.Document.ContentStart, box.Document.ContentEnd).Text.Replace("\r\n", "\n").TrimEnd('\n');

    private static (RichTextBox Box, TerminalSurfaceController Controller, TerminalViewModel Terminal, FakeTerminalService Service) Create()
    {
        var box = new RichTextBox();
        var controller = new TerminalSurfaceController(box);
        var service = new FakeTerminalService();
        var terminal = new TerminalViewModel(service, syncByDefault: true, "PowerShell 1");
        controller.Bind(terminal);
        return (box, controller, terminal, service);
    }

    [Fact]
    public void Output_IsShownOnTheSurface()
    {
        RunOnSta(() =>
        {
            var (box, _, _, service) = Create();

            service.RaiseOutput("PS C:\\> ");

            Assert.Equal("PS C:\\>", SurfaceText(box).TrimEnd());
        });
    }

    [Fact]
    public void ProgressBar_OverwritesTheCurrentLineOnTheSurface()
    {
        RunOnSta(() =>
        {
            var (box, _, _, service) = Create();

            service.RaiseOutput("header\n");
            service.RaiseOutput("progress 10%\r");
            service.RaiseOutput("progress 55%\r");
            service.RaiseOutput("progress 100%\n");
            service.RaiseOutput("done\n");

            Assert.Equal("header\nprogress 100%\ndone", SurfaceText(box));
        });
    }

    [Fact]
    public void ProgressBar_DoesNotEraseEarlierLinesOrTheTypedInput()
    {
        RunOnSta(() =>
        {
            var (box, _, terminal, service) = Create();
            service.RaiseOutput("line1\nline2\nspinner |");
            terminal.InsertInput("typed");

            service.RaiseOutput("\rspinner /");

            var text = SurfaceText(box);
            Assert.StartsWith("line1\nline2\nspinner /", text);
            Assert.EndsWith("typed", text);
            Assert.DoesNotContain("spinner |", text, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void RebindingToTheSameTerminalBuffer_ShowsTheOverwrittenResult()
    {
        RunOnSta(() =>
        {
            var (box, controller, terminal, service) = Create();
            service.RaiseOutput("a\nprogress 1\rprogress 2\n");

            // 別のタブへ切り替えて、戻した状況（スクロールバックから描き直す）。
            controller.Bind(null);
            controller.Bind(terminal);

            Assert.Equal("a\nprogress 2", SurfaceText(box));
        });
    }

    [Fact]
    public void TypedInput_IsShownAfterTheOutput()
    {
        RunOnSta(() =>
        {
            var (box, _, terminal, service) = Create();
            service.RaiseOutput("PS C:\\> ");

            terminal.InsertInput("dir");

            Assert.Equal("PS C:\\> dir", SurfaceText(box));
        });
    }
}
