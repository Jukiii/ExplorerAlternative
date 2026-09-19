using System.Diagnostics;
using System.Text;
using ExplorerAlternative.Services;

namespace ExplorerAlternative.Tests.Services;

// 仕様書17章：実際のWindows PowerShellを起動して確認する結合テスト。
// 特にCtrl+C（実行中コマンドの中断）は、コンソールをアタッチして本物のシグナルを送る実装のため、
// モックでは検証できず、実プロセスで確認する。
public sealed class PowerShellTerminalServiceTests : IDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    private readonly object _lock = new();
    private readonly StringBuilder _output = new();
    private readonly PowerShellTerminalService _sut;

    public PowerShellTerminalServiceTests()
    {
        // UIスレッドが無いテスト環境のため、通知は受信スレッドでそのまま実行する。
        _sut = new PowerShellTerminalService("powershell.exe", loadProfile: false, invokeOnUi: action => action());
        _sut.OutputReceived += (_, text) =>
        {
            lock (_lock)
            {
                _output.Append(text);
            }
        };
        _sut.ErrorOccurred += (_, message) =>
        {
            lock (_lock)
            {
                _output.Append("[ERROR]").Append(message);
            }
        };
    }

    public void Dispose() => _sut.Dispose();

    private string Output()
    {
        lock (_lock)
        {
            return _output.ToString();
        }
    }

    private bool WaitFor(Func<string, bool> condition, TimeSpan? timeout = null)
    {
        var deadline = Stopwatch.StartNew();

        while (deadline.Elapsed < (timeout ?? Timeout))
        {
            if (condition(Output()))
            {
                return true;
            }

            Thread.Sleep(50);
        }

        return condition(Output());
    }

    // 改行で終わらないプロンプト（"PS C:\...> "）が、次の行を待たずに届くこと。
    // 行単位で読んでいた旧実装では、これが表示されなかった。
    [Fact]
    public void Start_DeliversPromptThatDoesNotEndWithNewline()
    {
        _sut.Start();

        Assert.True(WaitFor(text => text.TrimEnd(' ').EndsWith('>') && text.Contains("PS ", StringComparison.Ordinal)),
            $"プロンプトが届きませんでした。受信内容: [{Output()}]");
        Assert.True(_sut.IsRunning);
    }

    [Fact]
    public void SendCommand_ExecutesAndReturnsOutput()
    {
        _sut.Start();

        // 結合した結果でのみ現れる文字列を使い、入力のエコーバックと区別する。
        _sut.SendCommand("Write-Output ('RESULT-' + (6 * 7))");

        Assert.True(WaitFor(text => text.Contains("RESULT-42", StringComparison.Ordinal)),
            $"コマンドの結果が届きませんでした。受信内容: [{Output()}]");
    }

    // 仕様書17章「Ctrl+C」：実行中のコマンドを中断でき、その後もシェルが使えること。
    // 中断できなければ、次のコマンドは60秒のスリープが終わるまで実行されないため、
    // 制限時間内に結果が届くかどうかで判定できる。
    [Fact]
    public void Interrupt_StopsRunningCommand_AndShellStaysUsable()
    {
        _sut.Start();
        Assert.True(WaitFor(text => text.Contains("PS ", StringComparison.Ordinal)), "シェルの起動を確認できませんでした。");

        _sut.SendCommand("Start-Sleep -Seconds 60");
        Thread.Sleep(1500); // コマンドが実行中になるまで待つ

        var stopwatch = Stopwatch.StartNew();
        _sut.Interrupt();
        _sut.SendCommand("Write-Output ('AFTER-' + 'INTERRUPT')");

        var reachedInTime = WaitFor(text => text.Contains("AFTER-INTERRUPT", StringComparison.Ordinal), TimeSpan.FromSeconds(20));

        Assert.True(reachedInTime, $"Ctrl+Cで中断できませんでした（{stopwatch.Elapsed.TotalSeconds:F1}秒待機）。受信内容: [{Output()}]");
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(30));
        Assert.True(_sut.IsRunning, "中断後にシェルが終了してしまいました。");
    }

    // 中断を繰り返しても、シェルも呼び出し元のプロセス（このテストプロセス）も落ちないこと。
    [Fact]
    public void Interrupt_CanBeRepeated_WithoutKillingShellOrCaller()
    {
        _sut.Start();
        Assert.True(WaitFor(text => text.Contains("PS ", StringComparison.Ordinal)), "シェルの起動を確認できませんでした。");

        for (var i = 0; i < 3; i++)
        {
            _sut.SendCommand("Start-Sleep -Seconds 60");
            Thread.Sleep(800);
            _sut.Interrupt();
            Thread.Sleep(800);
        }

        _sut.SendCommand("Write-Output ('STILL-' + 'ALIVE')");

        Assert.True(WaitFor(text => text.Contains("STILL-ALIVE", StringComparison.Ordinal), TimeSpan.FromSeconds(20)),
            $"繰り返しの中断後にシェルが応答しませんでした。受信内容: [{Output()}]");
        Assert.True(_sut.IsRunning);
    }

    [Fact]
    public void Interrupt_WhenNotRunning_DoesNothing()
    {
        // 起動していない状態でも例外を投げない。
        _sut.Interrupt();

        Assert.False(_sut.IsRunning);
    }

    [Fact]
    public void SendCommand_WhenNotRunning_ReportsError()
    {
        _sut.SendCommand("dir");

        Assert.Contains("ターミナルが起動していません", Output(), StringComparison.Ordinal);
    }

    [Fact]
    public void Start_WithMissingExecutable_ReportsErrorInsteadOfThrowing()
    {
        using var service = new PowerShellTerminalService(@"C:\definitely\missing\shell.exe", invokeOnUi: action => action());
        var errors = new List<string>();
        service.ErrorOccurred += (_, message) => errors.Add(message);

        service.Start();

        Assert.False(service.IsRunning);
        Assert.Contains(errors, e => e.Contains("PowerShellの起動に失敗しました", StringComparison.Ordinal));
    }

    // 日本語を含むコマンドが文字化けせずシェルへ渡り、結果も正しく戻ること（OEMコードページ対応）。
    // 日本語以外のOEMコードページの環境では、この検証は意味を持たない。
    [Fact]
    public void Japanese_RoundTrip_WorksOnJapaneseOem()
    {
        if (TerminalOutputDecoder.ShellInputEncoding.CodePage != 932)
        {
            return;
        }

        _sut.Start();

        _sut.SendCommand("Write-Output ('あい' + 'うえ')");

        Assert.True(WaitFor(text => text.Contains("あいうえ", StringComparison.Ordinal)),
            $"日本語が正しく往復しませんでした。受信内容: [{Output()}]");
    }
}
