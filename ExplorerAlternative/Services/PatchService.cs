using System.Diagnostics;
using System.IO;
using System.Text;
using ExplorerAlternative.Models;
using ExplorerAlternative.Services.Abstractions;

namespace ExplorerAlternative.Services;

/// <summary>
/// 仕様書14章の実装。現在のフォルダのGit/SVN判定結果（<see cref="VersionControlInfo"/>）を
/// もとに、git diff / svn diff と git apply / svn patch へ委譲する。
/// </summary>
public sealed class PatchService : IPatchService
{
    public bool IsSupported => true;

    public void CreatePatch(VersionControlInfo vcsInfo, string outputFilePath)
    {
        EnsureManaged(vcsInfo);

        var (fileName, arguments) = vcsInfo.Kind == VersionControlKind.Git
            ? ("git", "diff")
            : ("svn", "diff");

        var (exitCode, output, error) = RunCommand(vcsInfo.RootPath!, fileName, arguments);

        if (exitCode != 0)
        {
            throw new AppOperationException($"Patchの作成に失敗しました。({FormatError(error)})");
        }

        try
        {
            File.WriteAllText(outputFilePath, output, new UTF8Encoding(false));
        }
        catch (IOException ex)
        {
            throw new AppOperationException($"Patchファイルの書き込みに失敗しました。({ex.Message})");
        }
    }

    public void ApplyPatch(VersionControlInfo vcsInfo, string patchFilePath)
    {
        EnsureManaged(vcsInfo);

        if (!File.Exists(patchFilePath))
        {
            throw new AppOperationException($"Patchファイル「{patchFilePath}」が見つかりません。");
        }

        var (fileName, arguments) = vcsInfo.Kind == VersionControlKind.Git
            ? ("git", $"apply \"{patchFilePath}\"")
            : ("svn", $"patch \"{patchFilePath}\" .");

        var (exitCode, _, error) = RunCommand(vcsInfo.RootPath!, fileName, arguments);

        if (exitCode != 0)
        {
            throw new AppOperationException($"Patchの適用に失敗しました。({FormatError(error)})");
        }
    }

    private static void EnsureManaged(VersionControlInfo vcsInfo)
    {
        if (vcsInfo.Kind == VersionControlKind.None || vcsInfo.RootPath is null)
        {
            throw new AppOperationException("Git/SVN管理下のフォルダではないため、Patch機能を使用できません。");
        }
    }

    private static string FormatError(string error)
    {
        return string.IsNullOrWhiteSpace(error) ? "詳細不明のエラー" : error.Trim();
    }

    private static (int ExitCode, string Output, string Error) RunCommand(string workingDirectory, string fileName, string arguments)
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

        try
        {
            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException($"{fileName} を起動できませんでした。");

            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            process.WaitForExit(10000);
            return (process.ExitCode, output, error);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            throw new AppOperationException(
                $"{fileName} の実行に失敗しました。{fileName}.exeがインストールされ、PATHに登録されているか確認してください。({ex.Message})");
        }
    }
}
