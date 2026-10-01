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

    /// <summary>分割ペインで、最初のペインが占める割合（0.1〜0.9。仕様書19章「ペインサイズ変更」）。古い保存データには無いため、既定は半分ずつ。</summary>
    public double SplitRatio { get; set; } = 0.5;

    /// <summary>仕様書10章「タブ固定」。</summary>
    public bool IsPinned { get; set; }
}
