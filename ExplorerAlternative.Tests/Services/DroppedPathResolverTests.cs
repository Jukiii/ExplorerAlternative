using ExplorerAlternative.Services;

namespace ExplorerAlternative.Tests.Services;

// 仕様書19章：ターミナルで選んだ文字列を、Explorerへドロップしたときの、パスの解釈。
public sealed class DroppedPathResolverTests
{
    // 実在するフォルダ・ファイルを、固定の集合で表す（実際のファイルシステムに依存しない）。
    private static readonly HashSet<string> Dirs = new(StringComparer.OrdinalIgnoreCase)
    {
        @"C:\", @"C:\Users", @"C:\Users\Me", @"C:\Users\Me\My Documents", @"C:\Work\sub"
    };

    private static readonly HashSet<string> Files = new(StringComparer.OrdinalIgnoreCase)
    {
        @"C:\Users\Me\notes.txt", @"C:\Work\sub\a.cs"
    };

    private static bool Resolve(string? text, string baseDir, out string path, out bool isDirectory) =>
        DroppedPathResolver.TryResolve(text, baseDir, out path, out isDirectory, Dirs.Contains, Files.Contains);

    [Fact]
    public void AbsoluteFolder_IsResolvedAsDirectory()
    {
        Assert.True(Resolve(@"C:\Users\Me", @"C:\", out var path, out var isDirectory));

        Assert.Equal(@"C:\Users\Me", path);
        Assert.True(isDirectory);
    }

    [Fact]
    public void AbsoluteFile_IsResolvedAsFile()
    {
        Assert.True(Resolve(@"C:\Users\Me\notes.txt", @"C:\", out var path, out var isDirectory));

        Assert.Equal(@"C:\Users\Me\notes.txt", path);
        Assert.False(isDirectory);
    }

    [Fact]
    public void NonexistentPath_IsRejected()
    {
        Assert.False(Resolve(@"C:\Users\Nobody", @"C:\", out var path, out _));

        Assert.Equal(string.Empty, path);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\n\n")]
    public void BlankText_IsRejected(string? text)
    {
        Assert.False(Resolve(text, @"C:\", out _, out _));
    }

    // ターミナルの出力から選ぶと、前後に空白・改行・引用符が付くことがある。
    [Theory]
    [InlineData("  C:\\Users\\Me  ")]
    [InlineData("C:\\Users\\Me\r\n")]
    [InlineData("\"C:\\Users\\Me\"")]
    [InlineData("'C:\\Users\\Me'")]
    [InlineData("  \"  C:\\Users\\Me  \"  ")]
    public void SurroundingWhitespaceAndQuotes_AreIgnored(string text)
    {
        Assert.True(Resolve(text, @"C:\", out var path, out _));

        Assert.Equal(@"C:\Users\Me", path);
    }

    [Fact]
    public void QuotedPathWithSpaces_IsResolved()
    {
        Assert.True(Resolve("\"C:\\Users\\Me\\My Documents\"", @"C:\", out var path, out var isDirectory));

        Assert.Equal(@"C:\Users\Me\My Documents", path);
        Assert.True(isDirectory);
    }

    [Fact]
    public void TrailingSeparator_IsRemoved_ButARootIsKept()
    {
        Assert.True(Resolve(@"C:\Users\Me\", @"C:\", out var trimmed, out _));
        Assert.Equal(@"C:\Users\Me", trimmed);

        Assert.True(Resolve(@"C:\", @"C:\Users", out var root, out var isDirectory));
        Assert.Equal(@"C:\", root);
        Assert.True(isDirectory);
    }

    [Fact]
    public void ForwardSlashes_AreNormalized()
    {
        Assert.True(Resolve("C:/Users/Me", @"C:\", out var path, out _));

        Assert.Equal(@"C:\Users\Me", path);
    }

    [Fact]
    public void OnlyTheFirstNonEmptyLine_IsUsed()
    {
        Assert.True(Resolve("\r\nC:\\Users\\Me\r\nC:\\Work\\sub\r\n", @"C:\", out var path, out _));

        Assert.Equal(@"C:\Users\Me", path);
    }

    // ===== PowerShellのプロンプト =====

    [Fact]
    public void PowerShellPromptPrefix_IsRemoved()
    {
        Assert.True(Resolve(@"PS C:\Users\Me> C:\Work\sub", @"C:\", out var path, out _));

        Assert.Equal(@"C:\Work\sub", path);
    }

    [Fact]
    public void PromptOnly_IsRejected()
    {
        Assert.False(Resolve(@"PS C:\Users\Me> ", @"C:\", out _, out _));
    }

    // ===== 相対パス =====

    [Fact]
    public void RelativeName_IsResolvedAgainstTheBaseDirectory()
    {
        Assert.True(Resolve("Me", @"C:\Users", out var path, out var isDirectory));

        Assert.Equal(@"C:\Users\Me", path);
        Assert.True(isDirectory);
    }

    [Fact]
    public void RelativeFile_IsResolvedAgainstTheBaseDirectory()
    {
        Assert.True(Resolve("notes.txt", @"C:\Users\Me", out var path, out var isDirectory));

        Assert.Equal(@"C:\Users\Me\notes.txt", path);
        Assert.False(isDirectory);
    }

    [Fact]
    public void DotDotRelativePath_IsNormalized()
    {
        Assert.True(Resolve(@"..\notes.txt", @"C:\Users\Me\My Documents", out var path, out _));

        Assert.Equal(@"C:\Users\Me\notes.txt", path);
    }

    [Fact]
    public void RelativePath_WithoutABaseDirectory_IsRejected()
    {
        Assert.False(Resolve("Me", string.Empty, out _, out _));
    }

    // ===== 環境変数 =====

    [Fact]
    public void EnvironmentVariables_AreExpanded()
    {
        Environment.SetEnvironmentVariable("EAT_TEST_DIR", @"C:\Users\Me");

        Assert.True(Resolve("%EAT_TEST_DIR%", @"C:\", out var path, out _));

        Assert.Equal(@"C:\Users\Me", path);
    }

    // ===== 異常な入力でも例外を出さない（27章） =====

    [Theory]
    [InlineData("a<b>c")]
    [InlineData("C:\\foo|bar")]
    [InlineData("C:\\foo\0bar")]
    public void InvalidPathCharacters_AreRejectedWithoutThrowing(string text)
    {
        Assert.False(Resolve(text, @"C:\", out _, out _));
    }

    [Fact]
    public void VeryLongText_IsRejectedWithoutThrowing()
    {
        var text = @"C:\" + new string('x', 5000);

        Assert.False(Resolve(text, @"C:\", out _, out _));
    }

    [Fact]
    public void ArbitraryTerminalOutput_IsRejected()
    {
        Assert.False(Resolve("Hello, world! This is not a path.", @"C:\Users", out _, out _));
    }

    // ===== 実際のファイルシステム（既定の存在確認） =====

    [Fact]
    public void DefaultExistenceChecks_UseTheRealFileSystem()
    {
        var root = Directory.CreateTempSubdirectory("eat_drop_").FullName;
        try
        {
            var file = Path.Combine(root, "real.txt");
            File.WriteAllText(file, "x");

            Assert.True(DroppedPathResolver.TryResolve(root, string.Empty, out var dirPath, out var dirIsDirectory));
            Assert.Equal(root, dirPath);
            Assert.True(dirIsDirectory);

            Assert.True(DroppedPathResolver.TryResolve("real.txt", root, out var filePath, out var fileIsDirectory));
            Assert.Equal(file, filePath);
            Assert.False(fileIsDirectory);

            Assert.False(DroppedPathResolver.TryResolve(Path.Combine(root, "missing"), string.Empty, out _, out _));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
