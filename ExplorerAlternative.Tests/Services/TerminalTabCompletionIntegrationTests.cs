using System.Diagnostics;
using ExplorerAlternative.Services;
using ExplorerAlternative.ViewModels;

namespace ExplorerAlternative.Tests.Services;

// 仕様書17章「Tab補完」：実際のWindows PowerShellのターミナルで、cdで移動したフォルダを追跡し、
// そのフォルダの中のファイル名をTabで補完できることを確認する結合テスト。
public sealed class TerminalTabCompletionIntegrationTests : IDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    private readonly string _root = Directory.CreateTempSubdirectory("eat_tabint_").FullName;
    private readonly PowerShellTerminalService _shell;
    private readonly PowerShellTabCompletionService _completion = new("powershell.exe");
    private readonly TerminalViewModel _terminal;

    public TerminalTabCompletionIntegrationTests()
    {
        _shell = new PowerShellTerminalService("powershell.exe", loadProfile: false, invokeOnUi: action => action());
        _terminal = new TerminalViewModel(_shell, syncByDefault: false, "PowerShell 1", _completion, initialDirectory: null);
    }

    public void Dispose()
    {
        _shell.Dispose();
        _completion.Dispose();

        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private bool WaitUntil(Func<bool> condition)
    {
        var stopwatch = Stopwatch.StartNew();

        while (stopwatch.Elapsed < Timeout)
        {
            if (condition())
            {
                return true;
            }

            Thread.Sleep(50);
        }

        return condition();
    }

    [Fact]
    public async Task AfterCd_TabCompletesAFileOfTheNewFolder()
    {
        var sub = Directory.CreateDirectory(Path.Combine(_root, "work folder")).FullName;
        File.WriteAllText(Path.Combine(sub, "unique-report-file.txt"), "x");

        _terminal.Start();
        _terminal.SendInput($"Set-Location -LiteralPath '{sub}'");
        Assert.True(WaitUntil(() => string.Equals(_terminal.CurrentDirectory, sub, StringComparison.OrdinalIgnoreCase)),
            $"プロンプトから現在のフォルダを読み取れませんでした。({_terminal.CurrentDirectory})");

        _terminal.InsertInput("notepad unique-rep");
        await _terminal.CompleteTabAsync();

        Assert.Equal("notepad .\\unique-report-file.txt", _terminal.Input.Text);
    }
}
