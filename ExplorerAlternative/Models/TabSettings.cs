namespace ExplorerAlternative.Models;

/// <summary>仕様書37章「スマートタブ」：フォルダを新しいタブで開く際の重複抑制方針。</summary>
public enum DuplicateTabBehavior
{
    /// <summary>常に新しいタブを作成する。</summary>
    AlwaysNew,

    /// <summary>同じフォルダを表示しているタブが既にあれば、それを再利用する。</summary>
    ReuseExisting,

    /// <summary>自動判定（現時点ではReuseExistingと同じ挙動）。</summary>
    Auto
}

public sealed class TabSettings
{
    public DuplicateTabBehavior DuplicateBehavior { get; set; } = DuplicateTabBehavior.Auto;
}
