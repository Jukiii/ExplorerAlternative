namespace ExplorerAlternative.Models;

/// <summary>仕様書45章「フォルダ同期」の差分確認結果1件のステータス。</summary>
public enum FolderCompareStatus
{
    /// <summary>左側にのみ存在する。</summary>
    OnlyLeft,

    /// <summary>右側にのみ存在する。</summary>
    OnlyRight,

    /// <summary>両側に存在するが内容が異なる。</summary>
    Different,

    /// <summary>両側に存在し、内容も同一。</summary>
    Same
}

/// <summary>仕様書45章「フォルダ同期」の差分確認結果1件（1ファイル分）。</summary>
public sealed class FolderCompareEntry
{
    public required string RelativePath { get; init; }

    public required FolderCompareStatus Status { get; init; }

    public required string? LeftFullPath { get; init; }

    public required string? RightFullPath { get; init; }
}
