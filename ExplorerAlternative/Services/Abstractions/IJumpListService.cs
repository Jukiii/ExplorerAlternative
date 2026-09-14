namespace ExplorerAlternative.Services.Abstractions;

/// <summary>
/// 仕様書39章「タスクバー / ジャンプリスト」：最近使ったフォルダ・よく使う場所・
/// お気に入り・ワークスペースをジャンプリストへ反映する。
/// </summary>
public interface IJumpListService
{
    void Rebuild(
        IEnumerable<(string Name, string Path)> recentPlaces,
        IEnumerable<(string Name, string Path)> frequentPlaces,
        IEnumerable<(string Name, string Path)> favorites,
        IEnumerable<string> workspaceNames);
}
