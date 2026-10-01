using System.Windows;
using System.Windows.Documents;
using ExplorerAlternative.Rendering;
using ExplorerAlternative.Tests.TestDoubles;

namespace ExplorerAlternative.Tests.Rendering;

// 仕様書13章：Markdownのプレビュー（簡易レンダラー）。WPFのFlowDocumentはSTAスレッドで扱う。
public sealed class MarkdownRendererTests
{
    private static string TextOf(TextElement element) =>
        new TextRange(element.ContentStart, element.ContentEnd).Text.TrimEnd('\r', '\n');

    private static List<Block> Render(string markdown) => MarkdownRenderer.Render(markdown).Blocks.ToList();

    // コードブロックは、最上位がSection（背景つき）で、その中に段落が入っている。Sectionの中も展開して、
    // 文書の順に、すべての段落を返す（テーブルは対象外）。
    private static IEnumerable<Block> Flatten(Block block) =>
        block is Section section ? section.Blocks.SelectMany(Flatten) : new[] { block };

    private static List<Paragraph> Paragraphs(string markdown) =>
        Render(markdown).SelectMany(Flatten).OfType<Paragraph>().ToList();

    // ===== 基本の記法 =====

    [Fact]
    public void Heading_UsesLargerBoldText_BySize()
    {
        StaTest.Run(() =>
        {
            var blocks = Paragraphs("# 大見出し\n## 中見出し\n###### 小見出し");

            Assert.Equal(new[] { "大見出し", "中見出し", "小見出し" }, blocks.Select(TextOf).ToArray());
            Assert.All(blocks, p => Assert.Equal(FontWeights.Bold, p.FontWeight));
            Assert.True(blocks[0].FontSize > blocks[1].FontSize);
            Assert.True(blocks[1].FontSize > blocks[2].FontSize);
        });
    }

    [Fact]
    public void HashWithoutSpace_IsNotAHeading()
    {
        StaTest.Run(() =>
        {
            var paragraph = Assert.Single(Paragraphs("#hashtag"));

            Assert.Equal("#hashtag", TextOf(paragraph));
            Assert.NotEqual(FontWeights.Bold, paragraph.FontWeight);
        });
    }

    [Fact]
    public void BoldItalicCodeAndLink_AreConverted()
    {
        StaTest.Run(() =>
        {
            var paragraph = Assert.Single(Paragraphs("a **bold** b *italic* c `code` d [link](https://example.com) e"));

            Assert.Equal("a bold b italic c code d link e", TextOf(paragraph));
            var runs = paragraph.Inlines.OfType<Run>().ToList();
            Assert.Contains(runs, r => r.Text == "bold" && r.FontWeight == FontWeights.Bold);
            Assert.Contains(runs, r => r.Text == "italic" && r.FontStyle == FontStyles.Italic);
            Assert.Contains(runs, r => r.Text == "code" && r.FontFamily.Source == "Consolas");
            var link = Assert.Single(paragraph.Inlines.OfType<Hyperlink>());
            Assert.Equal("link", TextOf(link));
            Assert.Equal("https://example.com/", link.NavigateUri?.ToString());
        });
    }

    [Fact]
    public void Paragraph_PlainText()
    {
        StaTest.Run(() =>
        {
            Assert.Equal("ただの文章です。", TextOf(Assert.Single(Paragraphs("ただの文章です。"))));
        });
    }

    [Fact]
    public void BlankLines_AreSkipped()
    {
        StaTest.Run(() =>
        {
            Assert.Equal(2, Paragraphs("one\n\n\n\ntwo").Count);
        });
    }

    [Fact]
    public void EmptyInput_GivesASingleEmptyParagraph()
    {
        StaTest.Run(() =>
        {
            var blocks = Render(string.Empty);

            Assert.Single(blocks);
            Assert.Equal(string.Empty, TextOf((Paragraph)blocks[0]));
        });
    }

    [Fact]
    public void CrLfLineEndings_AreHandled()
    {
        StaTest.Run(() =>
        {
            Assert.Equal(new[] { "A", "B" }, Paragraphs("A\r\nB").Select(TextOf).ToArray());
        });
    }

