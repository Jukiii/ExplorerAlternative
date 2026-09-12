using ExplorerAlternative.Models;

namespace ExplorerAlternative.Services.Abstractions;

/// <summary>
/// 仕様書14章：Patch作成・適用機能。Git/SVNのdiff/apply機能に委譲する。
/// </summary>
public interface IPatchService
{
    bool IsSupported { get; }

    /// <summary>現在の変更内容（git diff / svn diff）からPatchファイルを作成する（14.1章）。</summary>
    void CreatePatch(VersionControlInfo vcsInfo, string outputFilePath);

    /// <summary>Patchファイルを現在のGit/SVN管理フォルダへ適用する（14.2章）。</summary>
    void ApplyPatch(VersionControlInfo vcsInfo, string patchFilePath);
}
