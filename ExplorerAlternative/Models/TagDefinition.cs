namespace ExplorerAlternative.Models;

public sealed class TagDefinition
{
    public required string Name { get; set; }

    /// <summary>仕様書5章「アイコンを選択可能」。既定は🏷。</summary>
    public string IconGlyph { get; set; } = "🏷";

    /// <summary>仕様書5章「必要なら色も設定可能」。#RRGGBB形式。未設定時は既定色を使う。</summary>
    public string? ColorHex { get; set; }
}
