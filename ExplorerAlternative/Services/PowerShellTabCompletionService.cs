using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using ExplorerAlternative.Services.Abstractions;

namespace ExplorerAlternative.Services;

/// <summary>
/// Tab補完の候補を、補完専用の常駐PowerShell（<c>TabExpansion2</c>）に問い合わせて求める（仕様書17章）。
/// ターミナル本体のPowerShellは標準入出力リダイレクト方式で対話的な補完をしないため、
/// 別のプロセスを1つだけ起動したままにして、1行ごとの問い合わせ（JSON）に答えさせる。
/// 補完できるのは、コマンド名・パラメーター・ファイルやフォルダのパス等。ターミナルの中で
/// 定義した変数や関数は、別のプロセスなので補完されない。
/// </summary>
public sealed class PowerShellTabCompletionService : ITabCompletionService
{
    // 1行ずつ問い合わせを読み、TabExpansion2の結果を1行のJSONで返す。失敗した問い合わせは「候補なし」を返す。
    private const string LoopScript = """
        $ErrorActionPreference = 'Stop'
        [Console]::InputEncoding = New-Object System.Text.UTF8Encoding($false)
        [Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false)
        while ($true) {
            $line = [Console]::In.ReadLine()
            if ($null -eq $line) { break }
            try {
                $request = $line | ConvertFrom-Json
                if ($request.cwd -and (Test-Path -LiteralPath $request.cwd -PathType Container)) {
                    Set-Location -LiteralPath $request.cwd
                }
                $result = TabExpansion2 -inputScript $request.input -cursorColumn $request.caret
                $matches = @($result.CompletionMatches | ForEach-Object { $_.CompletionText })
                $response = @{ i = $result.ReplacementIndex; l = $result.ReplacementLength; m = $matches }
            } catch {
                $response = @{ i = 0; l = 0; m = @() }
            }
            [Console]::Out.WriteLine(($response | ConvertTo-Json -Compress -Depth 3))
            [Console]::Out.Flush()
        }
        """;

    private static readonly UTF8Encoding Utf8NoBom = new(false);

    private readonly string _shellExecutable;
    private readonly TimeSpan _firstRequestTimeout;
    private readonly TimeSpan _requestTimeout;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Process? _process;
    private bool _answeredBefore;
    private bool _disposed;

    /// <param name="shellExecutable">補完に使うPowerShell（ターミナルと同じ実行ファイル）。</param>
    /// <param name="firstRequestTimeout">最初の問い合わせの待ち時間。PowerShellの起動と、補完機能の初期化に時間がかかるため長め。</param>
    /// <param name="requestTimeout">2回目以降の待ち時間。</param>
    public PowerShellTabCompletionService(string shellExecutable, TimeSpan? firstRequestTimeout = null, TimeSpan? requestTimeout = null)
    {
        _shellExecutable = shellExecutable;
        _firstRequestTimeout = firstRequestTimeout ?? TimeSpan.FromSeconds(15);
        _requestTimeout = requestTimeout ?? TimeSpan.FromSeconds(5);
    }

    public async Task<TabCompletionResult?> CompleteAsync(string input, int caretIndex, string? workingDirectory, CancellationToken cancellationToken)
    {
        if (_disposed)
        {
            return null;
        }

        await _gate.WaitAsync(cancellationToken);

        try
        {
            var process = EnsureStarted();
            if (process is null)
            {
                return null;
            }

            // 非ASCII文字は、JSONの\uXXXXに変換される（既定のエンコーダー）ため、文字コードの違いの影響を受けない。
            var request = JsonSerializer.Serialize(new { input, caret = caretIndex, cwd = workingDirectory ?? string.Empty });
            var timeout = _answeredBefore ? _requestTimeout : _firstRequestTimeout;

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(timeout);

            try
            {
                await process.StandardInput.WriteLineAsync(request.AsMemory(), timeoutCts.Token);
                await process.StandardInput.FlushAsync(timeoutCts.Token);
                var responseLine = await process.StandardOutput.ReadLineAsync(timeoutCts.Token);

                if (responseLine is null)
                {
                    // 補完用のPowerShellが終了していた。次の問い合わせで起動し直す。
                    StopProcess();
                    return null;
                }

                var result = Parse(responseLine);
                _answeredBefore = true;
                return result;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // 時間切れ。応答が遅れて届くと次の問い合わせの応答とずれるため、プロセスごと起動し直す。
                StopProcess();
                return null;
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or ObjectDisposedException)
            {
                StopProcess();
                return null;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private Process? EnsureStarted()
    {
        if (_process is { HasExited: false })
        {
            return _process;
        }

        StopProcess();

        try
        {
            var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(LoopScript));
            var startInfo = new ProcessStartInfo
            {
                FileName = _shellExecutable,
                Arguments = $"-NoLogo -NoProfile -NonInteractive -EncodedCommand {encoded}",
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardInputEncoding = Utf8NoBom,
                StandardOutputEncoding = Utf8NoBom
            };

            var process = Process.Start(startInfo);
            if (process is null)
            {
                return null;
            }

            // 標準エラーは読まないと、バッファが詰まって止まることがあるため、捨てながら読む。
            process.ErrorDataReceived += (_, _) => { };
            process.BeginErrorReadLine();

            _process = process;
            _answeredBefore = false;
            return process;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            // PowerShellを起動できない場合、補完は使えないだけで、ターミナルの動作は妨げない。
            return null;
        }
    }

    internal static TabCompletionResult? Parse(string responseLine)
    {
        try
        {
            using var document = JsonDocument.Parse(responseLine);
            var root = document.RootElement;

            var matches = new List<string>();
            if (root.TryGetProperty("m", out var matchesElement))
            {
                if (matchesElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in matchesElement.EnumerateArray())
                    {
                        if (item.ValueKind == JsonValueKind.String && item.GetString() is { Length: > 0 } text)
                        {
                            matches.Add(text);
                        }
                    }
                }
                else if (matchesElement.ValueKind == JsonValueKind.String && matchesElement.GetString() is { Length: > 0 } single)
                {
                    matches.Add(single);
                }
            }

            var index = root.TryGetProperty("i", out var i) && i.TryGetInt32(out var iv) ? iv : 0;
            var length = root.TryGetProperty("l", out var l) && l.TryGetInt32(out var lv) ? lv : 0;
            return new TabCompletionResult(Math.Max(0, index), Math.Max(0, length), matches);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // 自分が起動した補完用のプロセスだけを終了する。
    private void StopProcess()
    {
        var process = _process;
        _process = null;

        if (process is null)
        {
            return;
        }

        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // 既に終了している。
        }
        finally
        {
            process.Dispose();
        }
    }

    public void Dispose()
    {
        _disposed = true;
        StopProcess();
    }
}
