using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using ExplorerAlternative.Views;

namespace ExplorerAlternative.Tests.Views;

// ターミナル画面（RichTextBox）の文字部分をクリックするとアプリが落ちていた不具合の回帰テスト。
// クリックされた要素（OriginalSource）は、文字部分ではVisualではなくRunやParagraphになる。
public sealed class VisualTreeUtilityTests
{
    // WPFの要素はSTAスレッドで扱う必要がある。
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

    private static (RichTextBox Box, Run Run) CreateTerminalLikeBox()
    {
        var run = new Run("PS C:\\> echo hi");
        var paragraph = new Paragraph(run);
        var box = new RichTextBox { Document = new FlowDocument(paragraph), IsReadOnly = true };
        return (box, run);
    }

    // 不具合の再現：Runに対してVisualTreeHelper.GetParentを直接呼ぶと例外になる。
    [Fact]
    public void VisualTreeHelper_GetParent_ThrowsForRun_WhichWasTheCrashCause()
    {
        RunOnSta(() =>
        {
            var (_, run) = CreateTerminalLikeBox();

            Assert.Throws<InvalidOperationException>(() => VisualTreeHelper.GetParent(run));
        });
    }

    [Fact]
    public void GetParent_DoesNotThrow_ForRun()
    {
        RunOnSta(() =>
        {
            var (_, run) = CreateTerminalLikeBox();

            var parent = VisualTreeUtility.GetParent(run);

            Assert.IsType<Paragraph>(parent);
        });
    }

    // クリックされたRunから親をたどって、ターミナル画面（RichTextBox）まで到達できること。
    // （画面自身のクリックか、それ以外かの判定に使うため、到達できないと誤判定する。）
    [Fact]
    public void GetParent_WalksFromRunUpToTheRichTextBox()
    {
        RunOnSta(() =>
        {
            var (box, run) = CreateTerminalLikeBox();

            DependencyObject? current = run;
            var reached = false;

            for (var i = 0; i < 20 && current is not null; i++)
            {
                if (ReferenceEquals(current, box))
                {
                    reached = true;
                    break;
                }

                current = VisualTreeUtility.GetParent(current);
            }

            Assert.True(reached, "RunからRichTextBoxまで親をたどれませんでした。");
        });
    }

    [Fact]
    public void GetParent_WorksForOrdinaryVisuals()
    {
        RunOnSta(() =>
        {
            var button = new Button();
            var grid = new Grid();
            grid.Children.Add(button);

            Assert.Same(grid, VisualTreeUtility.GetParent(button));
        });
    }

    [Fact]
    public void GetParent_ReturnsNull_AtTheRoot()
    {
        RunOnSta(() =>
        {
            Assert.Null(VisualTreeUtility.GetParent(new Grid()));
        });
    }

    [Fact]
    public void GetParent_ReturnsNull_ForDetachedContentElement()
    {
        RunOnSta(() =>
        {
            Assert.Null(VisualTreeUtility.GetParent(new Run("detached")));
        });
    }
}
