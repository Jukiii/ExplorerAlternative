namespace ExplorerAlternative.Models;

/// <summary>仕様書21章「Log」の1コミット分。</summary>
public sealed class CommitLogEntry
{
    public required string Revision { get; init; }

    public required string Author { get; init; }

    public required string Date { get; init; }

    public required string Message { get; init; }
}
