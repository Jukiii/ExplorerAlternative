using System.Diagnostics;
using System.IO;
using System.Linq;
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

    // 終了コードを問わない（例：git show/svn cat はファイルが管理外だと非0で終わる）。
    private static (string Output, bool Success) RunCommandAllowFailure(string workingDirectory, string fileName, string arguments)
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

        using var process = Process.Start(startInfo);
        if (process is null)
        {
            return (string.Empty, false);
        }

        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit(5000);
        return (output, process.ExitCode == 0);
    }

    public IReadOnlyDictionary<string, string> GetFileStatuses(VersionControlInfo vcsInfo)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (vcsInfo.Kind == VersionControlKind.None || vcsInfo.RootPath is null)
        {
            return result;
        }

        try
        {
            // 注意：RunCommand()はコンソール出力全体をTrim()するため、`git status --porcelain`の
            // 1行目先頭にある意味のある半角スペース（インデックス側が未変更であることを示す）が
            // 削れて列がずれてしまう。ここでは列位置が壊れないRunCommandAllowFailure()を使う。
            if (vcsInfo.Kind == VersionControlKind.Git)
            {
                var (output, _) = RunCommandAllowFailure(vcsInfo.RootPath, "git", "status --porcelain");
                ParseGitStatus(vcsInfo.RootPath, output, result);
            }
            else
            {
                var (output, _) = RunCommandAllowFailure(vcsInfo.RootPath, "svn", "status");
                ParseSvnStatus(vcsInfo.RootPath, output, result);
            }
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // 実行ファイルが見つからない等は状態バッジなしとして扱う（27章：クラッシュさせない）。
        }

        return result;
    }

    private static void ParseGitStatus(string root, string output, Dictionary<string, string> result)
    {
        foreach (var rawLine in output.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (line.Length < 4)
            {
                continue;
            }

            var indexStatus = line[0];
            var worktreeStatus = line[1];
            var relativePath = line[3..];

            var arrowIndex = relativePath.IndexOf(" -> ", StringComparison.Ordinal);
            if (arrowIndex >= 0)
            {
                relativePath = relativePath[(arrowIndex + 4)..];
            }

            relativePath = relativePath.Trim('"');
            var badge = MapGitBadge(indexStatus, worktreeStatus);
            if (badge is null)
            {
                continue;
            }

            var fullPath = Path.GetFullPath(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
            result[fullPath] = badge;
        }
    }

    private static string? MapGitBadge(char indexStatus, char worktreeStatus)
    {
        if (indexStatus == '?' && worktreeStatus == '?')
        {
            return "U";
        }

        if (indexStatus == 'R' || worktreeStatus == 'R')
        {
            return "R";
        }

        if (indexStatus == 'A' || worktreeStatus == 'A')
        {
            return "A";
        }

        if (indexStatus == 'D' || worktreeStatus == 'D')
        {
            return "D";
        }

        if (indexStatus == 'M' || worktreeStatus == 'M')
        {
            return "M";
        }

        return null;
    }

    private static void ParseSvnStatus(string root, string output, Dictionary<string, string> result)
    {
        foreach (var rawLine in output.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (line.Length <= 8)
            {
                continue;
            }

            var status = line[0];
            var badge = status switch
            {
                'M' => "M",
                'A' => "A",
                'D' => "D",
                '?' => "U",
                _ => (string?)null
            };

            if (badge is null)
            {
                continue;
            }

            var relativePath = line[8..].Trim();
            if (relativePath.Length == 0)
            {
                continue;
            }

            var fullPath = Path.GetFullPath(Path.IsPathRooted(relativePath) ? relativePath : Path.Combine(root, relativePath));
            result[fullPath] = badge;
        }
    }

    public IReadOnlyList<string> GetBranches(VersionControlInfo vcsInfo)
    {
        if (vcsInfo.Kind != VersionControlKind.Git || vcsInfo.RootPath is null)
        {
            return Array.Empty<string>();
        }

        try
        {
            var output = RunCommand(vcsInfo.RootPath, "git", "branch -a --format=%(refname:short)");
            return output
                .Split('\n')
                .Select(l => l.Trim())
                .Where(l => l.Length > 0 && !l.Contains("->", StringComparison.Ordinal))
                .Distinct()
                .ToList();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return Array.Empty<string>();
        }
    }

    public string? GetCommittedFileContent(VersionControlInfo vcsInfo, string fullFilePath)
    {
        if (vcsInfo.Kind == VersionControlKind.None || vcsInfo.RootPath is null)
        {
            return null;
        }

        try
        {
            if (vcsInfo.Kind == VersionControlKind.Git)
            {
                var relativePath = Path.GetRelativePath(vcsInfo.RootPath, fullFilePath).Replace(Path.DirectorySeparatorChar, '/');
                var (output, success) = RunCommandAllowFailure(vcsInfo.RootPath, "git", $"show HEAD:\"{relativePath}\"");
                return success ? output : null;
            }
            else
            {
                var (output, success) = RunCommandAllowFailure(vcsInfo.RootPath, "svn", $"cat \"{fullFilePath}\"");
                return success ? output : null;
            }
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return null;
        }
    }
}
