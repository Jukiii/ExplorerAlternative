namespace ExplorerAlternative.Models;

/// <summary>仕様書58章「重複ファイル検索」の結果1グループ（同一ハッシュ）。</summary>
public sealed class DuplicateFileGroup
{
    public required string Hash { get; init; }

    public required long SizeBytes { get; init; }

    public required IReadOnlyList<string> Paths { get; init; }
}
