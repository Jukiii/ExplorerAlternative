namespace ExplorerAlternative.ViewModels;

/// <summary>
/// チートシート（仕様書23章）に表示する1行分の情報。
/// 今後追加されるショートカットは<see cref="ShortcutRegistry"/>へ追加していく。
/// </summary>
public sealed record ShortcutEntry(string Category, string Action, string Gesture);
