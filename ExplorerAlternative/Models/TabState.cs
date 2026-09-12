using System.Windows.Controls;

namespace ExplorerAlternative.Models;

/// <summary>
/// タブ1枚分の状態。19章の分割ペインに対応し、Panesは複数（Phase 1では最大2件）保持できる。
/// </summary>
public sealed class TabState
{
    public string Header { get; set; } = string.Empty;

    public List<PaneState> Panes { get; set; } = new();

    public int ActivePaneIndex { get; set; }

    public Orientation SplitOrientation { get; set; } = Orientation.Horizontal;

    /// <summary>仕様書10章「タブ固定」。</summary>
    public bool IsPinned { get; set; }
}
