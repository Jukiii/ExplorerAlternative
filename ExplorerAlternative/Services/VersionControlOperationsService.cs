using System.IO;
using System.Text;
using ExplorerAlternative.Models;
using ExplorerAlternative.Services.Abstractions;

namespace ExplorerAlternative.Services;

/// <summary>
/// 仕様書13章・20章の実装。組み立てたコマンドは呼び出し側（PaneViewModel経由で
/// MainWindowViewModel）が統合ターミナル上で実行する。
/// </summary>
public sealed class VersionControlOperationsService : IVersionControlOperationsService
{
    public string BuildStageCommand(VersionControlInfo vcsInfo)
    {
        EnsureManaged(vcsInfo);

        var command = vcsInfo.Kind == VersionControlKind.Git ? "git add -A" : "svn add --force .";
        return WithWorkingDirectory(vcsInfo.RootPath!, command);
    }

    public string BuildCommitCommand(VersionControlInfo vcsInfo, string message)
    {
        EnsureManaged(vcsInfo);

        if (string.IsNullOrWhiteSpace(message))
        {
            throw new AppOperationException("コミットメッセージを入力してください。");
        }

        // PowerShellのネイティブコマンド引数再構築では、"`"" 等のエスケープを行っても
        // メッセージ中の二重引用符が消えてしまうことがある（例："a"b" → "ab"）。
        // メッセージファイル経由（-F）にすることで、引用符・バッククォート・改行を含む
        // 任意の内容を安全に渡せるようにする。
        var tool = vcsInfo.Kind == VersionControlKind.Git ? "git" : "svn";
        var messageFilePath = Path.GetTempFileName();

        try
        {
            File.WriteAllText(messageFilePath, message, new UTF8Encoding(false));
        }
        catch (IOException ex)
        {
            throw new AppOperationException($"コミットメッセージの一時ファイル作成に失敗しました。({ex.Message})");
        }

        var command = $"{tool} commit -F \"{messageFilePath}\"; Remove-Item -LiteralPath \"{messageFilePath}\" -Force";
        return WithWorkingDirectory(vcsInfo.RootPath!, command);
    }

    public string BuildPushCommand(VersionControlInfo vcsInfo)
    {
        EnsureGit(vcsInfo);
        return WithWorkingDirectory(vcsInfo.RootPath!, "git push");
    }

    public string BuildPullCommand(VersionControlInfo vcsInfo)
    {
        EnsureGit(vcsInfo);
        return WithWorkingDirectory(vcsInfo.RootPath!, "git pull");
    }

    public string BuildUpdateCommand(VersionControlInfo vcsInfo)
    {
        EnsureSvn(vcsInfo);
        return WithWorkingDirectory(vcsInfo.RootPath!, "svn update");
    }

    private static void EnsureManaged(VersionControlInfo vcsInfo)
    {
        if (vcsInfo.Kind == VersionControlKind.None || vcsInfo.RootPath is null)
        {
            throw new AppOperationException("Git/SVN管理下のフォルダではないため、この操作を実行できません。");
        }
    }

    private static void EnsureGit(VersionControlInfo vcsInfo)
    {
        EnsureManaged(vcsInfo);

        if (vcsInfo.Kind != VersionControlKind.Git)
        {
            throw new AppOperationException("この操作はGitリポジトリでのみ実行できます。");
        }
    }

    private static void EnsureSvn(VersionControlInfo vcsInfo)
    {
        EnsureManaged(vcsInfo);

        if (vcsInfo.Kind != VersionControlKind.Svn)
        {
            throw new AppOperationException("この操作はSVN管理下のフォルダでのみ実行できます。");
        }
    }

    private static string WithWorkingDirectory(string rootPath, string command)
    {
        return $"Set-Location -LiteralPath \"{rootPath}\"; {command}";
    }
}
