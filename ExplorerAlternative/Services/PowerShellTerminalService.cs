using System.Diagnostics;
using System.IO;
using System.Windows;
using ExplorerAlternative.Services.Abstractions;

namespace ExplorerAlternative.Services;

/// <summary>
/// PowerShellプロセスをホストするターミナルサービス（仕様書9章）。
/// 標準入出力をリダイレクトし、VS Code統合ターミナルに近い対話操作を実現する。
/// </summary>
public sealed class PowerShellTerminalService : IPowerShellTerminalService
{
    private readonly string _shellExecutable;
    private Process? _process;

    public PowerShellTerminalService(string shellExecutable)
    {
        _shellExecutable = shellExecutable;
    }

    public event EventHandler<string>? OutputReceived;

    public event EventHandler<string>? ErrorOccurred;

    public bool IsRunning => _process is { HasExited: false };

    public void Start()
    {
        if (IsRunning)
        {
            return;
        }

        try
        {
            // 注意: "-Command -" を付けるとPowerShellは標準入力をEOFまで読み切ってから
            // 一括実行するモードになり、対話的に1行ずつ実行されなくなる（ターミナルが
            // 「使えない」ように見える不具合の原因）。引数なしで起動し、通常の対話型
            // ホストとして標準入力を1行ずつ読み取らせる。
            var startInfo = new ProcessStartInfo
            {
                FileName = _shellExecutable,
                Arguments = "-NoLogo -NoProfile",
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            _process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
            _process.OutputDataReceived += (_, e) => RaiseOutput(e.Data);
            _process.ErrorDataReceived += (_, e) => RaiseOutput(e.Data);
            _process.Start();
            _process.StandardInput.AutoFlush = true;
            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            RaiseError($"PowerShellの起動に失敗しました。({ex.Message})");
        }
    }

    public void SendCommand(string command)
    {
        if (!IsRunning)
        {
            RaiseError("ターミナルが起動していません。");
            return;
        }

        try
        {
            _process!.StandardInput.WriteLine(command);
        }
        catch (IOException ex)
        {
            RaiseError($"コマンドの送信に失敗しました。({ex.Message})");
        }
    }

    public void ChangeDirectory(string path)
    {
        SendCommand($"Set-Location -LiteralPath \"{path}\"");
    }

    public void Dispose()
    {
        try
        {
            if (_process is { HasExited: false })
            {
                _process.StandardInput.WriteLine("exit");
                _process.WaitForExit(1000);
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            // 終了処理中の失敗はアプリ終了を妨げないよう無視する。
        }
        finally
        {
            _process?.Dispose();
        }
    }

    private void RaiseOutput(string? data)
    {
        if (data is null)
        {
            return;
        }

        Application.Current?.Dispatcher.Invoke(() => OutputReceived?.Invoke(this, data));
    }

    private void RaiseError(string message)
    {
        Application.Current?.Dispatcher.Invoke(() => ErrorOccurred?.Invoke(this, message));
    }
}
