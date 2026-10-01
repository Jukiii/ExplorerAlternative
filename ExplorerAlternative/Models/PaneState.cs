namespace ExplorerAlternative.Models;

/// <summary>
/// 分割ペイン1枠分の状態。仕様書17章：ワークスペースに「各ペインの表示状態」を保存できる構造にする。
/// </summary>
public sealed class PaneState
{
    public string CurrentPath { get; set; } = string.Empty;

    public ViewMode ViewMode { get; set; } = ViewMode.Tree;

    /// <summary>階層表示で展開していたフォルダのフルパス（仕様書43章「展開状態」）。古い保存データには無いため、既定は空。</summary>
    public List<string> ExpandedPaths { get; set; } = new();
}
