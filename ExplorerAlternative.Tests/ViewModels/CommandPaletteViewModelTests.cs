using ExplorerAlternative.ViewModels;

namespace ExplorerAlternative.Tests.ViewModels;

// 仕様書46章「コマンドパレット」。
public sealed class CommandPaletteViewModelTests
{
    private static CommandPaletteEntry Entry(string name, bool canExecute = true, Action? execute = null)
    {
        return new CommandPaletteEntry
        {
            Name = name,
            Execute = execute ?? (() => { }),
            CanExecute = () => canExecute
        };
    }

    [Fact]
    public void InitialFilteredCommands_ContainsOnlyExecutableEntries()
    {
        var entries = new List<CommandPaletteEntry>
        {
            Entry("開く", canExecute: true),
            Entry("無効なコマンド", canExecute: false)
        };

        var sut = new CommandPaletteViewModel(entries);

        Assert.Single(sut.FilteredCommands);
        Assert.Equal("開く", sut.FilteredCommands[0].Name);
    }

    [Fact]
    public void Query_FiltersEntriesBySubstring_CaseInsensitive()
    {
        var entries = new List<CommandPaletteEntry>
        {
            Entry("設定を開く"),
            Entry("検索を開く"),
            Entry("Settings")
        };
        var sut = new CommandPaletteViewModel(entries);

        sut.Query = "settings";

        Assert.Single(sut.FilteredCommands);
        Assert.Equal("Settings", sut.FilteredCommands[0].Name);
    }

    [Fact]
    public void Query_AutoSelectsFirstMatch()
    {
        var entries = new List<CommandPaletteEntry> { Entry("Alpha"), Entry("Beta") };
        var sut = new CommandPaletteViewModel(entries);

        sut.Query = "Beta";

        Assert.Same(sut.FilteredCommands[0], sut.SelectedCommand);
    }

    [Fact]
    public void ExecuteEntry_InvokesExecuteAndRaisesRequestClose()
    {
        var executed = false;
        var closeRaised = false;
        var entry = Entry("Do It", execute: () => executed = true);
        var sut = new CommandPaletteViewModel(new List<CommandPaletteEntry> { entry });
        sut.RequestClose += () => closeRaised = true;

        sut.ExecuteEntry(entry);

        Assert.True(executed);
        Assert.True(closeRaised);
    }

    [Fact]
    public void ExecuteEntry_ClosesBeforeExecuting()
    {
        // Phase 8で見つけた不具合の再発防止：ShowDialog()等でブロックするコマンドの場合、
        // 先にパレットを閉じてから実行しないと、パレットが裏に残ってしまう。
        var order = new List<string>();
        var entry = Entry("Do It", execute: () => order.Add("execute"));
        var sut = new CommandPaletteViewModel(new List<CommandPaletteEntry> { entry });
        sut.RequestClose += () => order.Add("close");

        sut.ExecuteEntry(entry);

        Assert.Equal(new[] { "close", "execute" }, order);
    }

    [Fact]
    public void EmptyQuery_RestoresFullList()
    {
        var entries = new List<CommandPaletteEntry> { Entry("Alpha"), Entry("Beta") };
        var sut = new CommandPaletteViewModel(entries);

        sut.Query = "Alpha";
        sut.Query = "";

        Assert.Equal(2, sut.FilteredCommands.Count);
    }
}
