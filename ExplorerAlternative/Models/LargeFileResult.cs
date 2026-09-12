namespace ExplorerAlternative.Models;

/// <summary>仕様書38章「巨大ファイル検索」の結果1件。</summary>
public sealed class LargeFileResult
{
    public required string FullPath { get; init; }

    public required long SizeBytes { get; init; }
}
