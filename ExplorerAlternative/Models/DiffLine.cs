namespace ExplorerAlternative.Models;

public enum DiffLineKind
{
    Equal,
    Added,
    Removed
}

/// <summary>行単位のDiff結果1件（仕様書23章）。</summary>
public sealed class DiffLine
{
    public required DiffLineKind Kind { get; init; }

    public required string Text { get; init; }
}
