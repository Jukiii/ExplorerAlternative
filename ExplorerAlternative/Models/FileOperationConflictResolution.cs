namespace ExplorerAlternative.Models;

/// <summary>仕様書26章「同名ファイル競合」の解決方法。</summary>
public enum FileOperationConflictResolution
{
    Overwrite,
    OverwriteAll,
    Skip,
    SkipAll,
    Rename,
    Cancel
}
