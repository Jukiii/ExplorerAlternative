using System.Diagnostics;
using ExplorerAlternative.Services;
using ExplorerAlternative.ViewModels;

namespace ExplorerAlternative.Tests.Services;

// 仕様書17章：実際のWindows PowerShellを起動し、CRで行を上書きする出力（プログレスバー）が、
// ターミナルのスクロールバックでは最後の状態の1行になることを確認する結合テスト。
public sealed class TerminalProgressIntegrationTests : IDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    private readonly PowerShellTerminalService _service;
    private readonly TerminalViewModel _terminal;
    private readonly object _lock = new();

    public TerminalProgressIntegrationTests()
    {
        // UIスレッドが無いテスト環境のため、通知は受信スレッドでそのまま実行する。
        _service = new PowerShellTerminalService("powershell.exe", loadProfile: false, invokeOnUi: action => action());
        _terminal = new TerminalViewModel(_service, syncByDefault: false, "PowerShell 1");
        _terminal.SegmentsAppended += _ =>
        {
            lock (_lock)
            {
            }
        };
    }

    public void Dispose() => _service.Dispose();

    private string BufferText()
    {
        lock (_lock)
        {
            return string.Concat(_terminal.Buffer.ToArray().Select(s => s.Text));
        }
    }

    private bool WaitFor(Func<string, bool> condition)
    {
        var stopwatch = Stopwatch.StartNew();

        while (stopwatch.Elapsed < Timeout)
        {
            if (condition(BufferText()))
            {
                return true;
            }

            Thread.Sleep(50);
        }

        return condition(BufferText());
    }

    [Fact]
    public void CarriageReturnProgress_ShowsOnlyTheFinalStateInTheScrollback()
    {
        _terminal.Start();
        Assert.True(WaitFor(text => text.Contains("PS ", StringComparison.Ordinal)), "シェルの起動を確認できませんでした。");

        // 各更新のあいだに少し待ち、別々のチャンクとして届くようにする（実際のプログレスバーと同じ）。
        // シェルは入力したコマンドをそのまま画面に表示（エコー）するため、判定に使う文字列は、コマンドの中では
        // 連結されていない形（'progress-' + 'finished' など）にして、実際の出力と区別する。
        _service.SendCommand(
            "1..4 | ForEach-Object { [Console]::Out.Write(\"progress $_/4`r\"); [Console]::Out.Flush(); Start-Sleep -Milliseconds 300 }; " +
            "[Console]::Out.WriteLine(\"progress {0}/4\" -f 4); Write-Output ('progress-' + 'finished')");

        Assert.True(WaitFor(text => text.Contains("progress-finished", StringComparison.Ordinal)),
            $"出力が届きませんでした。受信内容: [{BufferText()}]");

        var text = BufferText();

        // 上書きされた途中の状態は残らず、最後の状態だけが残る。
        Assert.Contains("progress 4/4", text, StringComparison.Ordinal);
        Assert.False(text.Contains("progress 1/4", StringComparison.Ordinal), $"途中の状態が残っています: [{text.Replace("\n", "\\n")}]");
        Assert.DoesNotContain("progress 2/4", text, StringComparison.Ordinal);
        Assert.DoesNotContain("progress 3/4", text, StringComparison.Ordinal);
    }
}
