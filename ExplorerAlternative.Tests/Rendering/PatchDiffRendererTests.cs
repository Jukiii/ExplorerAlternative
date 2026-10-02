using System.Windows.Documents;
using System.Windows.Media;
using ExplorerAlternative.Rendering;
using ExplorerAlternative.Tests.TestDoubles;

namespace ExplorerAlternative.Tests.Rendering;

// 仕様書24章「Patch内容確認」：unified diffの色分けと、対象ファイル数の数え方。
public sealed class PatchDiffRendererTests
{
    private const string GitPatch =
        "diff --git a/a.txt b/a.txt\nindex 111..222 100644\n--- a/a.txt\n+++ b/a.txt\n@@ -1,2 +1,2 @@\n context\n-old\n+new\n";

    [Fact]
    public void CountFiles_Git_CountsDiffHeaders()
    {
        var two = GitPatch + "diff --git a/b.txt b/b.txt\n--- a/b.txt\n+++ b/b.txt\n";

        Assert.Equal(1, PatchDiffRenderer.CountFiles(GitPatch));
        Assert.Equal(2, PatchDiffRenderer.CountFiles(two));
    }

    [Fact]
    public void CountFiles_Svn_CountsIndexLines()
    {
        var svn = "Index: a.txt\n=====\n--- a.txt\n+++ a.txt\nIndex: b.txt\n=====\n";

        Assert.Equal(2, PatchDiffRenderer.CountFiles(svn));
    }

    [Fact]
    public void CountFiles_PlainTextAndEmpty_AreZero()
    {
        Assert.Equal(0, PatchDiffRenderer.CountFiles(string.Empty));
        Assert.Equal(0, PatchDiffRenderer.CountFiles("just text\n"));
    }

    [Fact]
    public void CountFiles_CRLF_Works()
    {
        Assert.Equal(1, PatchDiffRenderer.CountFiles(GitPatch.Replace("\n", "\r\n")));
    }

    private static (string Text, Brush? Foreground, bool HasBackground, Brush? Background)[] Lines(string patch)
    {
        var result = new List<(string, Brush?, bool, Brush?)>();
        StaTest.Run(() =>
        {
            var doc = PatchDiffRenderer.Render(patch);
            foreach (var p in doc.Blocks.OfType<Paragraph>())
            {
                var run = (Run)p.Inlines.First();
                result.Add((run.Text, run.Foreground, p.Background is not null, p.Background));
            }
        });
        return result.ToArray();
    }

    [Fact]
    public void Render_OneParagraphPerLine()
    {
        var lines = Lines(GitPatch);

        Assert.Equal(GitPatch.Split('\n').Length, lines.Length);
        Assert.Equal("-old", lines[6].Text);
    }

    // 運営者の指示（2026-10-03）：追加行（+）は背景を黄緑、削除行（-）は背景を赤にする。
    [Fact]
    public void Render_AddedLinesGetAYellowGreenBackground_RemovedLinesARedOne_ContextNone()
    {
        var lines = Lines(GitPatch);

        Assert.Same(PatchDiffRenderer.AddedBackground, lines[7].Background);
        Assert.Same(PatchDiffRenderer.RemovedBackground, lines[6].Background);
        Assert.False(lines[5].HasBackground); // " context"
    }

    [Fact]
    public void Backgrounds_AreYellowGreenAndRed()
    {
        var added = ((SolidColorBrush)PatchDiffRenderer.AddedBackground).Color;
        var removed = ((SolidColorBrush)PatchDiffRenderer.RemovedBackground).Color;

        // 黄緑：緑が強く、赤は中程度、青が小さい。赤：赤が強く、緑・青が小さい。
        Assert.True(added.G > added.R && added.R > added.B);
        Assert.True(removed.R > removed.G && removed.R > removed.B);
        Assert.True(PatchDiffRenderer.AddedBackground.IsFrozen && PatchDiffRenderer.RemovedBackground.IsFrozen);
    }

    // 背景で示すので、文字の色は変えない（テーマの文字色のまま。どちらの背景でも読める）。
    [Fact]
    public void Render_AddedAndRemovedLines_KeepTheNormalTextColor()
    {
        StaTest.Run(() =>
        {
            var document = PatchDiffRenderer.Render(GitPatch);
            var blocks = document.Blocks.OfType<Paragraph>().ToList();

            // 文字色を、行に直接指定していない（周りの色を引き継ぐ）。
            foreach (var index in new[] { 6, 7 })
            {
                var run = (Run)blocks[index].Inlines.First();
                Assert.Equal(System.Windows.DependencyProperty.UnsetValue, run.ReadLocalValue(TextElement.ForegroundProperty));
            }
        });
    }

    [Fact]
    public void Render_FileHeadersAndHunks_AreNotColoredAsAddedOrRemoved()
    {
        var lines = Lines(GitPatch);

        // "--- a/a.txt" は、削除行（赤）ではなく、ファイルの見出し（灰）。
        Assert.Same(Brushes.Gray, lines[2].Foreground);
        Assert.Same(Brushes.Gray, lines[3].Foreground);
        Assert.False(lines[2].HasBackground);
        Assert.Same(Brushes.RoyalBlue, lines[4].Foreground);
    }

    [Fact]
    public void Render_EmptyPatch_StillHasABlock()
    {
        StaTest.Run(() => Assert.NotEmpty(PatchDiffRenderer.Render(string.Empty).Blocks));
    }
}