    // ===== コードブロック =====

    [Fact]
    public void FencedCodeBlock_KeepsLinesAndIgnoresMarkdownInside()
    {
        StaTest.Run(() =>
        {
            var blocks = Paragraphs("before\n```\n# not a heading\n- not a list\n```\nafter");

            Assert.Equal(3, blocks.Count);
            var code = TextOf(blocks[1]);
            Assert.Contains("# not a heading", code);
            Assert.Contains("- not a list", code);
        });
    }

    [Fact]
    public void UnclosedCodeBlock_IsStillRendered()
    {
        StaTest.Run(() =>
        {
            var blocks = Paragraphs("```\nstill code");

            Assert.Contains("still code", TextOf(Assert.Single(blocks)));
        });
    }

    // ===== 引用・リスト =====

    [Fact]
    public void Blockquote_IsItalicAndIndented()
    {
        StaTest.Run(() =>
        {
            var quote = Assert.Single(Paragraphs("> 引用の文章"));

            Assert.Equal("引用の文章", TextOf(quote));
            Assert.Equal(FontStyles.Italic, quote.FontStyle);
            Assert.True(quote.Margin.Left > 0);
        });
    }

    [Theory]
    [InlineData("- item", "• item")]
    [InlineData("* item", "• item")]
    [InlineData("+ item", "• item")]
    public void BulletList_AllMarkersGiveABullet(string markdown, string expected)
    {
        StaTest.Run(() =>
        {
            Assert.Equal(expected, TextOf(Assert.Single(Paragraphs(markdown))));
        });
    }

    [Fact]
    public void NumberedList_KeepsTheNumbers()
    {
        StaTest.Run(() =>
        {
            var items = Paragraphs("1. one\n2. two\n10. ten").Select(TextOf).ToArray();

            Assert.Equal(new[] { "1. one", "2. two", "10. ten" }, items);
        });
    }

    [Fact]
    public void BoldAtLineStart_IsNotMistakenForABullet()
    {
        StaTest.Run(() =>
        {
            var paragraph = Assert.Single(Paragraphs("**強調**から始まる文"));

            Assert.Equal("強調から始まる文", TextOf(paragraph));
        });
    }

    // ===== 入れ子のリスト（2026-10追加） =====

    private static double[] LeftMargins(string markdown) => Paragraphs(markdown).Select(p => p.Margin.Left).ToArray();

    [Fact]
    public void NestedBullets_AreIndentedDeeper_ByLevel()
    {
        StaTest.Run(() =>
        {
            var margins = LeftMargins("- a\n  - b\n    - c\n- d");

            Assert.True(margins[1] > margins[0]);
            Assert.True(margins[2] > margins[1]);
            Assert.Equal(margins[0], margins[3]); // 最初の階層に戻る
        });
    }

    [Fact]
    public void NestedBullets_UseADifferentGlyphPerLevel()
    {
        StaTest.Run(() =>
        {
            var texts = Paragraphs("- a\n  - b\n    - c\n      - d").Select(TextOf).ToArray();

            Assert.Equal(new[] { "• a", "◦ b", "▪ c", "• d" }, texts); // 4段目は、最初の記号へ戻る
        });
    }

    // 字下げが2つでも4つでも、タブでも、同じように入れ子になる。
    [Theory]
    [InlineData("- a\n  - b")]
    [InlineData("- a\n    - b")]
    [InlineData("- a\n\t- b")]
    public void NestedBullets_WorkWithTwoFourOrTabIndentation(string markdown)
    {
        StaTest.Run(() =>
        {
            var margins = LeftMargins(markdown);

            Assert.True(margins[1] > margins[0]);
        });
    }

    [Fact]
    public void NestedNumberedList_UnderABullet_IsIndented()
    {
        StaTest.Run(() =>
        {
            var blocks = Paragraphs("- parent\n  1. first\n  2. second");

            Assert.True(blocks[1].Margin.Left > blocks[0].Margin.Left);
            Assert.Equal(blocks[1].Margin.Left, blocks[2].Margin.Left);
            Assert.Equal("1. first", TextOf(blocks[1]));
        });
    }

