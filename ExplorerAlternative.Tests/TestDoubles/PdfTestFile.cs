using System.Text;

namespace ExplorerAlternative.Tests.TestDoubles;

/// <summary>
/// テスト用の、最小のPDFファイルを作る。各ページには、背景の四角形（ページごとに色が違う）と
/// 「Page n」という文字だけを描く。外部のファイルや、PDFのライブラリに頼らずに、
/// 実際のPDF描画（Windows標準）を試せるようにするためのもの。
/// </summary>
internal static class PdfTestFile
{
    /// <summary>ページの大きさ（ポイント）。</summary>
    public const int PageWidth = 300;

    public const int PageHeight = 400;

    public static byte[] Build(int pageCount)
    {
        // オブジェクト番号：1=Catalog 2=Pages 3=Font、4以降は、ページごとに（ページ,内容）の2つ。
        var objects = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            string.Empty, // Pages（あとで、Kidsが決まってから入れる）
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>"
        };

        var kids = new List<string>();

        for (var page = 0; page < pageCount; page++)
        {
            var pageObjectNumber = 4 + (page * 2);
            var contentObjectNumber = pageObjectNumber + 1;
            kids.Add($"{pageObjectNumber} 0 R");

            // ページごとに、背景の色を変える（赤の成分を変える）。その上に、黒い文字。
            var red = pageCount == 1 ? 1.0 : page / (double)(pageCount - 1);
            var content = $"{red:0.00} 0.80 0.80 rg 0 0 {PageWidth} {PageHeight} re f " +
                          $"0 0 0 rg BT /F1 36 Tf 40 200 Td (Page {page + 1}) Tj ET";

            objects.Add($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {PageWidth} {PageHeight}] /Contents {contentObjectNumber} 0 R /Resources << /Font << /F1 3 0 R >> >> >>");
            objects.Add($"<< /Length {content.Length} >>\nstream\n{content}\nendstream");
        }

        objects[1] = $"<< /Type /Pages /Kids [{string.Join(" ", kids)}] /Count {pageCount} >>";

        var builder = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int>();

        for (var i = 0; i < objects.Count; i++)
        {
            offsets.Add(builder.Length);
            builder.Append($"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }

        var xrefOffset = builder.Length;
        builder.Append($"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets)
        {
            builder.Append($"{offset:0000000000} 00000 n \n");
        }

        builder.Append($"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xrefOffset}\n%%EOF\n");

        // すべてASCII文字だけなので、文字数とバイト数が一致し、xrefのオフセットが正しくなる。
        return Encoding.ASCII.GetBytes(builder.ToString());
    }

    /// <summary>指定のフォルダに、PDFを書き出して、そのパスを返す。</summary>
    public static string Write(string folder, string name, int pageCount)
    {
        var path = Path.Combine(folder, name);
        File.WriteAllBytes(path, Build(pageCount));
        return path;
    }
}
