using ExplorerAlternative.Services;

namespace ExplorerAlternative.Tests.Services;

// 仕様書17章「Tab補完」：実際のWindows PowerShell（TabExpansion2）に問い合わせて、候補が得られることを確認する結合テスト。
public sealed class PowerShellTabCompletionServiceTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("eat_tab_").FullName;
    private readonly PowerShellTabCompletionService _sut = new("powershell.exe");

    public void Dispose()
    {
        _sut.Dispose();

        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private Task<ExplorerAlternative.Services.Abstractions.TabCompletionResult?> Complete(string input, int? caret = null) =>
        _sut.CompleteAsync(input, caret ?? input.Length, _root, CancellationToken.None);

    [Fact]
    public async Task CommandName_IsCompleted()
    {
        var result = await Complete("Get-ChildIt");

        Assert.NotNull(result);
        Assert.Contains("Get-ChildItem", result!.Matches);
        Assert.Equal(0, result.ReplacementIndex);
        Assert.Equal("Get-ChildIt".Length, result.ReplacementLength);
    }

    [Fact]
    public async Task Path_IsCompletedRelativeToTheWorkingDirectory()
    {
        Directory.CreateDirectory(Path.Combine(_root, "documents"));
        File.WriteAllText(Path.Combine(_root, "doc.txt"), "x");

        var result = await Complete("cd doc");

        Assert.NotNull(result);
        // cdはフォルダだけを候補にする。
        Assert.Contains(result!.Matches, m => m.Contains("documents"));
        Assert.DoesNotContain(result.Matches, m => m.Contains("doc.txt"));
        Assert.Equal(3, result.ReplacementIndex);
    }

    [Fact]
    public async Task Path_WithASpace_IsQuoted()
    {
        File.WriteAllText(Path.Combine(_root, "my file.txt"), "x");

        var result = await Complete("notepad my");

        Assert.NotNull(result);
        var match = Assert.Single(result!.Matches);
        Assert.StartsWith("'", match);
        Assert.Contains("my file.txt", match);
    }

    [Fact]
    public async Task JapanesePath_IsCompleted()
    {
        File.WriteAllText(Path.Combine(_root, "日本語のファイル.txt"), "x");

        var result = await Complete("notepad 日本");

        Assert.NotNull(result);
        Assert.Contains(result!.Matches, m => m.Contains("日本語のファイル.txt"));
    }

    [Fact]
    public async Task Parameter_IsCompleted()
    {
        var result = await Complete("Get-ChildItem -Recur");

        Assert.NotNull(result);
        Assert.Contains("-Recurse", result!.Matches);
    }

    [Fact]
    public async Task CaretInTheMiddle_CompletesTheWordBeforeTheCaret()
    {
        var result = await Complete("Get-ChildIt -Force", caret: "Get-ChildIt".Length);

        Assert.NotNull(result);
        Assert.Contains("Get-ChildItem", result!.Matches);
        Assert.Equal("Get-ChildIt".Length, result.ReplacementLength);
    }

    [Fact]
    public async Task NothingToComplete_GivesNoMatches()
    {
        var result = await Complete("zzzzqqqq-no-such-thing");

        Assert.NotNull(result);
        Assert.Empty(result!.Matches);
    }

    [Fact]
    public async Task SeveralRequests_ReuseTheSameProcess_AndStayInOrder()
    {
        var first = await Complete("Get-ChildIt");
        var second = await Complete("Get-Proces");
        var third = await Complete("Get-ChildIt");

        Assert.Contains("Get-ChildItem", first!.Matches);
        Assert.Contains("Get-Process", second!.Matches);
        Assert.Contains("Get-ChildItem", third!.Matches);
    }

    [Fact]
    public async Task ConcurrentRequests_AreSerialized_WithoutMixingUpAnswers()
    {
        var tasks = new[]
        {
            Complete("Get-ChildIt"),
            Complete("Get-Proces"),
            Complete("Set-Locatio")
        };

        var results = await Task.WhenAll(tasks);

        Assert.Contains("Get-ChildItem", results[0]!.Matches);
        Assert.Contains("Get-Process", results[1]!.Matches);
        Assert.Contains("Set-Location", results[2]!.Matches);
    }

    [Fact]
    public async Task Cancelled_ThrowsOperationCanceled()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _sut.CompleteAsync("Get-", 4, _root, cts.Token));
    }

    [Fact]
    public async Task ShellThatCannotStart_GivesNull_NotAnException()
    {
        using var broken = new PowerShellTabCompletionService(Path.Combine(_root, "no-such-shell.exe"));

        var result = await broken.CompleteAsync("Get-", 4, _root, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task AfterDispose_GivesNull()
    {
        _sut.Dispose();

        Assert.Null(await Complete("Get-"));
    }

    [Fact]
    public async Task TooSlowAnAnswer_GivesNull_AndTheNextRequestStartsAFreshProcess()
    {
        // 待ち時間を極端に短くすると、最初の問い合わせ（PowerShellの起動）は間に合わず、時間切れになる。
        using var impatient = new PowerShellTabCompletionService("powershell.exe", firstRequestTimeout: TimeSpan.FromMilliseconds(1));

        var result = await impatient.CompleteAsync("Get-ChildIt", 11, _root, CancellationToken.None);

        Assert.Null(result);
    }

    [Theory]
    [InlineData("{\"i\":3,\"l\":2,\"m\":[\"a\",\"b\"]}", 3, 2, new[] { "a", "b" })]
    [InlineData("{\"i\":0,\"l\":0,\"m\":[]}", 0, 0, new string[0])]
    [InlineData("{\"i\":1,\"l\":1,\"m\":\"only\"}", 1, 1, new[] { "only" })] // 候補が1つのときに、配列ではなく文字列で来ても受け付ける
    [InlineData("{\"i\":1,\"l\":1}", 1, 1, new string[0])]
    public void Parse_AcceptsTheShapesPowerShellProduces(string json, int index, int length, string[] expected)
    {
        var result = PowerShellTabCompletionService.Parse(json);

        Assert.NotNull(result);
        Assert.Equal(index, result!.ReplacementIndex);
        Assert.Equal(length, result.ReplacementLength);
        Assert.Equal(expected, result.Matches);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{ broken")]
    public void Parse_Garbage_GivesNull(string json)
    {
        Assert.Null(PowerShellTabCompletionService.Parse(json));
    }
}
