using ExplorerAlternative.Models;

namespace ExplorerAlternative.Services.Abstractions;

/// <summary>
/// 仕様書13章・20章：Git/SVNの実際の操作（ステージ・コミット・プッシュ・プル・更新）。
/// 資格情報プロンプト等の対話にも対応できるよう、実行そのものは統合ターミナル（9章）へ
/// 委譲する方針とし、このサービスは各操作のコマンド文字列を組み立てる責務のみを持つ。
/// </summary>
public interface IVersionControlOperationsService
{
    /// <summary>変更を全てステージするコマンドを組み立てる（git add -A / svn add --force .）。</summary>
    string BuildStageCommand(VersionControlInfo vcsInfo);

    /// <summary>コミットコマンドを組み立てる（Git/SVN共通）。</summary>
    string BuildCommitCommand(VersionControlInfo vcsInfo, string message);

    /// <summary>プッシュコマンドを組み立てる（Gitのみ）。</summary>
    string BuildPushCommand(VersionControlInfo vcsInfo);

    /// <summary>プルコマンドを組み立てる（Gitのみ）。</summary>
    string BuildPullCommand(VersionControlInfo vcsInfo);

    /// <summary>更新コマンドを組み立てる（SVNのみ、svn update）。</summary>
    string BuildUpdateCommand(VersionControlInfo vcsInfo);

    /// <summary>Fetchコマンドを組み立てる（Gitのみ）。</summary>
    string BuildFetchCommand(VersionControlInfo vcsInfo);

    /// <summary>Stashコマンドを組み立てる（Gitのみ）。</summary>
    string BuildStashCommand(VersionControlInfo vcsInfo);

    /// <summary>Stash Popコマンドを組み立てる（Gitのみ）。</summary>
    string BuildStashPopCommand(VersionControlInfo vcsInfo);

    /// <summary>変更の破棄コマンドを組み立てる（Discard Changes / svn revert）。</summary>
    string BuildDiscardCommand(VersionControlInfo vcsInfo, string fullFilePath);

    /// <summary>ブランチ切り替えコマンドを組み立てる（Gitのみ）。</summary>
    string BuildCheckoutBranchCommand(VersionControlInfo vcsInfo, string branchName);

    /// <summary>新規ブランチ作成コマンドを組み立てる（Gitのみ）。</summary>
    string BuildCreateBranchCommand(VersionControlInfo vcsInfo, string branchName);

    /// <summary>Mergeコマンドを組み立てる（Gitのみ）。</summary>
    string BuildMergeCommand(VersionControlInfo vcsInfo, string branchName);

    /// <summary>Rebaseコマンドを組み立てる（Gitのみ）。</summary>
    string BuildRebaseCommand(VersionControlInfo vcsInfo, string branchName);

    /// <summary>指定フォルダをGitリポジトリとして初期化するコマンドを組み立てる。</summary>
    string BuildInitCommand(string folderPath);

    /// <summary>指定フォルダへリポジトリをCloneするコマンドを組み立てる。</summary>
    string BuildCloneCommand(string folderPath, string repositoryUrl);
}
