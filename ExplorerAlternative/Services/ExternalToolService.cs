using System.Diagnostics;
using ExplorerAlternative.Models;
using ExplorerAlternative.Services.Abstractions;

namespace ExplorerAlternative.Services;

/// <summary>
/// 外部ツール実行（仕様書22章）。引数中の "{path}" を対象パスへ置換して起動する。
/// </summary>
public sealed class ExternalToolService : IExternalToolService
{
    public void Run(ExternalToolDefinition tool, string targetPath)
    {
        try
        {
            var arguments = BuildArguments(tool.Arguments, targetPath);
            var startInfo = new ProcessStartInfo
            {
                FileName = tool.ExecutablePath,
                Arguments = arguments,
                UseShellExecute = true
            };

            Process.Start(startInfo);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            throw new AppOperationException($"外部ツール「{tool.Name}」を起動できませんでした。", ex);
        }
    }

    // 仕様書65章「任意EXE起動時の引数を適切にエスケープ」：対象パスに空白が含まれる場合
    // （Windowsでは珍しくない）、置換前のテンプレート側で既に引用符が付いていない限り、
    // 単一の引数として渡るよう自動的に引用符で囲む。
    private static string BuildArguments(string template, string targetPath)
    {
        const string placeholder = "{path}";
        var index = template.IndexOf(placeholder, StringComparison.Ordinal);

        if (index < 0)
        {
            return template;
        }

        var alreadyQuoted =
            index > 0 && template[index - 1] == '"' &&
            index + placeholder.Length < template.Length && template[index + placeholder.Length] == '"';

        var needsQuoting = !alreadyQuoted && targetPath.Contains(' ');
        var replacement = needsQuoting ? $"\"{targetPath}\"" : targetPath;

        return template.Replace(placeholder, replacement);
    }
}
