using ExplorerAlternative.Models;
using ExplorerAlternative.Services.Abstractions;
using ExplorerAlternative.ViewModels;
using ExplorerAlternative.Tests.TestDoubles;

namespace ExplorerAlternative.Tests.ViewModels;

// 仕様書17章：ターミナル1枚分のViewModel（入力行・コマンド履歴・スクロールバック・パスワード自動入力）。
public sealed class TerminalViewModelTests
{
    private static (TerminalViewModel Terminal, FakeTerminalService Service) Create(bool syncByDefault = true)
    {
        var service = new FakeTerminalService();
        return (new TerminalViewModel(service, syncByDefault, "PowerShell 1"), service);
    }

    // ===== 入力行 =====

    [Fact]
    public void InsertInput_UpdatesInputAndRaisesInputChanged()
    {
        var (terminal, _) = Create();
        var raised = 0;
        terminal.InputChanged += () => raised++;

        terminal.InsertInput("echo hi");

        Assert.Equal("echo hi", terminal.Input.Text);
        Assert.Equal(1, raised);
    }

    [Fact]
    public void InsertInput_Empty_DoesNotRaiseInputChanged()
    {
        var (terminal, _) = Create();
        var raised = 0;
        terminal.InputChanged += () => raised++;

        terminal.InsertInput(string.Empty);

        Assert.Equal(0, raised);
    }

    [Fact]
    public void CaretMovesAtBoundaries_DoNotRaiseInputChanged()
    {
        var (terminal, _) = Create();
        var raised = 0;
        terminal.InputChanged += () => raised++;

        terminal.MoveCaretLeft();
        terminal.MoveCaretRight();
        terminal.MoveCaretToStart();
        terminal.MoveCaretToEnd();
        terminal.Backspace();
        terminal.DeleteForward();

        Assert.Equal(0, raised);
    }

    [Fact]
    public void EditingKeys_EditTheInputLine()
    {
        var (terminal, _) = Create();
        terminal.InsertInput("abcd");

        terminal.MoveCaretToStart();
        terminal.DeleteForward();     // "bcd"
        terminal.MoveCaretRight();
        terminal.Backspace();         // "cd"
        terminal.MoveCaretToEnd();
        terminal.InsertInput("!");    // "cd!"

        Assert.Equal("cd!", terminal.Input.Text);
    }

    [Fact]
    public void PasteText_FlattensNewlinesIntoSpaces()
    {
        var (terminal, _) = Create();

        terminal.PasteText("line1\r\nline2\nline3\rline4");

        Assert.Equal("line1 line2 line3 line4", terminal.Input.Text);
    }

    [Fact]
    public void InsertPathIntoInput_QuotesOnlyPathsWithSpaces()
    {
        var (terminal, _) = Create();

        terminal.InsertPathIntoInput(@"C:\Program Files\App");
        terminal.InsertInput(" ");
        terminal.InsertPathIntoInput(@"C:\Temp\a.txt");

        Assert.Equal("\"C:\\Program Files\\App\" C:\\Temp\\a.txt", terminal.Input.Text);
    }

    // 入力行の状態をViewModelが持つため、画面を張り替えても（別タブへ切り替えても）保持される。
    [Fact]
    public void InputSurvivesRebinding_BecauseViewModelOwnsIt()
    {
        var (terminal, _) = Create();

        terminal.InsertInput("git status");

        Assert.Equal("git status", terminal.Input.Text);
        Assert.Equal("git status".Length, terminal.Input.Caret);
    }

    // ===== 確定・中断 =====

    [Fact]
    public void SubmitInput_SendsCommandAndClearsInput()
    {
        var (terminal, service) = Create();
        var raised = 0;
        terminal.InsertInput("dir");
        terminal.InputChanged += () => raised++;

        terminal.SubmitInput();

        Assert.Equal(new[] { "dir" }, service.Commands);
        Assert.Equal(string.Empty, terminal.Input.Text);
        Assert.Equal(1, raised);
    }

