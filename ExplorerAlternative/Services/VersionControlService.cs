using System.Diagnostics;
using System.IO;
using ExplorerAlternative.Models;
using ExplorerAlternative.Services.Abstractions;

namespace ExplorerAlternative.Services;

/// <summary>
/// Git/SVN管理情報の判定（仕様書12章）。
/// 仕様書の文言どおり「現在のフォルダまたはその下位階層」を探索対象とする
/// （通常のリポジトリルート探索のように祖先フォルダは辿らない）。
/// 探索コストを抑えるため下位階層の探索深度には上限を設ける。
/// </summary>
public sealed class VersionControlService : IVersionControlService
{
    private const int MaxSearchDepth = 3;

    public VersionControlInfo Detect(string path)
    {
        if (!Directory.Exists(path))
        {
            return VersionControlInfo.None;
        }

        var gitRoot = FindMarker(path, ".git", MaxSearchDepth);
        if (gitRoot is not null)
        {
            return BuildGitInfo(gitRoot);
        }

        var svnRoot = FindMarker(path, ".svn", MaxSearchDepth);
        if (svnRoot is not null)
        {
            return BuildSvnInfo(svnRoot);
        }

        return VersionControlInfo.None;
    }

    private static string? FindMarker(string path, string markerName, int remainingDepth)
    {
        try
        {
            if (Directory.Exists(Path.Combine(path, markerName)) || File.Exists(Path.Combine(path, markerName)))
            {
                return path;
            }

            if (remainingDepth <= 0)
            {
                return null;
            }

            foreach (var subDirectory in Directory.EnumerateDirectories(path))
            {
                var name = Path.GetFileName(subDirectory);
                if (name is ".git" or ".svn")
                {
                    continue;
                }

                var found = FindMarker(subDirectory, markerName, remainingDepth - 1);
                if (found is not null)
                {
                    return found;
                }
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            return null;
        }

        return null;
    }

    private static VersionControlInfo BuildGitInfo(string root)
    {
        string? branch = null;
        string? status = null;

        try
        {
            var headPath = Path.Combine(root, ".git", "HEAD");
            if (File.Exists(headPath))
            {
                var headContent = File.ReadAllText(headPath).Trim();
                branch = headContent.StartsWith("ref: refs/heads/", StringComparison.Ordinal)
                    ? headContent["ref: refs/heads/".Length..]
                    : headContent;
            }
        }
        catch (IOException)
        {
            branch = null;
        }

        try
        {
            status = RunCommand(root, "git", "status --short --branch");
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            status = "Git情報の取得に失敗しました（git.exeが見つかりません）。";
        }

        return new VersionControlInfo
        {
            Kind = VersionControlKind.Git,
            RootPath = root,
            BranchName = branch,
            StatusSummary = status
        };
    }

    private static VersionControlInfo BuildSvnInfo(string root)
    {
        string? status;

        try
        {
            status = RunCommand(root, "svn", "status");
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            status = "SVN情報の取得に失敗しました（svn.exeが見つかりません）。";
        }

        return new VersionControlInfo
        {
            Kind = VersionControlKind.Svn,
            RootPath = root,
            BranchName = null,
            StatusSummary = status
        };
    }

    private static string RunCommand(string workingDirectory, string fileName, string arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"{fileName} を起動できませんでした。");

        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit(3000);
        return output.Trim();
    }
}
