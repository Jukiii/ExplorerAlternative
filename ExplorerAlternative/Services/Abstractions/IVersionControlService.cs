using ExplorerAlternative.Models;

namespace ExplorerAlternative.Services.Abstractions;

public interface IVersionControlService
{
    /// <summary>
    /// 指定フォルダについてGit/SVNの管理情報を判定する。仕様書12章：現在のフォルダまたは
    /// その下位階層に管理情報が存在する場合に情報を返す。どちらにも該当しない場合は
    /// <see cref="VersionControlInfo.None"/> を返す。
    /// </summary>
    VersionControlInfo Detect(string path);

    /// <summary>仕様書21章「Explorer上：M Modified / A Added / D Deleted / U Untracked / R Renamed」。
    /// フルパス→ステータス文字（M/A/D/U/R）の辞書を返す。管理外の場合は空の辞書。</summary>
    IReadOnlyDictionary<string, string> GetFileStatuses(VersionControlInfo vcsInfo);

    /// <summary>仕様書21章「ブランチ一覧」。ローカル・リモートのブランチ名を返す（Gitのみ）。</summary>
    IReadOnlyList<string> GetBranches(VersionControlInfo vcsInfo);

    /// <summary>仕様書23章のDiff表示用。コミット済み（HEAD/BASE）時点のファイル内容を返す。
    /// 新規追加・未管理などで取得できない場合はnull。</summary>
    string? GetCommittedFileContent(VersionControlInfo vcsInfo, string fullFilePath);
}
