namespace ExplorerAlternative.ViewModels;

/// <summary>
/// アプリ全体のショートカット一覧。チートシート（仕様書23章）はここを表示するだけなので、
/// 今後ショートカットを追加する際はこのリストに追記すればよい。ジャンル（Category）ごとに
/// まとめて表示するため、既存の分類に合うものはそこへ、無ければ新しいCategory名を追加する。
/// </summary>
public static class ShortcutRegistry
{
    public static IReadOnlyList<ShortcutEntry> All { get; } = new List<ShortcutEntry>
    {
        new("移動", "親フォルダへ移動", "Alt + ↑"),
        new("移動", "戻る", "Alt + ←"),
        new("移動", "進む", "Alt + →"),
        new("移動", "フォルダを展開", "→"),
        new("移動", "フォルダを折りたたむ", "←"),
        new("移動", "アドレスバーを編集", "Ctrl + L / F6"),

        new("ファイル操作", "開く", "Enter"),
        new("ファイル操作", "名前の変更", "F2"),
        new("ファイル操作", "削除", "Delete"),
        new("ファイル操作", "選択したファイル・フォルダを複製", "Ctrl + D"),
        new("ファイル操作", "新しいフォルダを作成", "Ctrl + Shift + N"),
        new("ファイル操作", "最新の情報に更新", "F5"),

        new("プレビュー", "プレビュー", "Space"),
        new("プレビュー", "プレビューを閉じる", "Esc（プレビューウィンドウ上）"),
        new("プレビュー", "プレビューで前後移動", "← / →（プレビューウィンドウ上）"),

        new("タブ・ウィンドウ", "新しいタブ", "Ctrl + T"),
        new("タブ・ウィンドウ", "タブを閉じる", "Ctrl + W"),
        new("タブ・ウィンドウ", "次のタブへ切替", "Ctrl + Tab"),
        new("タブ・ウィンドウ", "タブを複製", "Ctrl + ドラッグ"),
        new("タブ・ウィンドウ", "新しいウィンドウを開く", "Ctrl + N"),

        new("ターミナル", "ターミナル表示/非表示", "Ctrl + @（USキー配列ではCtrl + Shift + 2でも可）"),

        new("検索・その他", "検索", "Ctrl + F"),
        new("検索・その他", "コマンドパレット", "Ctrl + Shift + P"),
        new("検索・その他", "設定を開く", "Ctrl + ,"),
    };
}
