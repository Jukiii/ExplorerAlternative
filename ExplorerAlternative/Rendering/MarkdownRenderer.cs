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
    // 脚注の参照（[^1]）と、改行（<br>）を、強調・コード・リンクより先に判定する。
    private static readonly Regex InlineTokenPattern = new(
        @"(?<fnref>\[\^(?<fnid>[^\]\s]+)\])|(?<br><br\s*/?>)|(?<bold>\*\*(?<boldtext>.+?)\*\*)|(?<code>`(?<codetext>.+?)`)|(?<link>\[(?<linktext>.+?)\]\((?<linkurl>.+?)\))|(?<italic>\*(?<italictext>.+?)\*)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // 脚注の定義行：[^id]: 本文
    private static readonly Regex FootnoteDefinitionPattern = new(@"^\[\^(?<id>[^\]\s]+)\]:\s*(?<text>.*)$", RegexOptions.Compiled);

    // 箇条書きの記号：- * +
    private static readonly Regex BulletListPattern = new(@"^[-*+]\s+(?<text>.*)$", RegexOptions.Compiled);

    // 脚注の番号付け・定義の保持（Render中だけ有効。Renderは1回の呼び出しの間、同じスレッドで完結する）。
    [ThreadStatic]
    private static FootnoteContext? _footnotes;

    private sealed class FootnoteContext
    {
        public FootnoteContext(Dictionary<string, string> definitions) => Definitions = definitions;

        public Dictionary<string, string> Definitions { get; }

        /// <summary>参照された順（番号は、この順の1始まり）。</summary>
        public List<string> ReferencedOrder { get; } = new();

        public int NumberOf(string id)
        {
            var index = ReferencedOrder.IndexOf(id);
            if (index < 0)
            {
                ReferencedOrder.Add(id);
                index = ReferencedOrder.Count - 1;
            }

            return index + 1;
        }
    }

    private static readonly Regex NumberedListPattern = new(@"^(?<num>\d+)\.\s+(?<text>.*)$", RegexOptions.Compiled);

    public static FlowDocument Render(string markdown)
    {
        var lines = ExtractFootnoteDefinitions(markdown.Replace("\r\n", "\n").Split('\n'), out var definitions);

        _footnotes = new FootnoteContext(definitions);
        try
        {
            return RenderCore(lines);
        }
        finally
        {
            _footnotes = null;
        }
    }

    // 脚注の定義行（[^id]: 本文）を、本文から取り除いて集める。コードブロックの中は対象外。
    // 定義の次の行が、2つ以上の空白で字下げされていれば、その定義の続きとして連結する。
    private static string[] ExtractFootnoteDefinitions(string[] lines, out Dictionary<string, string> definitions)
    {
        definitions = new Dictionary<string, string>(StringComparer.Ordinal);
        var body = new List<string>();
        var inCode = false;
        string? currentId = null;

        foreach (var line in lines)
        {
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                inCode = !inCode;
                currentId = null;
                body.Add(line);
                continue;
            }

            if (inCode)
            {
                body.Add(line);
                continue;
            }

            var match = FootnoteDefinitionPattern.Match(line);
            if (match.Success)
            {
                currentId = match.Groups["id"].Value;
                definitions[currentId] = match.Groups["text"].Value.Trim();
                continue;
            }

            if (currentId is not null && line.Length > 0 && (line.StartsWith("  ", StringComparison.Ordinal) || line[0] == '\t'))
            {
                definitions[currentId] = (definitions[currentId] + " " + line.Trim()).Trim();
                continue;
            }

            currentId = null;
            body.Add(line);
        }

        return body.ToArray();
    }

    private static FlowDocument RenderCore(string[] lines)
    {
        var document = new FlowDocument { PagePadding = new Thickness(4) };
        var inCodeBlock = false;
        List<string>? codeBlockLines = null;

        // 入れ子のリスト用：現在のリストの、各階層の字下げ幅（浅い順）。リスト以外の行が来たら空にする。
        var listIndents = new List<int>();

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];

            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                if (!inCodeBlock)
                {
                    inCodeBlock = true;
                    codeBlockLines = new List<string>();
                    listIndents.Clear();
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
                listIndents.Clear();
                document.Blocks.Add(CreateHeading(line[(headingLevel + 1)..].Trim(), headingLevel));
                continue;
            }

            var trimmed = line.TrimStart();

            // GFM形式のパイプテーブル：ヘッダー行の次が区切り行（|---|---|等）であるかで判定する。
            if (trimmed.StartsWith("|", StringComparison.Ordinal) &&
                i + 1 < lines.Length && IsTableSeparatorRow(lines[i + 1]))
            {
                listIndents.Clear();
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
                listIndents.Clear();
                document.Blocks.Add(CreateBlockquoteParagraph(trimmed.TrimStart('>').TrimStart()));
                continue;
            }

            var bulletMatch = BulletListPattern.Match(trimmed);
            if (bulletMatch.Success)
            {
                var level = ResolveListLevel(listIndents, IndentWidth(line));
                document.Blocks.Add(CreateBulletParagraph(bulletMatch.Groups["text"].Value, level));
                continue;
            }

            var numberedMatch = NumberedListPattern.Match(trimmed);
            if (numberedMatch.Success)
            {
                var level = ResolveListLevel(listIndents, IndentWidth(line));
                document.Blocks.Add(CreateNumberedParagraph(numberedMatch.Groups["num"].Value, numberedMatch.Groups["text"].Value, level));
                continue;
            }

            listIndents.Clear();
            document.Blocks.Add(CreateParagraph(line));
        }

        if (inCodeBlock && codeBlockLines is not null)
        {
            document.Blocks.Add(CreateCodeBlock(codeBlockLines));
        }

        AppendFootnotes(document);

        if (document.Blocks.Count == 0)
        {
            document.Blocks.Add(new Paragraph(new Run(string.Empty)));
        }

        return document;
    }

    // 行頭の空白の幅（タブは4つ分）。
    private static int IndentWidth(string line)
    {
        var width = 0;
        foreach (var c in line)
        {
            if (c == ' ')
            {
                width++;
            }
            else if (c == '\t')
            {
                width += 4;
            }
            else
            {
                break;
            }
        }

        return width;
    }

    // 入れ子のリストの階層（0始まり）を、字下げの幅から決める。直前までより深ければ1段深くし、
    // 浅くなったら、その幅に対応する階層まで戻る。字下げが2つでも4つでも、同じように入れ子になる。
    private static int ResolveListLevel(List<int> indents, int indent)
    {
        while (indents.Count > 0 && indents[^1] > indent)
        {
            indents.RemoveAt(indents.Count - 1);
        }

        if (indents.Count == 0 || indents[^1] < indent)
        {
            indents.Add(indent);
        }

        return indents.Count - 1;
    }

    // 参照された脚注を、参照された順に、本文の最後へ並べる。定義の中の参照で、新しい脚注が参照されることもある。
    private static void AppendFootnotes(FlowDocument document)
    {
        var context = _footnotes;
        if (context is null || context.ReferencedOrder.Count == 0)
        {
            return;
        }

        document.Blocks.Add(new Paragraph
        {
            Margin = new Thickness(0, 12, 0, 4),
            BorderBrush = Brushes.Gray,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(0, 4, 0, 0),
            FontWeight = FontWeights.Bold,
            Inlines = { new Run("脚注") }
        });

        for (var i = 0; i < context.ReferencedOrder.Count; i++)
        {
            var paragraph = new Paragraph { Margin = new Thickness(16, 0, 0, 2), FontSize = 13 };
            paragraph.Inlines.Add(new Run($"{i + 1}. "));

            foreach (var inline in ParseInlines(context.Definitions[context.ReferencedOrder[i]]))
            {
                paragraph.Inlines.Add(inline);
            }

            document.Blocks.Add(paragraph);
        }
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

    // 行をセルに分ける。先頭と末尾の区切り（|）は1つずつ取り除き、\| は区切りではなく、文字の | として扱う。
    private static List<string> SplitTableRow(string line)
    {
        var text = line.Trim();

        if (text.StartsWith('|'))
        {
            text = text[1..];
        }

        if (text.EndsWith('|') && !text.EndsWith("\\|", StringComparison.Ordinal))
        {
            text = text[..^1];
        }

        var cells = new List<string>();
        var current = new System.Text.StringBuilder();

        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\\' && i + 1 < text.Length && text[i + 1] == '|')
            {
                current.Append('|');
                i++;
            }
            else if (text[i] == '|')
            {
                cells.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(text[i]);
            }
        }

        cells.Add(current.ToString());
        return cells;
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

    private static Paragraph CreateNumberedParagraph(string number, string text, int level = 0)
    {
        var paragraph = new Paragraph { Margin = new Thickness(16 + (level * 18), 0, 0, 2) };
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

    // 階層ごとに、記号を変える（•  ◦  ▪ の順に繰り返す）。
    private static readonly string[] BulletGlyphs = { "• ", "◦ ", "▪ " };

    private static Paragraph CreateBulletParagraph(string text, int level = 0)
    {
        var paragraph = new Paragraph { Margin = new Thickness(16 + (level * 18), 0, 0, 2) };
        paragraph.Inlines.Add(new Run(BulletGlyphs[level % BulletGlyphs.Length]));
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

            if (match.Groups["fnref"].Success)
            {
                // 定義がある脚注だけを、上付きの番号にする（定義が無いものは、書かれたままの文字にする）。
                var id = match.Groups["fnid"].Value;
                if (_footnotes is { } footnotes && footnotes.Definitions.ContainsKey(id))
                {
                    result.Add(new Run(footnotes.NumberOf(id).ToString())
                    {
                        BaselineAlignment = BaselineAlignment.Superscript,
                        FontSize = 10
                    });
                }
                else
                {
                    result.Add(new Run(match.Value));
                }
            }
            else if (match.Groups["br"].Success)
            {
                result.Add(new LineBreak());
            }
            else if (match.Groups["bold"].Success)
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
