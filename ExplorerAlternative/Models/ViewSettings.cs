namespace ExplorerAlternative.Models;

public sealed class ViewSettings
{
    public ViewMode DefaultViewMode { get; set; } = ViewMode.Tree;

    /// <summary>仕様書49章「隠しファイル」。既定は非表示（実Explorerの既定に合わせる）。</summary>
    public bool ShowHiddenFiles { get; set; }
}