    [Fact]
    public void SubmitInput_StartsShellIfNotRunning()
    {
        var (terminal, service) = Create();
        terminal.InsertInput("dir");

        terminal.SubmitInput();

        Assert.Equal(1, service.StartCount);
    }

    [Fact]
    public void InterruptAndClearInput_InterruptsAndDiscardsTypedText()
    {
        var (terminal, service) = Create();
        terminal.InsertInput("half typed");

        terminal.InterruptAndClearInput();

        Assert.Equal(1, service.InterruptCount);
        Assert.Equal(string.Empty, terminal.Input.Text);
    }

    // ===== コマンド履歴 =====

    private static void Submit(TerminalViewModel terminal, string command)
    {
        terminal.InsertInput(command);
        terminal.SubmitInput();
    }

    [Fact]
    public void History_RecallPreviousAndNext_WalksThroughSubmittedCommands()
    {
        var (terminal, _) = Create();
        Submit(terminal, "first");
        Submit(terminal, "second");

        terminal.RecallPreviousCommand();
        Assert.Equal("second", terminal.Input.Text);

        terminal.RecallPreviousCommand();
        Assert.Equal("first", terminal.Input.Text);

        // 最も古い履歴より前へは進まない。
        terminal.RecallPreviousCommand();
        Assert.Equal("first", terminal.Input.Text);

        terminal.RecallNextCommand();
        Assert.Equal("second", terminal.Input.Text);

        // 末尾を超えると入力行が空になる。
        terminal.RecallNextCommand();
        Assert.Equal(string.Empty, terminal.Input.Text);
    }

    [Fact]
    public void History_WithNoCommands_DoesNothing()
    {
        var (terminal, _) = Create();
        var raised = 0;
        terminal.InputChanged += () => raised++;

        terminal.RecallPreviousCommand();
        terminal.RecallNextCommand();

        Assert.Equal(0, raised);
        Assert.Equal(string.Empty, terminal.Input.Text);
    }

    [Fact]
    public void History_RepeatedCommand_MovesToTheEndWithoutDuplicates()
    {
        var (terminal, _) = Create();
        Submit(terminal, "a");
        Submit(terminal, "b");
        Submit(terminal, "a");

        terminal.RecallPreviousCommand();
        Assert.Equal("a", terminal.Input.Text);

        terminal.RecallPreviousCommand();
        Assert.Equal("b", terminal.Input.Text);

        terminal.RecallPreviousCommand();
        Assert.Equal("b", terminal.Input.Text); // "a"は重複せず、これ以上古い履歴は無い
    }

    [Fact]
    public void History_BlankCommand_IsSentButNotRemembered()
    {
        var (terminal, service) = Create();
        Submit(terminal, "real");

        terminal.SubmitInput(); // 空のまま確定（空行を送る）

        Assert.Equal(new[] { "real", string.Empty }, service.Commands);

        terminal.RecallPreviousCommand();
        Assert.Equal("real", terminal.Input.Text);
    }

    [Fact]
    public void History_KeepsOnlyTheMostRecent200Commands()
    {
        var (terminal, _) = Create();
        for (var i = 0; i < 250; i++)
        {
            Submit(terminal, $"cmd{i}");
        }

        for (var i = 0; i < 300; i++)
        {
            terminal.RecallPreviousCommand();
        }

        Assert.Equal("cmd50", terminal.Input.Text); // 古い50件は捨てられ、最古は cmd50
    }

    [Fact]
    public void History_SubmittingARecalledCommand_ResetsPositionAndMovesItToTheEnd()
    {
        var (terminal, service) = Create();
        Submit(terminal, "one");
        Submit(terminal, "two");
        terminal.RecallPreviousCommand();
        terminal.RecallPreviousCommand(); // 入力行は "one"

        terminal.SubmitInput(); // 履歴から呼び出した "one" をそのまま再実行

        Assert.Equal(new[] { "one", "two", "one" }, service.Commands);

        terminal.RecallPreviousCommand();
        Assert.Equal("one", terminal.Input.Text); // 直近に実行したものが最初に出る

        terminal.RecallPreviousCommand();
        Assert.Equal("two", terminal.Input.Text);
    }

