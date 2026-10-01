namespace ExplorerAlternative.Services.Abstractions;

/// <summary>Undo履歴の1件（仕様書30章「GUI Undo履歴」表示用）。</summary>
public sealed record UndoHistoryEntry(Guid Id, string Description);

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

    /// <summary>仕様書30章「GUI Undo履歴」：記録済みの操作一覧（古い順）。</summary>
    IReadOnlyList<UndoHistoryEntry> History { get; }

    /// <summary>元に戻した操作をやり直せるか（仕様書30章「Redo」）。</summary>
    bool CanRedo { get; }

    /// <summary>次にやり直される操作の説明（メニュー表示用）。やり直せる操作がない場合はnull。</summary>
    string? NextRedoDescription { get; }

    /// <summary>Undo/Redo可能な操作の記録・消費のたびに発火する（UIのCanExecute再評価用）。</summary>
    event Action? Changed;

    /// <summary>
    /// 操作の完了後に呼び出し、Undoのための巻き戻し処理を記録する。
    /// <paramref name="redo"/>は、元に戻した操作をやり直すための処理で、省略した操作はやり直せない。
    /// 新しい操作を記録すると、それまでやり直せた操作は無効になる。
    /// </summary>
    void Record(string description, Action undo, Action? redo = null);

    /// <summary>直近の操作を1件元に戻す。Undo可能な操作がない場合は何もしない。</summary>
    void Undo();

    /// <summary>最後に元に戻した操作をやり直す。やり直せる操作がない場合は何もしない。</summary>
    void Redo();

    /// <summary>仕様書30章：指定した操作までさかのぼって元に戻す（最新の操作から順に、
    /// 指定した操作を含めて実行）。個別の操作が失敗した場合はスキップして残りを続行し、
    /// 失敗した操作の説明を返す（成功時は空リスト）。</summary>
    IReadOnlyList<string> UndoTo(Guid id);
}