    [Fact]
    public void ListLevel_ReturnsToTheMatchingLevel_NotJustOneUp()
    {
        StaTest.Run(() =>
        {
            var margins = LeftMargins("- a\n  - b\n    - c\n- d\n  - e");

            Assert.Equal(margins[0], margins[3]);
            Assert.Equal(margins[1], margins[4]); // 別の枝でも、同じ階層は、同じ字下げ
        });
    }

    // リスト以外の行をはさんだら、字下げの記憶はリセットされる（次のリストは、最初の階層から）。
    [Fact]
    public void ListLevel_ResetsAfterAParagraph()
    {
        StaTest.Run(() =>
        {
            var margins = Paragraphs("- a\n  - b\n\ntext\n\n  - c").Select(p => p.Margin.Left).ToArray();

            // 「  - c」は、新しいリストの最初の項目なので、最初の階層と同じ字下げになる。
            Assert.Equal(margins[0], margins[3]);
        });
    }

    [Fact]
    public void ListLevel_ResetsAfterAHeadingAndACodeBlock()
    {
        StaTest.Run(() =>
        {
            var afterHeading = Paragraphs("- a\n  - b\n# H\n  - c").Select(p => p.Margin.Left).ToArray();
            Assert.Equal(afterHeading[0], afterHeading[3]);

            var afterCode = Paragraphs("- a\n  - b\n```\ncode\n```\n  - c").Select(p => p.Margin.Left).ToArray();
            Assert.Equal(afterCode[0], afterCode[3]);
        });
    }

    // 空行をはさんだだけでは、同じリストが続く（緩いリスト）。
    [Fact]
    public void BlankLineBetweenItems_KeepsTheSameList()
    {
        StaTest.Run(() =>
        {
            var margins = LeftMargins("- a\n\n  - b\n\n- c");

            Assert.True(margins[1] > margins[0]);
            Assert.Equal(margins[0], margins[2]);
        });
    }

    // ===== テーブル =====

    private static List<List<string>> TableRows(string markdown)
    {
        var table = Render(markdown).OfType<Table>().Single();
        return table.RowGroups.SelectMany(g => g.Rows)
            .Select(r => r.Cells.Select(c => TextOf(c)).ToList())
            .ToList();
    }

    [Fact]
    public void Table_HeaderAndAllBodyRowsAreRendered()
    {
        StaTest.Run(() =>
        {
            // 過去に、最初の本文の行が抜ける不具合があった（回帰テスト）。
            var rows = TableRows("| A | B |\n|---|---|\n| 1 | 2 |\n| 3 | 4 |");

            Assert.Equal(3, rows.Count);
            Assert.Equal(new[] { "A", "B" }, rows[0]);
            Assert.Equal(new[] { "1", "2" }, rows[1]);
            Assert.Equal(new[] { "3", "4" }, rows[2]);
        });
    }

    [Fact]
    public void Table_WithAlignmentMarkersInTheSeparator_IsRecognized()
    {
        StaTest.Run(() =>
        {
            var rows = TableRows("| A | B |\n|:---|---:|\n| 1 | 2 |");

            Assert.Equal(2, rows.Count);
        });
    }

    [Fact]
    public void Table_KeepsInlineFormattingInCells()
    {
        StaTest.Run(() =>
        {
            var table = Render("| 名前 |\n|---|\n| **太字** |").OfType<Table>().Single();
            var cell = table.RowGroups[1].Rows[0].Cells[0];

            var paragraph = (Paragraph)cell.Blocks.First();
            Assert.Contains(paragraph.Inlines.OfType<Run>(), r => r.Text == "太字" && r.FontWeight == FontWeights.Bold);
        });
    }

    [Fact]
    public void PipeInPlainText_WithoutASeparatorRow_IsNotATable()
    {
        StaTest.Run(() =>
        {
            Assert.Empty(Render("| just | text |").OfType<Table>());
        });
    }

