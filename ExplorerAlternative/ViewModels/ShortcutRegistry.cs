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
        new("プレビュー", "Space"),
    };
}
