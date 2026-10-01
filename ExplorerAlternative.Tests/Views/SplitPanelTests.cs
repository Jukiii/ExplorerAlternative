using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using ExplorerAlternative.Models;
using ExplorerAlternative.Views;

namespace ExplorerAlternative.Tests.Views;

// 仕様書19章：分割ペインの配置パネル。WPFの要素はSTAスレッドで扱う。
public sealed class SplitPanelTests
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

    private static (SplitPanel Panel, Border First, Border Second) CreatePanel(Orientation orientation, double ratio, Size size)
    {
        var first = new Border();
        var second = new Border();
        var panel = new SplitPanel { Orientation = orientation, Ratio = ratio };
        panel.Children.Add(first);
        panel.Children.Add(second);

        panel.Measure(size);
        panel.Arrange(new Rect(size));
        return (panel, first, second);
    }

    private static Rect Slot(UIElement element) => LayoutInformation.GetLayoutSlot((FrameworkElement)element);

    [Fact]
    public void Horizontal_PlacesPanesSideBySide_AccordingToTheRatio()
    {
        RunOnSta(() =>
        {
            var (_, first, second) = CreatePanel(Orientation.Horizontal, 0.7, new Size(1006, 400));

            var a = Slot(first);
            var b = Slot(second);
            Assert.Equal(0, a.X, precision: 6);
            Assert.Equal(700, a.Width, precision: 6);
            Assert.Equal(400, a.Height, precision: 6);
            Assert.Equal(706, b.X, precision: 6);
            Assert.Equal(300, b.Width, precision: 6);
        });
    }

    [Fact]
    public void Vertical_PlacesPanesTopAndBottom_AccordingToTheRatio()
    {
        RunOnSta(() =>
        {
            var (_, first, second) = CreatePanel(Orientation.Vertical, 0.25, new Size(800, 806));

            var a = Slot(first);
            var b = Slot(second);
            Assert.Equal(0, a.Y, precision: 6);
            Assert.Equal(200, a.Height, precision: 6);
            Assert.Equal(800, a.Width, precision: 6);
            Assert.Equal(206, b.Y, precision: 6);
            Assert.Equal(600, b.Height, precision: 6);
        });
    }

    [Fact]
    public void SinglePane_FillsTheWholePanel()
    {
        RunOnSta(() =>
        {
            var only = new Border();
            var panel = new SplitPanel { Ratio = 0.3 };
            panel.Children.Add(only);

            panel.Measure(new Size(500, 300));
            panel.Arrange(new Rect(0, 0, 500, 300));

            var slot = Slot(only);
            Assert.Equal(500, slot.Width, precision: 6);
            Assert.Equal(300, slot.Height, precision: 6);
        });
    }

    [Fact]
    public void ChangingTheRatio_RearrangesThePanes()
    {
        RunOnSta(() =>
        {
            var (panel, first, _) = CreatePanel(Orientation.Horizontal, 0.5, new Size(1006, 400));

            panel.Ratio = 0.2;
            panel.Measure(new Size(1006, 400));
            panel.Arrange(new Rect(0, 0, 1006, 400));

            Assert.Equal(200, Slot(first).Width, precision: 6);
        });
    }

    // 範囲外の比率を設定しても、どちらのペインも消えない。
    [Fact]
    public void Ratio_IsCoercedIntoTheAllowedRange()
    {
        RunOnSta(() =>
        {
            var panel = new SplitPanel();

            panel.Ratio = 0.0;
            Assert.Equal(SplitLayout.MinRatio, panel.Ratio);

            panel.Ratio = 3;
            Assert.Equal(SplitLayout.MaxRatio, panel.Ratio);

            panel.Ratio = double.NaN;
            Assert.Equal(SplitLayout.DefaultRatio, panel.Ratio);
        });
    }

    [Fact]
    public void Panel_ReportsItsFullSizeToTheParent()
    {
        RunOnSta(() =>
        {
            var (panel, _, _) = CreatePanel(Orientation.Horizontal, 0.5, new Size(900, 500));

            Assert.Equal(900, panel.DesiredSize.Width, precision: 6);
            Assert.Equal(500, panel.DesiredSize.Height, precision: 6);
        });
    }

    [Fact]
    public void EmptyPanel_DoesNotThrow()
    {
        RunOnSta(() =>
        {
            var panel = new SplitPanel();

            panel.Measure(new Size(100, 100));
            panel.Arrange(new Rect(0, 0, 100, 100));
        });
    }
}