    // ===== テーブルのセル内改行・エスケープされたパイプ（2026-10追加） =====

    [Theory]
    [InlineData("<br>")]
    [InlineData("<br/>")]
    [InlineData("<br />")]
    [InlineData("<BR>")]
    public void TableCell_BrTagsBecomeLineBreaks(string tag)
    {
        StaTest.Run(() =>
        {
            var table = Render($"| A |\n|---|\n| 1行目{tag}2行目 |").OfType<Table>().Single();
            var paragraph = (Paragraph)table.RowGroups[1].Rows[0].Cells[0].Blocks.First();

            Assert.Single(paragraph.Inlines.OfType<LineBreak>());
            Assert.Contains("1行目", TextOf(paragraph));
            Assert.Contains("2行目", TextOf(paragraph));
        });
    }

    [Fact]
    public void TableCell_MultipleBrTags_GiveMultipleLineBreaks()
    {
        StaTest.Run(() =>
        {
            var table = Render("| A |\n|---|\n| a<br>b<br>c |").OfType<Table>().Single();
            var paragraph = (Paragraph)table.RowGroups[1].Rows[0].Cells[0].Blocks.First();

            Assert.Equal(2, paragraph.Inlines.OfType<LineBreak>().Count());
        });
    }

    [Fact]
    public void Br_InAnOrdinaryParagraph_IsAlsoALineBreak()
    {
        StaTest.Run(() =>
        {
            var paragraph = Assert.Single(Paragraphs("one<br>two"));

            Assert.Single(paragraph.Inlines.OfType<LineBreak>());
        });
    }

    [Fact]
    public void TableCell_EscapedPipe_IsLiteralNotASeparator()
    {
        StaTest.Run(() =>
        {
            var rows = TableRows("| 式 | 意味 |\n|---|---|\n| a \\| b | OR |");

            Assert.Equal(2, rows[1].Count);
            Assert.Equal("a | b", rows[1][0]);
            Assert.Equal("OR", rows[1][1]);
        });
    }

    [Fact]
    public void TableRow_TrailingEmptyCell_IsKept()
    {
        StaTest.Run(() =>
        {
            var rows = TableRows("| A | B |\n|---|---|\n| 1 | |");

            Assert.Equal(2, rows[1].Count);
            Assert.Equal(string.Empty, rows[1][1]);
        });
    }

    // ===== 脚注（2026-10追加） =====

    [Fact]
    public void Footnote_ReferenceBecomesASuperscriptNumber_AndTheDefinitionIsListedAtTheEnd()
    {
        StaTest.Run(() =>
        {
            var blocks = Paragraphs("本文[^note]です。\n\n[^note]: 脚注の内容");

            var body = blocks[0];
            var number = Assert.Single(body.Inlines.OfType<Run>(), r => r.BaselineAlignment == BaselineAlignment.Superscript);
            Assert.Equal("1", number.Text);
            Assert.Equal("本文1です。", TextOf(body));

            Assert.Equal("脚注", TextOf(blocks[1]));
            Assert.Equal("1. 脚注の内容", TextOf(blocks[2]));
        });
    }

    [Fact]
    public void Footnotes_AreNumberedByTheOrderOfFirstReference_NotByDefinitionOrder()
    {
        StaTest.Run(() =>
        {
            var blocks = Paragraphs("先に[^b]、次に[^a]。\n\n[^a]: Aの説明\n[^b]: Bの説明");

            Assert.Equal("先に1、次に2。", TextOf(blocks[0]));
            Assert.Equal("1. Bの説明", TextOf(blocks[2]));
            Assert.Equal("2. Aの説明", TextOf(blocks[3]));
        });
    }

    [Fact]
    public void Footnote_ReferencedTwice_KeepsOneNumber()
    {
        StaTest.Run(() =>
        {
            var blocks = Paragraphs("一回目[^x]と二回目[^x]。\n\n[^x]: 説明");

            Assert.Equal("一回目1と二回目1。", TextOf(blocks[0]));
            Assert.Equal(3, blocks.Count); // 本文・「脚注」見出し・脚注1件
        });
    }

