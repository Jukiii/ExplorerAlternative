using ExplorerAlternative.Models;

namespace ExplorerAlternative.ViewModels;

/// <summary>Diffウィンドウの左右比較1行分（仕様書23章「左右比較」）。</summary>
public sealed class DiffRowViewModel
{
    public DiffLineKind LeftKind { get; init; } = DiffLineKind.Equal;

    public string LeftText { get; init; } = string.Empty;

    public int? LeftNumber { get; init; }

    public DiffLineKind RightKind { get; init; } = DiffLineKind.Equal;

    public string RightText { get; init; } = string.Empty;

    public int? RightNumber { get; init; }

    /// <summary>次/前の差分（仕様書23章）へジャンプする対象かどうか。</summary>
    public bool IsChange => LeftKind != DiffLineKind.Equal || RightKind != DiffLineKind.Equal;
}
