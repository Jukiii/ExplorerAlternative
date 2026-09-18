using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace ExplorerAlternative.Rendering;

/// <summary>
/// 仕様書24章「Patch内容確認・PatchからのDiff表示」：unified diff形式（git diff / svn diff の
/// 出力そのもの）を色分け表示する。Patchファイル自体が既に差分形式であるため、
/// 23章のDiffService（2ファイルの再比較）は使わず、テキストをそのまま解釈して色付けする。
/// </summary>
public static class PatchDiffRenderer
{
    public static FlowDocument Render(string patchText)
    {
        var document = new FlowDocument { PagePadding = new Thickness(4), FontFamily = new FontFamily("Consolas") };
        var lines = patchText.Replace("\r\n", "\n").Split('\n');

        foreach (var line in lines)
        {
            document.Blocks.Add(CreateLine(line));
        }

        if (document.Blocks.Count == 0)
        {
            document.Blocks.Add(new Paragraph(new Run(string.Empty)));
        }

        return document;
    }

    /// <summary>diff対象ファイルの件数（git diff: "diff --git"、svn diff: "Index:"の出現数）。</summary>
    public static int CountFiles(string patchText)
    {
        var lines = patchText.Replace("\r\n", "\n").Split('\n');
        return lines.Count(l => l.StartsWith("diff --git ", StringComparison.Ordinal) || l.StartsWith("Index: ", StringComparison.Ordinal));
    }

    private static Paragraph CreateLine(string line)
    {
        var paragraph = new Paragraph { Margin = new Thickness(0) };
        var run = new Run(line);

        if (line.StartsWith("diff --git ", StringComparison.Ordinal) || line.StartsWith("Index: ", StringComparison.Ordinal))
        {
            run.FontWeight = FontWeights.Bold;
        }
        else if (line.StartsWith("+++", StringComparison.Ordinal) || line.StartsWith("---", StringComparison.Ordinal))
        {
            run.Foreground = Brushes.Gray;
            run.FontWeight = FontWeights.Bold;
        }
        else if (line.StartsWith("@@", StringComparison.Ordinal))
        {
            run.Foreground = Brushes.RoyalBlue;
            run.FontWeight = FontWeights.Bold;
        }
        else if (line.StartsWith("+", StringComparison.Ordinal))
        {
            run.Foreground = Brushes.Green;
            paragraph.Background = new SolidColorBrush(Color.FromArgb(30, 0, 200, 0));
        }
        else if (line.StartsWith("-", StringComparison.Ordinal))
        {
            run.Foreground = Brushes.Firebrick;
            paragraph.Background = new SolidColorBrush(Color.FromArgb(30, 200, 0, 0));
        }

        paragraph.Inlines.Add(run);
        return paragraph;
    }
}
