namespace ExplorerAlternative.Models;

/// <summary>
/// ワークスペース1件分の状態。仕様書17章：ウィンドウサイズ・位置・タブ・各タブの状態・分割ペインの状態・
/// 各ペインの表示状態を保存できる構造にする。
/// </summary>
public sealed class WorkspaceState
{
    public required string Name { get; set; }

    public double WindowWidth { get; set; }

    public double WindowHeight { get; set; }

    public double WindowLeft { get; set; }

    public double WindowTop { get; set; }

    public List<TabState> Tabs { get; set; } = new();

    public int ActiveTabIndex { get; set; }

    public bool NavigationPaneCollapsed { get; set; }

    /// <summary>ウィンドウを最大化していたか（仕様書43章）。サイズ・位置は、最大化する前の大きさを保存する。</summary>
    public bool IsWindowMaximized { get; set; }

    /// <summary>統合ターミナルの状態（表示・タブ・Sync・高さ）。この機能の追加前に保存したワークスペースには無い（null）。</summary>
    public TerminalWorkspaceState? Terminal { get; set; }
}
