using System.Text.RegularExpressions;

namespace ExplorerAlternative.Tests.Views;

// 画面（XAML）の、運営者の指示に基づく書き方が、元に戻ってしまわないことを、XAMLの文面で確かめる
// （ウィンドウを開かずに確認できる範囲）。ソースの無い環境（成果物だけの実行）では、確かめずに終わる。
public sealed class XamlLayoutGuardTests
{
    private static string? Read(params string[] relative)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var candidate = Path.Combine(new[] { directory.FullName, "ExplorerAlternative" }.Concat(relative).ToArray());
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            directory = directory.Parent;
        }

        return null;
    }

    // 2026-10-03：左右のペインの折りたたみボタンは、「折りたたむ」の文字をやめ、矢印だけにする。
    [Fact]
    public void CollapseButtons_ShowOnlyAnArrow_WithTheNameInTheTooltip()
    {
        var xaml = Read("MainWindow.xaml");
        if (xaml is null) return;

        Assert.DoesNotContain("Content=\"« 折りたたむ\"", xaml);
        Assert.DoesNotContain("Content=\"折りたたむ »\"", xaml);
        Assert.Matches(new Regex("Content=\"«\"[^>]*ToolTip=\"折りたたむ\"", RegexOptions.Singleline), xaml);
        Assert.Matches(new Regex("Content=\"»\"[^>]*ToolTip=\"折りたたむ\"", RegexOptions.Singleline), xaml);
    }

    // 2026-10-03：左ペインの「最近使った場所」「よく使う場所」は、表示しない。
    [Fact]
    public void LeftPane_DoesNotListRecentOrFrequentPlaces()
    {
        var xaml = Read("MainWindow.xaml");
        if (xaml is null) return;

        Assert.DoesNotContain("NavigationPane.RecentPlaces", xaml);
        Assert.DoesNotContain("NavigationPane.FrequentPlaces", xaml);
        Assert.DoesNotContain("Text=\"最近使った場所\"", xaml);
        Assert.DoesNotContain("Text=\"よく使う場所\"", xaml);
        // 最近のプロジェクトは、残す。
        Assert.Contains("NavigationPane.RecentProjects", xaml);
    }

    // 2026-10-03：タッチパネルでなぞってスクロールできるように、ScrollViewerの既定のスタイルで、PanningModeを有効にする。
    [Fact]
    public void ScrollViewers_AllowTouchPanning_ByDefault()
    {
        var xaml = Read("App.xaml");
        if (xaml is null) return;

        var style = Regex.Match(xaml, "<Style TargetType=\"ScrollViewer\">.*?</Style>", RegexOptions.Singleline);

        Assert.True(style.Success, "ScrollViewerの既定のスタイルがありません。");
        Assert.Contains("Property=\"PanningMode\" Value=\"Both\"", style.Value);
    }

    // 個別にStyleを指定したScrollViewerは、既定のスタイルを使わない（置き換わる）ため、PanningModeを直接指定している。
    [Fact]
    public void PreviewWindowScrollViewers_WithTheirOwnStyle_SetPanningModeDirectly()
    {
        var xaml = Read("Views", "PreviewWindow.xaml");
        if (xaml is null) return;

        foreach (var name in new[] { "ImageScrollViewer", "PdfScrollViewer" })
        {
            var tag = Regex.Match(xaml, $"<ScrollViewer x:Name=\"{name}\"[^>]*>", RegexOptions.Singleline);
            Assert.True(tag.Success, $"{name}が見つかりません。");
            Assert.Contains("PanningMode=\"Both\"", tag.Value);
        }
    }

    // 2026-10-03：Logウィンドウの変更内容は、+/-の背景色を付けて表示する（色付きのRichTextBox）。
    [Fact]
    public void GitLogWindow_ShowsTheDiffInAColorableViewer()
    {
        var xaml = Read("Views", "GitLogWindow.xaml");
        if (xaml is null) return;

        Assert.Contains("<RichTextBox x:Name=\"DiffViewer\"", xaml);
    }
}
