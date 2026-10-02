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
    /// <summary>追加された行（+）の背景（黄緑）。文字色は変えず、背景だけで示す（どのテーマでも読めるように、少し透かす）。</summary>
    public static readonly Brush AddedBackground = CreateBrush(150, 154, 205, 50);

    /// <summary>削除された行（-）の背景（赤）。</summary>
    public static readonly Brush RemovedBackground = CreateBrush(150, 220, 50, 47);

    private static Brush CreateBrush(byte alpha, byte red, byte green, byte blue)
    {
        var brush = new SolidColorBrush(Color.FromArgb(alpha, red, green, blue));
        brush.Freeze();
        return brush;
    }

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
            paragraph.Background = AddedBackground;
        }
        else if (line.StartsWith("-", StringComparison.Ordinal))
        {
            paragraph.Background = RemovedBackground;
        }

        paragraph.Inlines.Add(run);
        return paragraph;
    }
}
