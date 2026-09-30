using ExplorerAlternative.ViewModels;

namespace ExplorerAlternative.Tests.ViewModels;

// 仕様書17章：ターミナルの入力行（プロンプトの後ろに打ちかけているコマンド）の編集ロジック。
public sealed class TerminalInputBufferTests
{
    private static TerminalInputBuffer Create(string text, int caret)
    {
        var buffer = new TerminalInputBuffer();
        buffer.Set(text);
        while (buffer.Caret > caret)
        {
            buffer.MoveLeft();
        }

        return buffer;
    }

    [Fact]
    public void Insert_AppendsAtEnd_AndAdvancesCaret()
    {
        var buffer = new TerminalInputBuffer();

        Assert.True(buffer.Insert("echo"));
        Assert.True(buffer.Insert(" hi"));

        Assert.Equal("echo hi", buffer.Text);
        Assert.Equal(7, buffer.Caret);
    }

    [Fact]
    public void Insert_InsertsAtCaretInMiddle()
    {
        var buffer = Create("ab", caret: 1);

        buffer.Insert("XY");

        Assert.Equal("aXYb", buffer.Text);
        Assert.Equal(3, buffer.Caret);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void Insert_IgnoresEmptyText(string? text)
    {
        var buffer = Create("ab", caret: 1);

        Assert.False(buffer.Insert(text!));

        Assert.Equal("ab", buffer.Text);
        Assert.Equal(1, buffer.Caret);
    }

    [Fact]
    public void Backspace_RemovesCharBeforeCaret()
    {
        var buffer = Create("abc", caret: 2);

        Assert.True(buffer.Backspace());

        Assert.Equal("ac", buffer.Text);
        Assert.Equal(1, buffer.Caret);
    }

    [Fact]
    public void Backspace_AtStart_DoesNothing()
    {
        var buffer = Create("abc", caret: 0);

        Assert.False(buffer.Backspace());

        Assert.Equal("abc", buffer.Text);
        Assert.Equal(0, buffer.Caret);
    }

    [Fact]
    public void DeleteForward_RemovesCharAfterCaret_AndKeepsCaret()
    {
        var buffer = Create("abc", caret: 1);

        Assert.True(buffer.DeleteForward());

        Assert.Equal("ac", buffer.Text);
        Assert.Equal(1, buffer.Caret);
    }

    [Fact]
    public void DeleteForward_AtEnd_DoesNothing()
    {
        var buffer = Create("abc", caret: 3);

        Assert.False(buffer.DeleteForward());

        Assert.Equal("abc", buffer.Text);
    }

    [Fact]
    public void MoveLeftAndRight_StayWithinBounds()
    {
        var buffer = Create("ab", caret: 0);

        Assert.False(buffer.MoveLeft());
        Assert.True(buffer.MoveRight());
        Assert.True(buffer.MoveRight());
        Assert.False(buffer.MoveRight());
        Assert.Equal(2, buffer.Caret);
    }

    [Fact]
    public void MoveToStartAndEnd_ReportWhetherCaretMoved()
    {
        var buffer = Create("abc", caret: 1);

        Assert.True(buffer.MoveToEnd());
        Assert.Equal(3, buffer.Caret);
        Assert.False(buffer.MoveToEnd());

        Assert.True(buffer.MoveToStart());
        Assert.Equal(0, buffer.Caret);
        Assert.False(buffer.MoveToStart());
    }

    [Fact]
    public void Set_ReplacesText_AndPutsCaretAtEnd()
    {
        var buffer = Create("abc", caret: 0);

        buffer.Set("recalled command");

        Assert.Equal("recalled command", buffer.Text);
        Assert.Equal("recalled command".Length, buffer.Caret);
    }

    [Fact]
    public void Take_ReturnsTextAndClearsBuffer()
    {
        var buffer = Create("dir", caret: 3);

        var taken = buffer.Take();

        Assert.Equal("dir", taken);
        Assert.Equal(string.Empty, buffer.Text);
        Assert.Equal(0, buffer.Caret);
    }

    // 日本語入力（全角文字）でもカーソル位置が文字単位でずれないこと。
    [Fact]
    public void Editing_WorksWithJapaneseText()
    {
        var buffer = new TerminalInputBuffer();

        buffer.Insert("こんにちは");
        buffer.MoveLeft(); // 「ち」と「は」の間
        buffer.Backspace(); // 「ち」を削除

        Assert.Equal("こんには", buffer.Text);
        Assert.Equal(3, buffer.Caret);
    }
}
