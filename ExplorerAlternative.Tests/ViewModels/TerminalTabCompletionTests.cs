using ExplorerAlternative.Services.Abstractions;
using ExplorerAlternative.Tests.TestDoubles;
using ExplorerAlternative.ViewModels;

namespace ExplorerAlternative.Tests.ViewModels;

// 仕様書17章「Tab補完」：Tabで候補を入力行へ入れる動き（1件・複数件の巡回・Shift+Tab・古い結果の破棄）と、
// 補完の基準にするフォルダの追跡。補完の候補そのものは、偽のサービスで与える。
public sealed class TerminalTabCompletionTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("eat_tabvm_").FullName;
    private readonly FakeTerminalService _service = new();
    private readonly FakeCompletion _completion = new();
    private readonly TerminalViewModel _terminal;

    public TerminalTabCompletionTests()
    {
        _terminal = new TerminalViewModel(_service, syncByDefault: false, "PowerShell 1", _completion, initialDirectory: _dir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private void Type(string text) => _terminal.InsertInput(text);

    // ===== 1件 =====

    [Fact]
    public async Task SingleMatch_ReplacesTheWord_AndPutsTheCaretAfterIt()
    {
        Type("Get-ChildIt");
        _completion.Result = new TabCompletionResult(0, 11, new[] { "Get-ChildItem" });

        await _terminal.CompleteTabAsync();

        Assert.Equal("Get-ChildItem", _terminal.Input.Text);
        Assert.Equal(13, _terminal.Input.Caret);
    }

    [Fact]
    public async Task ReplacesOnlyTheWordAtTheCaret_KeepingTheRest()
    {
        Type("cd doc -Force");
        _terminal.MoveCaretToEnd();
        for (var i = 0; i < " -Force".Length; i++)
        {
            _terminal.MoveCaretLeft();
        }

        _completion.Result = new TabCompletionResult(3, 3, new[] { "documents" });

        await _terminal.CompleteTabAsync();

        Assert.Equal("cd documents -Force", _terminal.Input.Text);
        Assert.Equal("cd documents".Length, _terminal.Input.Caret);
    }

    [Fact]
    public async Task TheServiceIsGivenTheInput_TheCaret_AndTheCurrentFolder()
    {
        Type("cd x");
        _completion.Result = TabCompletionResult.None;

        await _terminal.CompleteTabAsync();

        var call = Assert.Single(_completion.Calls);
        Assert.Equal(("cd x", 4, _dir), call);
    }

    [Fact]
    public async Task NoMatches_LeavesTheInputAlone()
    {
        Type("zzz");
        _completion.Result = TabCompletionResult.None;

        await _terminal.CompleteTabAsync();

        Assert.Equal("zzz", _terminal.Input.Text);
    }

    [Fact]
    public async Task ServiceReturningNull_LeavesTheInputAlone()
    {
        Type("zzz");
        _completion.Result = null;

        await _terminal.CompleteTabAsync();

        Assert.Equal("zzz", _terminal.Input.Text);
    }

    [Fact]
    public async Task RaisesInputChanged_SoTheViewRedraws()
    {
        Type("Get-ChildIt");
        _completion.Result = new TabCompletionResult(0, 11, new[] { "Get-ChildItem" });
        var raised = 0;
        _terminal.InputChanged += () => raised++;

        await _terminal.CompleteTabAsync();

        Assert.Equal(1, raised);
    }

    [Fact]
    public async Task OutOfRangeReplacement_IsClamped_NotACrash()
    {
        Type("ab");
        _completion.Result = new TabCompletionResult(1, 50, new[] { "bXYZ" });

        await _terminal.CompleteTabAsync();

        Assert.Equal("abXYZ", _terminal.Input.Text);
    }

    // ===== 複数件：押すたびに次の候補 =====

    private async Task<string[]> PressTabTimes(int times, bool backwards = false)
    {
        var seen = new List<string>();
        for (var i = 0; i < times; i++)
        {
            await _terminal.CompleteTabAsync(backwards);
            seen.Add(_terminal.Input.Text);
        }

        return seen.ToArray();
    }

    [Fact]
    public async Task SeveralMatches_CycleOnEachTab_AndWrapAround()
    {
        Type("Get-Ch");
        _completion.Result = new TabCompletionResult(0, 6, new[] { "Get-ChildItem", "Get-Choice", "Get-Charm" });

        var seen = await PressTabTimes(4);

        Assert.Equal(new[] { "Get-ChildItem", "Get-Choice", "Get-Charm", "Get-ChildItem" }, seen);
        Assert.Single(_completion.Calls); // 巡回中は、問い合わせ直さない
    }

    [Fact]
    public async Task ShiftTab_StartsFromTheLast_AndGoesBackwards()
    {
        Type("Get-Ch");
        _completion.Result = new TabCompletionResult(0, 6, new[] { "A", "B", "C" });

        var seen = await PressTabTimes(4, backwards: true);

        Assert.Equal(new[] { "C", "B", "A", "C" }, seen);
    }

    [Fact]
    public async Task ShiftTab_AfterTab_GoesBackToThePreviousCandidate()
    {
        Type("x");
        _completion.Result = new TabCompletionResult(0, 1, new[] { "A", "B", "C" });

        await _terminal.CompleteTabAsync();
        await _terminal.CompleteTabAsync();
        await _terminal.CompleteTabAsync(backwards: true);

        Assert.Equal("A", _terminal.Input.Text);
    }

    [Fact]
    public async Task TypingAfterACompletion_StartsAFreshCompletion()
    {
        Type("Get-Ch");
        _completion.Result = new TabCompletionResult(0, 6, new[] { "Get-ChildItem", "Get-Choice" });
        await _terminal.CompleteTabAsync();

        Type(" -F");
        _completion.Result = new TabCompletionResult(14, 2, new[] { "-Force" });
        await _terminal.CompleteTabAsync();

        Assert.Equal(2, _completion.Calls.Count);
        Assert.Equal("Get-ChildItem -Force", _terminal.Input.Text);
    }

    [Fact]
    public async Task MovingTheCaretAfterACompletion_StartsAFreshCompletion()
    {
        Type("ab");
        _completion.Result = new TabCompletionResult(0, 2, new[] { "abc", "abd" });
        await _terminal.CompleteTabAsync();

        _terminal.MoveCaretLeft();
        await _terminal.CompleteTabAsync();

        Assert.Equal(2, _completion.Calls.Count);
    }

    [Fact]
    public async Task RecallingHistory_AfterACompletion_DoesNotContinueTheOldCycle()
    {
        _terminal.SendInput("old command");
        Type("x");
        _completion.Result = new TabCompletionResult(0, 1, new[] { "xa", "xb" });
        await _terminal.CompleteTabAsync();

        _terminal.RecallPreviousCommand();
        _completion.Result = TabCompletionResult.None;
        await _terminal.CompleteTabAsync();

        Assert.Equal(2, _completion.Calls.Count);
        Assert.Equal("old command", _terminal.Input.Text);
    }

    // ===== 待っている間の変化 =====

    [Fact]
    public async Task ResultThatArrivesAfterTheInputChanged_IsDiscarded()
    {
        Type("Get-ChildIt");
        _completion.Gate = new TaskCompletionSource();
        _completion.Result = new TabCompletionResult(0, 11, new[] { "Get-ChildItem" });

        var pending = _terminal.CompleteTabAsync();
        Type("e"); // 待っている間に、入力を続けた
        _completion.Gate.SetResult();
        await pending;

        Assert.Equal("Get-ChildIte", _terminal.Input.Text);
    }

    [Fact]
    public async Task ASecondTabWhileWaiting_OnlyTheNewestRequestApplies()
    {
        Type("Get-Ch");
        _completion.Gate = new TaskCompletionSource();
        _completion.Result = new TabCompletionResult(0, 6, new[] { "Get-ChildItem", "Get-Choice" });

        var first = _terminal.CompleteTabAsync();
        var second = _terminal.CompleteTabAsync();
        _completion.Gate.SetResult();
        await Task.WhenAll(first, second);

        // 同じ入力に対する2つの結果のうち、後の1つだけが入力へ反映される（1回目の結果は捨てられる）。
        Assert.Equal("Get-ChildItem", _terminal.Input.Text);
    }

    // ===== 失敗 =====

    [Fact]
    public async Task ServiceFailure_ShowsAJapaneseMessage_AndDoesNotThrow()
    {
        Type("x");
        _completion.Failure = new InvalidOperationException("boom");

        await _terminal.CompleteTabAsync();

        var text = string.Concat(_terminal.Buffer.Select(s => s.Text));
        Assert.Contains("Tab補完に失敗しました", text);
        Assert.Equal("x", _terminal.Input.Text);
    }

    [Fact]
    public async Task WithoutAService_TabDoesNothing()
    {
        var terminal = new TerminalViewModel(_service, syncByDefault: false, "PowerShell 2");
        terminal.InsertInput("abc");

        await terminal.CompleteTabAsync();

        Assert.Equal("abc", terminal.Input.Text);
    }

    // ===== 補完の基準にするフォルダ =====

    [Fact]
    public void CurrentDirectory_StartsAsTheInitialDirectory()
    {
        Assert.Equal(_dir, _terminal.CurrentDirectory);
    }

    [Fact]
    public void CurrentDirectory_FollowsThePrompt_AfterCd()
    {
        var other = Directory.CreateDirectory(Path.Combine(_dir, "other")).FullName;

        _service.RaiseOutput($"PS {other}> ");

        Assert.Equal(other, _terminal.CurrentDirectory);
    }

    [Fact]
    public void CurrentDirectory_FollowsAPromptSplitAcrossChunks()
    {
        var other = Directory.CreateDirectory(Path.Combine(_dir, "split")).FullName;

        _service.RaiseOutput($"output line\r\nPS {other}");
        _service.RaiseOutput("> ");

        Assert.Equal(other, _terminal.CurrentDirectory);
    }

    [Fact]
    public void CurrentDirectory_IgnoresPromptsOfNonFolders_AndOrdinaryOutput()
    {
        _service.RaiseOutput("PS HKLM:\\> ");
        _service.RaiseOutput("\r\nPS C:\\this\\folder\\does\\not\\exist> ");
        _service.RaiseOutput("\r\nsome PS output > ");

        Assert.Equal(_dir, _terminal.CurrentDirectory);
    }

    [Fact]
    public void CurrentDirectory_UsesTheLastPromptOfAChunk()
    {
        var first = Directory.CreateDirectory(Path.Combine(_dir, "first")).FullName;
        var second = Directory.CreateDirectory(Path.Combine(_dir, "second")).FullName;

        _service.RaiseOutput($"PS {first}> cd ..\r\nPS {second}> ");

        Assert.Equal(second, _terminal.CurrentDirectory);
    }

    [Fact]
    public void SyncedDirectoryChange_UpdatesTheCurrentDirectory()
    {
        var synced = new TerminalViewModel(_service, syncByDefault: true, "PowerShell 3", _completion);
        _service.IsRunning = true;
        var target = Directory.CreateDirectory(Path.Combine(_dir, "synced")).FullName;

        synced.SyncCurrentDirectory(target);

        Assert.Equal(target, synced.CurrentDirectory);
    }

    // ===== 入力行 =====

    [Fact]
    public void InputBuffer_SetWithCaret_ClampsTheCaret()
    {
        var buffer = new TerminalInputBuffer();

        buffer.Set("abc", 99);
        Assert.Equal(3, buffer.Caret);

        buffer.Set("abc", -4);
        Assert.Equal(0, buffer.Caret);

        buffer.Set("abc", 1);
        Assert.Equal(1, buffer.Caret);
    }

    private sealed class FakeCompletion : ITabCompletionService
    {
        public TabCompletionResult? Result { get; set; } = TabCompletionResult.None;

        public Exception? Failure { get; set; }

        /// <summary>設定すると、これが完了するまで、結果を返さない。</summary>
        public TaskCompletionSource? Gate { get; set; }

        public List<(string Input, int Caret, string? Directory)> Calls { get; } = new();

        public async Task<TabCompletionResult?> CompleteAsync(string input, int caretIndex, string? workingDirectory, CancellationToken cancellationToken)
        {
            Calls.Add((input, caretIndex, workingDirectory));

            if (Failure is not null)
            {
                throw Failure;
            }

            var gate = Gate;
            if (gate is not null)
            {
                await gate.Task;
            }

            return Result;
        }

        public void Dispose()
        {
        }
    }
}
