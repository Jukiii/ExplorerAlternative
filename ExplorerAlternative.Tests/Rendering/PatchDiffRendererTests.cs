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

    private static (string Text, Brush? Foreground, bool HasBackground)[] Lines(string patch)
    {
        var result = new List<(string, Brush?, bool)>();
        StaTest.Run(() =>
        {
            var doc = PatchDiffRenderer.Render(patch);
            foreach (var p in doc.Blocks.OfType<Paragraph>())
            {
                var run = (Run)p.Inlines.First();
                result.Add((run.Text, run.Foreground, p.Background is not null));
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

    [Fact]
    public void Render_AddedAndRemovedLines_GetColorsAndBackground_ContextDoesNot()
    {
        var lines = Lines(GitPatch);

        Assert.Same(Brushes.Green, lines[7].Foreground);
        Assert.True(lines[7].HasBackground);
        Assert.Same(Brushes.Firebrick, lines[6].Foreground);
        Assert.True(lines[6].HasBackground);
        Assert.False(lines[5].HasBackground); // " context"
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
