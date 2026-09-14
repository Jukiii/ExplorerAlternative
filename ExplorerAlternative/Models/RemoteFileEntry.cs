namespace ExplorerAlternative.Models;

/// <summary>仕様書44章「SFTPリモートファイル操作」のリモート側1エントリ。</summary>
public sealed class RemoteFileEntry
{
    public required string Name { get; init; }

    public required string FullPath { get; init; }

    public required bool IsDirectory { get; init; }

    public required long SizeBytes { get; init; }

    public required DateTime LastModifiedUtc { get; init; }
}
