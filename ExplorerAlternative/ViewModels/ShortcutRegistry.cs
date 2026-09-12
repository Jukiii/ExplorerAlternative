namespace ExplorerAlternative.ViewModels;

/// <summary>
/// アプリ全体のショートカット一覧。チートシート（仕様書23章）はここを表示するだけなので、
/// 今後ショートカットを追加する際はこのリストに追記すればよい。
/// </summary>
public static class ShortcutRegistry
{
    public static IReadOnlyList<ShortcutEntry> All { get; } = new List<ShortcutEntry>
    {
        new("ターミナル表示/非表示", "Ctrl + @（USキー配列ではCtrl + Shift + 2でも可）"),
        new("親フォルダへ移動", "Alt + ↑"),
        new("戻る", "Alt + ←"),
        new("進む", "Alt + →"),
        new("プレビュー", "Space"),
        new("プレビューを閉じる", "Esc（プレビューウィンドウ上）"),
        new("プレビューで前後移動", "← / →（プレビューウィンドウ上）"),
        new("アドレスバーを編集", "Ctrl + L / F6"),
        new("開く", "Enter"),
        new("名前の変更", "F2"),
        new("削除", "Delete"),
        new("フォルダを展開", "→"),
        new("フォルダを折りたたむ", "←"),
        new("新しいタブ", "Ctrl + T"),
        new("タブを閉じる", "Ctrl + W"),
        new("次のタブへ切替", "Ctrl + Tab"),
        new("タブを複製", "Ctrl + ドラッグ"),
        new("検索", "Ctrl + F"),
        new("コマンドパレット", "Ctrl + Shift + P"),
        new("現在の場所をブックマークに追加", "Ctrl + D"),
        new("最新の情報に更新", "F5"),
    };
}
