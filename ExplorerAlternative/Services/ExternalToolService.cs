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
            var arguments = tool.Arguments.Replace("{path}", targetPath);
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
}
