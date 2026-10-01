using System.IO.Compression;
using System.Text;
using ExplorerAlternative.Rendering;

namespace ExplorerAlternative.Tests.Rendering;

// 仕様書13章「Office」：docx/xlsx/pptxの本文テキストの抽出。テスト用のOOXML（ZIP+XML）を作って確認する。
public sealed class OfficeTextExtractorTests : IDisposable
{
    private const string W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private const string S = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string A = "http://schemas.openxmlformats.org/drawingml/2006/main";

    private readonly string _root = Directory.CreateTempSubdirectory("eat_office_").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private string Zip(string name, params (string Entry, string Xml)[] entries)
    {
        var path = Path.Combine(_root, name);
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (entry, xml) in entries)
        {
            using var writer = new StreamWriter(archive.CreateEntry(entry).Open(), new UTF8Encoding(false));
            writer.Write(xml);
        }

        return path;
    }

    // ===== docx =====

    [Fact]
    public void Docx_ParagraphsPerLine_JoiningRuns()
    {
        var path = Zip("a.docx", ("word/document.xml",
            $"<w:document xmlns:w=\"{W}\"><w:body>" +
            "<w:p><w:r><w:t>Hello </w:t></w:r><w:r><w:t>World</w:t></w:r></w:p>" +
            "<w:p><w:r><w:t>日本語の段落</w:t></w:r></w:p>" +
            "</w:body></w:document>"));

        var text = OfficeTextExtractor.Extract(path, "docx");

        Assert.Equal($"Hello World{Environment.NewLine}日本語の段落", text);
    }

    [Fact]
    public void Docx_TableCellParagraphsAreIncluded()
    {
        var path = Zip("t.docx", ("word/document.xml",
            $"<w:document xmlns:w=\"{W}\"><w:body><w:tbl><w:tr><w:tc><w:p><w:r><w:t>cell</w:t></w:r></w:p></w:tc></w:tr></w:tbl></w:body></w:document>"));

        Assert.Contains("cell", OfficeTextExtractor.Extract(path, "docx"));
    }

    [Fact]
    public void Docx_WithoutDocumentXml_IsEmpty()
    {
        var path = Zip("empty.docx", ("other.xml", "<a/>"));

        Assert.Equal(string.Empty, OfficeTextExtractor.Extract(path, "docx"));
    }

    // ===== xlsx =====

    [Fact]
    public void Xlsx_SharedStringsInlineStringsAndNumbers_AreTabSeparated()
    {
        var path = Zip("a.xlsx",
            ("xl/sharedStrings.xml", $"<sst xmlns=\"{S}\"><si><t>名前</t></si><si><t>値</t></si></sst>"),
            ("xl/worksheets/sheet1.xml",
                $"<worksheet xmlns=\"{S}\"><sheetData>" +
                "<row><c t=\"s\"><v>0</v></c><c t=\"s\"><v>1</v></c></row>" +
                "<row><c t=\"inlineStr\"><is><t>abc</t></is></c><c><v>42</v></c></row>" +
                "</sheetData></worksheet>"));

        var lines = OfficeTextExtractor.Extract(path, "xlsx").Split(Environment.NewLine);

        Assert.Equal("--- sheet1.xml ---", lines[0]);
        Assert.Equal("名前\t値", lines[1]);
        Assert.Equal("abc\t42", lines[2]);
    }

    [Fact]
    public void Xlsx_SharedStringIndexOutOfRange_FallsBackToTheRawValue_WithoutCrashing()
    {
        var path = Zip("bad.xlsx",
            ("xl/sharedStrings.xml", $"<sst xmlns=\"{S}\"><si><t>only</t></si></sst>"),
            ("xl/worksheets/sheet1.xml", $"<worksheet xmlns=\"{S}\"><sheetData><row><c t=\"s\"><v>9</v></c></row></sheetData></worksheet>"));

        var text = OfficeTextExtractor.Extract(path, "xlsx");

        Assert.Contains("9", text);
    }

    [Fact]
    public void Xlsx_EmptyCellsKeepTheirColumn()
    {
        var path = Zip("gap.xlsx",
            ("xl/worksheets/sheet1.xml", $"<worksheet xmlns=\"{S}\"><sheetData><row><c><v>1</v></c><c/><c><v>3</v></c></row></sheetData></worksheet>"));

        var lines = OfficeTextExtractor.Extract(path, "xlsx").Split(Environment.NewLine);

        Assert.Equal("1\t\t3", lines[1]);
    }

    [Fact]
    public void Xlsx_SheetsAreListedInNameOrder()
    {
        var path = Zip("two.xlsx",
            ("xl/worksheets/sheet2.xml", $"<worksheet xmlns=\"{S}\"><sheetData><row><c><v>second</v></c></row></sheetData></worksheet>"),
            ("xl/worksheets/sheet1.xml", $"<worksheet xmlns=\"{S}\"><sheetData><row><c><v>first</v></c></row></sheetData></worksheet>"));

        var text = OfficeTextExtractor.Extract(path, "xlsx");

        Assert.True(text.IndexOf("first", StringComparison.Ordinal) < text.IndexOf("second", StringComparison.Ordinal));
    }

    // ===== pptx =====

    [Fact]
    public void Pptx_SlidesInNumericOrder_NotTextOrder()
    {
        string Slide(string text) => $"<p:sld xmlns:p=\"p\" xmlns:a=\"{A}\"><a:t>{text}</a:t></p:sld>";
        var path = Zip("a.pptx",
            ("ppt/slides/slide10.xml", Slide("ten")),
            ("ppt/slides/slide2.xml", Slide("two")),
            ("ppt/slides/slide1.xml", Slide("one")));

        var text = OfficeTextExtractor.Extract(path, "pptx");

        var one = text.IndexOf("one", StringComparison.Ordinal);
        var two = text.IndexOf("two", StringComparison.Ordinal);
        var ten = text.IndexOf("ten", StringComparison.Ordinal);
        Assert.True(one < two && two < ten);
    }

    // ===== 共通 =====

    [Fact]
    public void LongText_IsCutAtTheLimit()
    {
        var big = new string('あ', 250_000);
        var path = Zip("big.docx", ("word/document.xml", $"<w:document xmlns:w=\"{W}\"><w:body><w:p><w:r><w:t>{big}</w:t></w:r></w:p></w:body></w:document>"));

        Assert.Equal(200_000, OfficeTextExtractor.Extract(path, "docx").Length);
    }

    [Fact]
    public void UnsupportedExtension_Throws()
    {
        var path = Zip("a.zip", ("x.txt", "x"));

        Assert.Throws<NotSupportedException>(() => OfficeTextExtractor.Extract(path, "doc"));
    }

    [Fact]
    public void NotAZipFile_ThrowsInvalidData_ForTheCallerToReport()
    {
        var path = Path.Combine(_root, "fake.docx");
        File.WriteAllText(path, "not a zip");

        Assert.Throws<InvalidDataException>(() => OfficeTextExtractor.Extract(path, "docx"));
    }
}
