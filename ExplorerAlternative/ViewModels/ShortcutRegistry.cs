namespace ExplorerAlternative.ViewModels;

/// <summary>
/// アプリ全体のショートカット一覧。チートシート（仕様書23章）はここを表示するだけなので、
/// 今後ショートカットを追加する際はこのリストに追記すればよい。
/// </summary>
public static class ShortcutRegistry
{
    public static IReadOnlyList<ShortcutEntry> All { get; } = new List<ShortcutEntry>
    {
        new("ターミナル表示/非表示", "Ctrl + @"),
        new("親フォルダへ移動", "Alt + ↑"),
        new("戻る", "Alt + ←"),
        new("進む", "Alt + →"),
        new("プレビュー", "Space"),
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
    };
}