    // ===== 出力（スクロールバック） =====

    [Fact]
    public void Output_IsAppendedToBufferAndRaisesSegmentsAppended()
    {
        var (terminal, service) = Create();
        IReadOnlyList<TerminalSegment>? received = null;
        terminal.SegmentsAppended += segments => received = segments;

        service.RaiseOutput("PS C:\\> ");

        Assert.NotNull(received);
        Assert.Equal("PS C:\\> ", string.Concat(received!.Select(s => s.Text)));
        Assert.Equal("PS C:\\> ", string.Concat(terminal.Buffer.Select(s => s.Text)));
    }

    [Fact]
    public void Output_AnsiColorsAreParsedIntoSegments()
    {
        var (terminal, service) = Create();

        service.RaiseOutput("\u001b[31mred\u001b[0m");

        var segment = Assert.Single(terminal.Buffer);
        Assert.Equal("red", segment.Text);
        Assert.NotNull(segment.Foreground);
    }

    [Fact]
    public void Output_WithOnlyEscapeSequences_AddsNothing()
    {
        var (terminal, service) = Create();
        var raised = 0;
        terminal.SegmentsAppended += _ => raised++;

        // カーソルの上移動は無視される（行全体の消去 ESC[2K は、行リセットとして扱うため別のテスト）。
        service.RaiseOutput("\u001b[5A");

        Assert.Empty(terminal.Buffer);
        Assert.Equal(0, raised);
    }

    [Fact]
    public void Error_IsShownInTheTerminalWithMarker()
    {
        var (terminal, service) = Create();

        service.RaiseError("起動に失敗しました");

        var text = string.Concat(terminal.Buffer.Select(s => s.Text));
        Assert.Contains("[エラー] 起動に失敗しました", text);
    }

    // スクロールバックには上限があり、超えた分は古い方から捨てる（メモリ肥大の防止）。
    [Fact]
    public void Buffer_IsTrimmedToTheScrollbackLimit()
    {
        var (terminal, service) = Create();
        var line = new string('x', 999) + "\n";

        for (var i = 0; i < 400; i++)
        {
            service.RaiseOutput(line);
        }

        var total = terminal.Buffer.Sum(s => s.Text.Length);
        Assert.InRange(total, 1, 200_000);
    }

    // ===== 行の上書き（プログレスバー等。仕様書17章） =====

    private static string BufferText(TerminalViewModel terminal) =>
        string.Concat(terminal.Buffer.Select(s => s.Text));

    [Fact]
    public void ProgressBar_OverwritesCurrentLineInBuffer()
    {
        var (terminal, service) = Create();

        service.RaiseOutput("start\n");
        service.RaiseOutput("progress 10%\r");
        service.RaiseOutput("progress 50%\r");
        service.RaiseOutput("progress 100%\n");
        service.RaiseOutput("done\n");

        Assert.Equal("start\nprogress 100%\ndone\n", BufferText(terminal));
    }

    [Fact]
    public void ProgressBar_DoesNotTouchEarlierLines()
    {
        var (terminal, service) = Create();

        service.RaiseOutput("line1\nline2\nspinner |");
        service.RaiseOutput("\rspinner /");

        Assert.Equal("line1\nline2\nspinner /", BufferText(terminal));
    }

    [Fact]
    public void LineReset_AtVeryStart_LeavesBufferEmptyAndDoesNotCrash()
    {
        var (terminal, service) = Create();

        service.RaiseOutput("\rhello");

        Assert.Equal("hello", BufferText(terminal));
    }

