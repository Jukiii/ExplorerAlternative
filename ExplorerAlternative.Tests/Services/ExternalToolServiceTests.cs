using ExplorerAlternative.Services;

namespace ExplorerAlternative.Tests.Services;

// 仕様書65章「任意EXE起動時の引数を適切にエスケープ」の検証。
// Phase 10で見つけた「空白を含むパスが引数分割されてしまう」バグの再発防止。
public sealed class ExternalToolServiceTests
{
    [Fact]
    public void BuildArguments_QuotesPathWithSpaces_WhenTemplateHasNoQuotes()
    {
        var result = ExternalToolService.BuildArguments("{path}", @"C:\My Files\a.txt");

        Assert.Equal("\"C:\\My Files\\a.txt\"", result);
    }

    [Fact]
    public void BuildArguments_DoesNotDoubleQuote_WhenTemplateAlreadyQuotesPlaceholder()
    {
        var result = ExternalToolService.BuildArguments("\"{path}\"", @"C:\My Files\a.txt");

        Assert.Equal("\"C:\\My Files\\a.txt\"", result);
    }

    [Fact]
    public void BuildArguments_DoesNotQuote_WhenPathHasNoSpaces()
    {
        var result = ExternalToolService.BuildArguments("{path}", @"C:\Files\a.txt");

        Assert.Equal(@"C:\Files\a.txt", result);
    }

    [Fact]
    public void BuildArguments_PreservesSurroundingFlags()
    {
        var result = ExternalToolService.BuildArguments("-i {path} -v", @"C:\My Files\a.txt");

        Assert.Equal("-i \"C:\\My Files\\a.txt\" -v", result);
    }

    [Fact]
    public void BuildArguments_ReturnsTemplateUnchanged_WhenPlaceholderMissing()
    {
        var result = ExternalToolService.BuildArguments("--version", @"C:\My Files\a.txt");

        Assert.Equal("--version", result);
    }
}
