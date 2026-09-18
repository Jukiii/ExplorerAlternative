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

    private static readonly Regex NumberedListPattern = new(@"^(?<num>\d+)\.\s+(?<text>.*)$", RegexOptions.Compiled);

    public static FlowDocument Render(string markdown)
    {
        var document = new FlowDocument { PagePadding = new Thickness(4) };
        var lines = markdown.Replace("\r\n", "\n").Split('\n');
        var inCodeBlock = false;
        List<string>? codeBlockLines = null;

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];

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

            // GFM形式のパイプテーブル：ヘッダー行の次が区切り行（|---|---|等）であるかで判定する。
            if (trimmed.StartsWith("|", StringComparison.Ordinal) &&
                i + 1 < lines.Length && IsTableSeparatorRow(lines[i + 1]))
            {
                var tableLines = new List<string> { line };
                var j = i + 2;
                while (j < lines.Length && lines[j].TrimStart().StartsWith("|", StringComparison.Ordinal))
                {
                    tableLines.Add(lines[j]);
                    j++;
                }

                document.Blocks.Add(CreateTable(tableLines));
                i = j - 1;
                continue;
            }

            if (trimmed.StartsWith(">", StringComparison.Ordinal))
            {
                document.Blocks.Add(CreateBlockquoteParagraph(trimmed.TrimStart('>').TrimStart()));
                continue;
            }

            if (trimmed.StartsWith("- ", StringComparison.Ordinal) || trimmed.StartsWith("* ", StringComparison.Ordinal))
            {
                document.Blocks.Add(CreateBulletParagraph(trimmed[2..]));
                continue;
            }

            var numberedMatch = NumberedListPattern.Match(trimmed);
            if (numberedMatch.Success)
            {
                document.Blocks.Add(CreateNumberedParagraph(numberedMatch.Groups["num"].Value, numberedMatch.Groups["text"].Value));
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

    private static bool IsTableSeparatorRow(string line)
    {
        var trimmed = line.Trim();
        if (!trimmed.StartsWith("|", StringComparison.Ordinal))
        {
            return false;
        }

        var cells = SplitTableRow(trimmed);
        return cells.Count > 0 && cells.All(c => Regex.IsMatch(c.Trim(), @"^:?-+:?$"));
    }

    private static List<string> SplitTableRow(string line)
    {
        var trimmed = line.Trim().Trim('|');
        return trimmed.Split('|').ToList();
    }

    private static Table CreateTable(List<string> tableLines)
    {
        var table = new Table { CellSpacing = 0, Margin = new Thickness(0, 4, 0, 8) };
        var headerCells = SplitTableRow(tableLines[0]);

        foreach (var _ in headerCells)
        {
            table.Columns.Add(new TableColumn());
        }

        var headerGroup = new TableRowGroup();
        table.RowGroups.Add(headerGroup);
        headerGroup.Rows.Add(CreateTableRow(headerCells, isHeader: true));

        var bodyGroup = new TableRowGroup();
        table.RowGroups.Add(bodyGroup);

        // tableLinesは区切り行を含まない（呼び出し元で除外済み）。0番目=ヘッダーなので、
        // 本文は1番目以降。
        for (var i = 1; i < tableLines.Count; i++)
        {
            bodyGroup.Rows.Add(CreateTableRow(SplitTableRow(tableLines[i]), isHeader: false));
        }

        return table;
    }

    private static TableRow CreateTableRow(List<string> cells, bool isHeader)
    {
        var row = new TableRow();

        foreach (var cellText in cells)
        {
            var paragraph = new Paragraph { Margin = new Thickness(4, 2, 4, 2) };
            if (isHeader)
            {
                paragraph.FontWeight = FontWeights.Bold;
            }

            foreach (var inline in ParseInlines(cellText.Trim()))
            {
                paragraph.Inlines.Add(inline);
            }

            var cell = new TableCell(paragraph)
            {
                BorderBrush = Brushes.Gray,
                BorderThickness = new Thickness(0, 0, 0, 1)
            };
            row.Cells.Add(cell);
        }

        return row;
    }

    private static Paragraph CreateNumberedParagraph(string number, string text)
    {
        var paragraph = new Paragraph { Margin = new Thickness(16, 0, 0, 2) };
        paragraph.Inlines.Add(new Run($"{number}. "));
        foreach (var inline in ParseInlines(text))
        {
            paragraph.Inlines.Add(inline);
        }

        return paragraph;
    }

    private static Paragraph CreateBlockquoteParagraph(string text)
    {
        var paragraph = new Paragraph
        {
            Margin = new Thickness(12, 2, 0, 2),
            Padding = new Thickness(8, 2, 0, 2),
            BorderBrush = Brushes.Gray,
            BorderThickness = new Thickness(2, 0, 0, 0),
            FontStyle = FontStyles.Italic
        };

        foreach (var inline in ParseInlines(text))
        {
            paragraph.Inlines.Add(inline);
        }

        return paragraph;
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
