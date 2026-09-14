namespace ExplorerAlternative.Services.Abstractions;

/// <summary>
/// ファイル操作の元に戻す（Undo、仕様書62章）。移動・コピー・名前変更・複製・新規作成・
/// ショートカット作成を対象とする。ごみ箱へ送る削除の復元にはWindowsのごみ箱APIとの連携
/// （COM相互運用）が必要となり対象外とする（ごみ箱から手動で復元するか、エクスプローラー
/// 自体のUndoを利用する）。
/// </summary>
public interface IUndoService
{
    bool CanUndo { get; }

    /// <summary>次にUndoされる操作の説明（メニュー表示用）。Undo可能な操作がない場合はnull。</summary>
    string? NextUndoDescription { get; }

    /// <summary>Undo可能な操作の記録・消費のたびに発火する（UIのCanExecute再評価用）。</summary>
    event Action? Changed;

    /// <summary>操作の完了後に呼び出し、Undoのための巻き戻し処理を記録する。</summary>
    void Record(string description, Action undo);

    /// <summary>直近の操作を1件元に戻す。Undo可能な操作がない場合は何もしない。</summary>
    void Undo();
}
