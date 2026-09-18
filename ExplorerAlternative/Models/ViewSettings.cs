namespace ExplorerAlternative.Models;

public sealed class ViewSettings
{
    public ViewMode DefaultViewMode { get; set; } = ViewMode.Tree;

    /// <summary>仕様書49章「隠しファイル」。既定は非表示（実Explorerの既定に合わせる）。</summary>
    public bool ShowHiddenFiles { get; set; }

    /// <summary>仕様書32章「ファイル操作プレビュー」：移動・コピー実行前に対象件数と移動元/移動先を
    /// 確認するダイアログを出すか。既定はOFF（従来通り即実行。ドラッグ&amp;ドロップ／切り取り貼り付け
    /// の操作性を変えないため）。</summary>
    public bool ConfirmMoveAndCopy { get; set; }
}
