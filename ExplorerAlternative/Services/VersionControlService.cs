using System.Diagnostics;
using System.IO;
using System.Linq;
using ExplorerAlternative.Models;
using ExplorerAlternative.Services.Abstractions;

namespace ExplorerAlternative.Services;

/// <summary>
/// Git/SVN管理情報の判定（仕様書12章・20章）。
/// 仕様書20章「現在パスから親方向へ探索」のとおり、現在のフォルダまたはその祖先
/// フォルダに管理情報が存在する場合にのみ表示する（下位階層は探索しない）。
/// </summary>
public sealed class VersionControlService : IVersionControlService
{
    public VersionControlInfo Detect(string path)
    {
        if (!Directory.Exists(path))
        {
            return VersionControlInfo.None;
        }

        var gitRoot = FindMarkerUpward(path, ".git");
        if (gitRoot is not null)
        {
            return BuildGitInfo(gitRoot);
        }

        var svnRoot = FindMarkerUpward(path, ".svn");
        if (svnRoot is not null)
        {
            return BuildSvnInfo(svnRoot);
        }

        return VersionControlInfo.None;
    }

    private static string? FindMarkerUpward(string path, string markerName)
    {
        var current = path;

        while (!string.IsNullOrEmpty(current))
        {
            try
            {
                if (Directory.Exists(Path.Combine(current, markerName)) || File.Exists(Path.Combine(current, markerName)))
                {
                    return current;
                }
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                return null;
            }

            var parent = Directory.GetParent(current);
            if (parent is null)
            {
                break;
            }

            current = parent.FullName;
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
            // git/svnはUTF-8で出力するが、指定しないと.NETがシステムのANSI/OEMコードページ
            // （日本語Windowsだと既定でCP932）で解釈してしまい、日本語のコミットメッセージ等が
            // 文字化けする。単体テスト実施後のUI動作確認で発見。
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8,
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
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8,
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

    public IReadOnlyList<CommitLogEntry> GetCommitLog(VersionControlInfo vcsInfo, int maxCount)
    {
        if (vcsInfo.Kind == VersionControlKind.None || vcsInfo.RootPath is null)
        {
            return Array.Empty<CommitLogEntry>();
        }

        try
        {
            return vcsInfo.Kind == VersionControlKind.Git
                ? GetGitCommitLog(vcsInfo.RootPath, maxCount)
                : GetSvnCommitLog(vcsInfo.RootPath, maxCount);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // 27章：git/svnが見つからない等でクラッシュさせない。空のログとして扱う。
            return Array.Empty<CommitLogEntry>();
        }
    }

    public string GetCommitDiff(VersionControlInfo vcsInfo, string revision)
    {
        if (vcsInfo.Kind == VersionControlKind.None || vcsInfo.RootPath is null || string.IsNullOrWhiteSpace(revision))
        {
            return string.Empty;
        }

        try
        {
            var (output, success) = vcsInfo.Kind == VersionControlKind.Git
                ? RunCommandAllowFailure(vcsInfo.RootPath, "git", $"show {revision}")
                : RunCommandAllowFailure(vcsInfo.RootPath, "svn", $"diff -c {revision}");

            return success ? output : string.Empty;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // 27章：git/svnが見つからない等でクラッシュさせない。
            return string.Empty;
        }
    }

    private static IReadOnlyList<CommitLogEntry> GetGitCommitLog(string root, int maxCount)
    {
        // \x1f（フィールド区切り）はコミットメッセージ中に出現しない制御文字のため、
        // メッセージに"|"等が含まれていても列がずれない。
        var (output, success) = RunCommandAllowFailure(
            root, "git", $"log -n {maxCount} --date=short --pretty=format:%h%an%ad%s");

        if (!success)
        {
            return Array.Empty<CommitLogEntry>();
        }

        var results = new List<CommitLogEntry>();

        foreach (var rawLine in output.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (line.Length == 0)
            {
                continue;
            }

            var fields = line.Split('');
            if (fields.Length < 4)
            {
                continue;
            }

            results.Add(new CommitLogEntry
            {
                Revision = fields[0],
                Author = fields[1],
                Date = fields[2],
                Message = fields[3]
            });
        }

        return results;
    }

    private static IReadOnlyList<CommitLogEntry> GetSvnCommitLog(string root, int maxCount)
    {
        var (output, success) = RunCommandAllowFailure(root, "svn", $"log -l {maxCount}");

        if (!success)
        {
            return Array.Empty<CommitLogEntry>();
        }

        var results = new List<CommitLogEntry>();

        // svn logの出力は各コミットが "----...----" 区切り線で分かれ、1行目が
        // "r123 | author | 2026-01-02 10:00:00 +0900 (...) | 1 line" 形式、
        // 空行を挟んでメッセージ本文が続く。
        var blocks = output.Split("------------------------------------------------------------------", StringSplitOptions.RemoveEmptyEntries);

        foreach (var block in blocks)
        {
            var lines = block.Split('\n').Select(l => l.TrimEnd('\r')).ToList();
            var headerLine = lines.FirstOrDefault(l => l.Length > 0);
            if (headerLine is null)
            {
                continue;
            }

            var parts = headerLine.Split(" | ");
            if (parts.Length < 3)
            {
                continue;
            }

            var messageLines = lines
                .SkipWhile(l => l != headerLine)
                .Skip(1)
                .SkipWhile(string.IsNullOrWhiteSpace)
                .ToList();

            var message = string.Join(" ", messageLines).Trim();

            results.Add(new CommitLogEntry
            {
                Revision = parts[0].Trim(),
                Author = parts[1].Trim(),
                Date = parts[2].Trim().Split(' ').FirstOrDefault() ?? parts[2].Trim(),
                Message = string.IsNullOrEmpty(message) ? "(コミットメッセージなし)" : message
            });
        }

        return results;
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
