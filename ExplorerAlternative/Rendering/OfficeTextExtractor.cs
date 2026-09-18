using System.IO.Compression;
using System.Xml.Linq;

namespace ExplorerAlternative.Rendering;

/// <summary>
/// 仕様書13章「Office」：docx/xlsx/pptxはOOXML形式（ZIP+XML）であることを利用し、
/// 外部NuGet依存を追加せず本文のテキストのみを抽出する簡易プレビュー。書式・画像・
/// レイアウトの再現は行わない（完全なOffice編集/表示機能は仕様上も対象外）。
/// </summary>
public static class OfficeTextExtractor
{
    private const int MaxChars = 200_000;

    private static readonly XNamespace WordNs = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static readonly XNamespace SpreadsheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace DrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/main";

    public static string Extract(string filePath, string extension)
    {
        using var archive = ZipFile.OpenRead(filePath);

        var text = extension switch
        {
            "docx" => ExtractWord(archive),
            "xlsx" => ExtractExcel(archive),
            "pptx" => ExtractPowerPoint(archive),
            _ => throw new NotSupportedException($"未対応の拡張子です: {extension}")
        };

        return text.Length > MaxChars ? text[..MaxChars] : text;
    }

    private static string ExtractWord(ZipArchive archive)
    {
        var entry = archive.GetEntry("word/document.xml");
        if (entry is null)
        {
            return string.Empty;
        }

        using var stream = entry.Open();
        var document = XDocument.Load(stream);
        var body = document.Root?.Element(WordNs + "body");
        if (body is null)
        {
            return string.Empty;
        }

        var paragraphs = body.Descendants(WordNs + "p")
            .Select(p => string.Concat(p.Descendants(WordNs + "t").Select(t => t.Value)));

        return string.Join(Environment.NewLine, paragraphs);
    }

    private static string ExtractExcel(ZipArchive archive)
    {
        var sharedStrings = LoadSharedStrings(archive);
        var sheetEntries = archive.Entries
            .Where(e => e.FullName.StartsWith("xl/worksheets/sheet", StringComparison.OrdinalIgnoreCase) && e.FullName.EndsWith(".xml"))
            .OrderBy(e => e.FullName)
            .ToList();

        var lines = new List<string>();

        foreach (var sheetEntry in sheetEntries)
        {
            lines.Add($"--- {sheetEntry.Name} ---");

            using var stream = sheetEntry.Open();
            var document = XDocument.Load(stream);
            var rows = document.Root?.Element(SpreadsheetNs + "sheetData")?.Elements(SpreadsheetNs + "row") ?? Enumerable.Empty<XElement>();

            foreach (var row in rows)
            {
                var cellValues = row.Elements(SpreadsheetNs + "c").Select(c => ReadCellValue(c, sharedStrings));
                lines.Add(string.Join("\t", cellValues));
            }
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static List<string> LoadSharedStrings(ZipArchive archive)
    {
        var entry = archive.GetEntry("xl/sharedStrings.xml");
        if (entry is null)
        {
            return new List<string>();
        }

        using var stream = entry.Open();
        var document = XDocument.Load(stream);

        return document.Root?.Elements(SpreadsheetNs + "si")
            .Select(si => string.Concat(si.Descendants(SpreadsheetNs + "t").Select(t => t.Value)))
            .ToList() ?? new List<string>();
    }

    private static string ReadCellValue(XElement cell, List<string> sharedStrings)
    {
        var type = cell.Attribute("t")?.Value;
        var valueElement = cell.Element(SpreadsheetNs + "v");

        if (type == "inlineStr")
        {
            return cell.Element(SpreadsheetNs + "is")?.Element(SpreadsheetNs + "t")?.Value ?? string.Empty;
        }

        if (valueElement is null)
        {
            return string.Empty;
        }

        if (type == "s" && int.TryParse(valueElement.Value, out var index) && index >= 0 && index < sharedStrings.Count)
        {
            return sharedStrings[index];
        }

        return valueElement.Value;
    }

    private static string ExtractPowerPoint(ZipArchive archive)
    {
        var slideEntries = archive.Entries
            .Where(e => e.FullName.StartsWith("ppt/slides/slide", StringComparison.OrdinalIgnoreCase) && e.FullName.EndsWith(".xml"))
            .OrderBy(e => ExtractSlideNumber(e.Name))
            .ToList();

        var slides = new List<string>();

        foreach (var slideEntry in slideEntries)
        {
            using var stream = slideEntry.Open();
            var document = XDocument.Load(stream);
            var text = string.Join(
                Environment.NewLine,
                document.Descendants(DrawingNs + "t").Select(t => t.Value));

            slides.Add($"--- {slideEntry.Name} ---{Environment.NewLine}{text}");
        }

        return string.Join(Environment.NewLine + Environment.NewLine, slides);
    }

    private static int ExtractSlideNumber(string fileName)
    {
        var digits = new string(fileName.Where(char.IsDigit).ToArray());
        return int.TryParse(digits, out var n) ? n : 0;
    }
}
