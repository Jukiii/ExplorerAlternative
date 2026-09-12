namespace ExplorerAlternative.Models;

public sealed class FileSystemEntry
{
    public required string Name { get; init; }

    public required string FullPath { get; init; }

    public required bool IsDirectory { get; init; }

    public long? SizeBytes { get; init; }

    public DateTime? LastModified { get; init; }

    public DateTime? Created { get; init; }

    /// <summary>仕様書49章：Hidden/Systemいずれかの属性を持つか。</summary>
    public bool IsHidden { get; init; }
}