    [Fact]
    public void LineReset_ReachesTheViewWithOriginalOrder()
    {
        var (terminal, service) = Create();
        var received = new List<TerminalSegment>();
        terminal.SegmentsAppended += segments => received.AddRange(segments);

        service.RaiseOutput("a\rb");

        Assert.Equal(new[] { false, true, false }, received.Select(s => s.IsLineReset).ToArray());
    }

    [Fact]
    public void LineReset_KeepsSegmentColorOfTheTextBeforeTheLineFeed()
    {
        var (terminal, service) = Create();

        service.RaiseOutput("\u001b[31mred line\nprogress\u001b[0m");
        service.RaiseOutput("\rnext");

        // 改行までは赤のまま残り、その後ろの現在の行だけが置き換わる。
        Assert.Equal("red line\nnext", BufferText(terminal));
        Assert.NotNull(terminal.Buffer[0].Foreground);
    }

    [Fact]
    public void PasswordPrompt_IsStillDetected_AfterALineReset()
    {
        var (terminal, service) = Create();

        terminal.SendRawCommand("ssh user@host", "s3cret");
        service.RaiseOutput("connecting...\r");
        service.RaiseOutput("user@host's password: ");

        Assert.Equal(new[] { "ssh user@host", "s3cret" }, service.Commands);
    }

    // ===== 同期 =====

    [Fact]
    public void SyncCurrentDirectory_ChangesDirectory_OnlyWhenSyncEnabledAndRunning()
    {
        var (terminal, service) = Create(syncByDefault: true);

        terminal.SyncCurrentDirectory(@"C:\a"); // 未起動
        terminal.Start();
        terminal.SyncCurrentDirectory(@"C:\b");
        terminal.IsSyncEnabled = false;
        terminal.SyncCurrentDirectory(@"C:\c"); // 同期OFF

        Assert.Equal(new[] { @"C:\b" }, service.DirectoryChanges);
    }

    [Fact]
    public void ToggleSyncCommand_FlipsSyncState()
    {
        var (terminal, _) = Create(syncByDefault: false);

        terminal.ToggleSyncCommand.Execute(null);

        Assert.True(terminal.IsSyncEnabled);
    }

    [Fact]
    public void Start_DoesNotRestartARunningShell()
    {
        var (terminal, service) = Create();

        terminal.Start();
        terminal.Start();

        Assert.Equal(1, service.StartCount);
    }

    // ===== 仕様書44章：保存済みパスワードの自動入力 =====

    [Fact]
    public void SavedPassword_IsSentOnce_WhenPasswordPromptAppears()
    {
        var (terminal, service) = Create();

        terminal.SendRawCommand("ssh user@host", "s3cret");
        service.RaiseOutput("user@host's password: ");
        service.RaiseOutput("\nPermission denied, please try again.\nuser@host's password: ");

        Assert.Equal(new[] { "ssh user@host", "s3cret" }, service.Commands);
    }

    [Fact]
    public void SavedPassword_IsNotSent_ForPassphrasePrompt()
    {
        var (terminal, service) = Create();

        terminal.SendRawCommand("ssh user@host", "s3cret");
        service.RaiseOutput("Enter passphrase for key 'id_rsa': ");

        Assert.Equal(new[] { "ssh user@host" }, service.Commands);
    }

    [Fact]
    public void SavedPassword_IsDetectedEvenWhenPromptArrivesInPieces()
    {
        var (terminal, service) = Create();

        terminal.SendRawCommand("ssh user@host", "s3cret");
        service.RaiseOutput("user@host's pass");
        service.RaiseOutput("word: ");

        Assert.Equal(new[] { "ssh user@host", "s3cret" }, service.Commands);
    }

    [Fact]
    public void WithoutSavedPassword_NothingIsSentAutomatically()
    {
        var (terminal, service) = Create();

        terminal.SendRawCommand("ssh user@host", password: null);
        service.RaiseOutput("user@host's password: ");

        Assert.Equal(new[] { "ssh user@host" }, service.Commands);
    }
}
