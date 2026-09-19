namespace ExplorerAlternative.Models;

/// <summary>仕様書16章「コードシンボル表示」：Quick Look内のソースコードプレビュー用の簡易シンボル。</summary>
public sealed class CodeSymbol
{
    public required string Name { get; set; }

    /// <summary>「class」「interface」「struct」「enum」「function」「method」「selector」「heading」等。</summary>
    public required string Kind { get; set; }

    /// <summary>1始まりの行番号。ジャンプ先の特定に使う。</summary>
    public int Line { get; set; }

    /// <summary>0=トップレベル、1=クラス/関数の直下、のように簡易的な入れ子の深さ。</summary>
    public int Depth { get; set; }

    public string DisplayText => $"{KindGlyph} {Name}";

    private string KindGlyph => Kind switch
    {
        "class" or "interface" or "struct" or "enum" or "record" => "🏛",
        "function" or "method" => "ƒ",
        "selector" => "🎨",
        "heading" => "§",
        _ => "•"
    };
}
