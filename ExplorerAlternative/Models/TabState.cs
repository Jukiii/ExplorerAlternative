namespace ExplorerAlternative.Models;

/// <summary>
/// タブ1枚分の状態。Phase 1ではPanesは常に1件だが、19章の分割ペイン拡張に備えて複数保持できる構造にする。
/// </summary>
public sealed class TabState
{
    public string Header { get; set; } = string.Empty;

    public List<PaneState> Panes { get; set; } = new();

    public int ActivePaneIndex { get; set; }
}
