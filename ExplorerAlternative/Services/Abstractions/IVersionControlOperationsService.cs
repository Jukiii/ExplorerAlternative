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
}
