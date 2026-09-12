using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace ExplorerAlternative.Rendering;

/// <summary>
/// Markdownプレビュー（仕様書11.2章）用の簡易レンダラー。
/// 外部NuGet依存を追加せず、見出し・強調・リスト・コードブロック・リンクなど
/// よく使われる記法をFlowDocumentへ変換する。完全なCommonMark互換ではない。
/// </summary>
public static class MarkdownRenderer
{
    private static readonly Regex InlineTokenPattern = new(
        @"(?<bold>\*\*(?<boldtext>.+?)\*\*)|(?<code>`(?<codetext>.+?)`)|(?<link>\[(?<linktext>.+?)\]\((?<linkurl>.+?)\))|(?<italic>\*(?<italictext>.+?)\*)",
        RegexOptions.Compiled);

    public static FlowDocument Render(string markdown)
    {
        var document = new FlowDocument { PagePadding = new Thickness(4) };
        var lines = markdown.Replace("\r\n", "\n").Split('\n');
        var inCodeBlock = false;
        List<string>? codeBlockLines = null;

        foreach (var line in lines)
        {
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                if (!inCodeBlock)
                {
                    inCodeBlock = true;
                    codeBlockLines = new List<string>();
                }
                else
                {
                    document.Blocks.Add(CreateCodeBlock(codeBlockLines!));
                    inCodeBlock = false;
                    codeBlockLines = null;
                }

                continue;
            }

            if (inCodeBlock)
            {
                codeBlockLines!.Add(line);
                continue;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var headingLevel = CountLeading(line, '#');
            if (headingLevel is >= 1 and <= 6 && line.Length > headingLevel && line[headingLevel] == ' ')
            {
                document.Blocks.Add(CreateHeading(line[(headingLevel + 1)..].Trim(), headingLevel));
                continue;
            }

            var trimmed = line.TrimStart();
            if (trimmed.StartsWith("- ", StringComparison.Ordinal) || trimmed.StartsWith("* ", StringComparison.Ordinal))
            {
                document.Blocks.Add(CreateBulletParagraph(trimmed[2..]));
                continue;
            }

            document.Blocks.Add(CreateParagraph(line));
        }

        if (inCodeBlock && codeBlockLines is not null)
        {
            document.Blocks.Add(CreateCodeBlock(codeBlockLines));
        }

        if (document.Blocks.Count == 0)
        {
            document.Blocks.Add(new Paragraph(new Run(string.Empty)));
        }

        return document;
    }

    private static int CountLeading(string line, char c)
    {
        var count = 0;
        while (count < line.Length && line[count] == c)
        {
            count++;
        }

        return count;
    }

    private static Paragraph CreateHeading(string text, int level)
    {
        var paragraph = new Paragraph
        {
            FontSize = Math.Max(14, 24 - (level * 2)),
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 8, 0, 4)
        };

        foreach (var inline in ParseInlines(text))
        {
            paragraph.Inlines.Add(inline);
        }

        return paragraph;
    }

    private static Paragraph CreateBulletParagraph(string text)
    {
        var paragraph = new Paragraph { Margin = new Thickness(16, 0, 0, 2) };
        paragraph.Inlines.Add(new Run("• "));
        foreach (var inline in ParseInlines(text))
        {
            paragraph.Inlines.Add(inline);
        }

        return paragraph;
    }

    private static Paragraph CreateParagraph(string text)
    {
        var paragraph = new Paragraph { Margin = new Thickness(0, 0, 0, 4) };
        foreach (var inline in ParseInlines(text))
        {
            paragraph.Inlines.Add(inline);
        }

        return paragraph;
    }

    private static IEnumerable<Inline> ParseInlines(string text)
    {
        var result = new List<Inline>();
        var lastIndex = 0;

        foreach (Match match in InlineTokenPattern.Matches(text))
        {
            if (match.Index > lastIndex)
            {
                result.Add(new Run(text[lastIndex..match.Index]));
            }

            if (match.Groups["bold"].Success)
            {
                result.Add(new Run(match.Groups["boldtext"].Value) { FontWeight = FontWeights.Bold });
            }
            else if (match.Groups["code"].Success)
            {
                result.Add(new Run(match.Groups["codetext"].Value) { FontFamily = new FontFamily("Consolas") });
            }
            else if (match.Groups["link"].Success)
            {
                result.Add(CreateHyperlink(match.Groups["linktext"].Value, match.Groups["linkurl"].Value));
            }
            else if (match.Groups["italic"].Success)
            {
                result.Add(new Run(match.Groups["italictext"].Value) { FontStyle = FontStyles.Italic });
            }

            lastIndex = match.Index + match.Length;
        }

        if (lastIndex < text.Length)
        {
            result.Add(new Run(text[lastIndex..]));
        }

        return result;
    }

    private static Hyperlink CreateHyperlink(string label, string url)
    {
        var hyperlink = new Hyperlink(new Run(label));

        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            hyperlink.NavigateUri = uri;
            hyperlink.RequestNavigate += (_, e) =>
            {
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(e.Uri.ToString())
                    {
                        UseShellExecute = true
                    });
                }
                catch (System.ComponentModel.Win32Exception)
                {
                    // リンクを開けなくてもプレビュー表示自体は継続する。
                }
            };
        }

        return hyperlink;
    }

    private static Section CreateCodeBlock(List<string> lines)
    {
        var section = new Section
        {
            Background = Brushes.WhiteSmoke,
            Margin = new Thickness(0, 4, 0, 4),
            Padding = new Thickness(8)
        };

        var paragraph = new Paragraph { FontFamily = new FontFamily("Consolas") };
        paragraph.Inlines.Add(new Run(string.Join(Environment.NewLine, lines)));
        section.Blocks.Add(paragraph);

        return section;
    }
}