    [Fact]
    public void Footnote_WithoutADefinition_IsLeftAsWritten()
    {
        StaTest.Run(() =>
        {
            var blocks = Paragraphs("定義のない[^missing]参照。");

            var only = Assert.Single(blocks);
            Assert.Equal("定義のない[^missing]参照。", TextOf(only));
            Assert.DoesNotContain(only.Inlines.OfType<Run>(), r => r.BaselineAlignment == BaselineAlignment.Superscript);
        });
    }

    [Fact]
    public void Footnote_DefinedButNeverReferenced_IsNotShown()
    {
        StaTest.Run(() =>
        {
            var blocks = Paragraphs("本文だけ。\n\n[^unused]: 参照されない脚注");

            Assert.Equal("本文だけ。", TextOf(Assert.Single(blocks)));
        });
    }

    [Fact]
    public void Footnote_DefinitionContinuationLines_AreJoined()
    {
        StaTest.Run(() =>
        {
            var blocks = Paragraphs("本文[^c]\n\n[^c]: 一行目\n    二行目");

            Assert.Equal("1. 一行目 二行目", TextOf(blocks[2]));
        });
    }

    [Fact]
    public void Footnote_DefinitionsCanContainFormatting()
    {
        StaTest.Run(() =>
        {
            var blocks = Paragraphs("本文[^f]\n\n[^f]: **重要**な注意");

            var definition = blocks[2];
            Assert.Contains(definition.Inlines.OfType<Run>(), r => r.Text == "重要" && r.FontWeight == FontWeights.Bold);
        });
    }

    // 脚注の中から、別の脚注を参照しても、番号が付く（定義の中の参照も、順に処理する）。
    [Fact]
    public void Footnote_ReferencedFromInsideAnotherFootnote_GetsItsOwnNumber()
    {
        StaTest.Run(() =>
        {
            var blocks = Paragraphs("本文[^one]\n\n[^one]: 最初の脚注（詳しくは[^two]）\n[^two]: 二つ目の脚注");

            var texts = blocks.Select(TextOf).ToList();
            Assert.Contains("1. 最初の脚注（詳しくは2）", texts);
            Assert.Contains("2. 二つ目の脚注", texts);
        });
    }

    [Fact]
    public void FootnoteDefinitionInsideACodeBlock_IsNotAFootnote()
    {
        StaTest.Run(() =>
        {
            var blocks = Paragraphs("```\n[^x]: コードの中\n```\n本文[^x]");

            Assert.Contains("[^x]: コードの中", TextOf(blocks[0]));
            Assert.Equal("本文[^x]", TextOf(blocks[1])); // 定義が無いので、そのまま
        });
    }

    [Fact]
    public void Footnotes_DoNotLeakBetweenRenders()
    {
        StaTest.Run(() =>
        {
            Render("a[^k]\n\n[^k]: first");

            var second = Paragraphs("b[^k]"); // 2回目には、定義が無い

            Assert.Equal("b[^k]", TextOf(Assert.Single(second)));
        });
    }

    [Fact]
    public void Footnote_IdIsCaseSensitive()
    {
        StaTest.Run(() =>
        {
            var blocks = Paragraphs("本文[^Note]\n\n[^note]: 小文字の定義");

            Assert.Equal("本文[^Note]", TextOf(Assert.Single(blocks)));
        });
    }

    // ===== 一般のリンク記法と脚注の取り違えがないこと =====

    [Fact]
    public void RegularLinks_StillWork_WhenFootnotesAreInTheSameDocument()
    {
        StaTest.Run(() =>
        {
            var blocks = Paragraphs("[リンク](https://example.com)と脚注[^1]\n\n[^1]: 説明");

            Assert.Single(blocks[0].Inlines.OfType<Hyperlink>());
            Assert.Equal("リンクと脚注1", TextOf(blocks[0]));
        });
    }
}
