using ExplorerAlternative.Tests.TestDoubles;
using ExplorerAlternative.ViewModels;

namespace ExplorerAlternative.Tests.ViewModels;

// 仕様書21章「一括名前変更」：入力を変えるたびに、新しい名前のプレビューが再計算される。
public sealed class BulkRenameViewModelTests : IDisposable
{
    private readonly PaneTestHost _host = new();

    public void Dispose() => _host.Dispose();

    private BulkRenameViewModel Create(params string[] names)
    {
        foreach (var name in names)
        {
            _host.CreateFile(name);
        }

        _host.Pane.RefreshCommand.Execute(null);
        var nodes = names.Select(n => _host.Pane.VisibleNodes.Single(x => x.Name == n)).ToList();
        return new BulkRenameViewModel(nodes);
    }

    private static string[] New(BulkRenameViewModel vm) => vm.PreviewItems.Select(p => p.NewName).ToArray();

    [Fact]
    public void Initial_UsesTheDefaultPattern_WithSequentialNumbers()
    {
        var vm = Create("a.txt", "b.txt");

        Assert.Equal(new[] { "a_1.txt", "b_2.txt" }, New(vm));
        Assert.Equal(new[] { "a.txt", "b.txt" }, vm.PreviewItems.Select(p => p.OriginalName));
    }

    [Fact]
    public void ChangingThePattern_RebuildsThePreview()
    {
        var vm = Create("a.txt", "b.txt");

        vm.Pattern = "photo_{n:00}.{ext}";

        Assert.Equal(new[] { "photo_01.txt", "photo_02.txt" }, New(vm));
    }

    [Fact]
    public void FindReplace_Mode_ReplacesText()
    {
        var vm = Create("report_old.txt", "memo_old.txt");

        vm.Mode = BulkRenameMode.FindReplace;
        vm.FindText = "old";
        vm.ReplaceText = "new";

        Assert.Equal(new[] { "report_new.txt", "memo_new.txt" }, New(vm));
    }

    [Fact]
    public void FindReplace_CaseSensitivityToggle_ChangesTheResult()
    {
        var vm = Create("Abc.txt");
        vm.Mode = BulkRenameMode.FindReplace;
        vm.FindText = "abc";
        vm.ReplaceText = "X";

        Assert.Equal("X.txt", New(vm)[0]); // 既定は、大文字小文字を区別しない

        vm.CaseSensitive = true;

        Assert.Equal("Abc.txt", New(vm)[0]);
    }

    [Fact]
    public void FindReplace_Regex_AndAnInvalidRegex_DoesNotCrash()
    {
        var vm = Create("a1.txt", "b22.txt");
        vm.Mode = BulkRenameMode.FindReplace;
        vm.UseRegex = true;
        vm.FindText = @"\d+";
        vm.ReplaceText = "#";

        Assert.Equal(new[] { "a#.txt", "b#.txt" }, New(vm));

        vm.FindText = "(unclosed";

        Assert.Equal(2, vm.PreviewItems.Count); // 不正な正規表現でも、画面が落ちない
    }

    [Fact]
    public void FindReplace_EmptyFind_LeavesNamesUnchanged()
    {
        var vm = Create("a.txt");
        vm.Mode = BulkRenameMode.FindReplace;
        vm.ReplaceText = "zzz";

        Assert.Equal(new[] { "a.txt" }, New(vm));
    }

    [Fact]
    public void CaseConversion_AppliesAfterThePattern()
    {
        var vm = Create("Mixed.TXT");
        vm.Pattern = "{name}.{ext}";

        vm.CaseConversion = CaseConversionMode.UpperCase;
        Assert.Equal("MIXED.TXT", New(vm)[0]);

        vm.CaseConversion = CaseConversionMode.LowerCase;
        Assert.Equal("mixed.txt", New(vm)[0]);
    }

    [Fact]
    public void WidthConversion_FullToHalf_AndBack()
    {
        var vm = Create("ＡＢＣ１２３.txt");
        vm.Pattern = "{name}.{ext}";

        vm.WidthConversion = WidthConversionMode.ToHalfWidth;
        Assert.Equal("ABC123.txt", New(vm)[0]);

        vm.WidthConversion = WidthConversionMode.None;
        Assert.Equal("ＡＢＣ１２３.txt", New(vm)[0]);
    }

    [Fact]
    public void Commands_SwitchTheModes()
    {
        var vm = Create("a.txt");

        vm.SetModeCommand.Execute(BulkRenameMode.FindReplace);
        vm.SetCaseConversionCommand.Execute(CaseConversionMode.UpperCase);
        vm.SetWidthConversionCommand.Execute(WidthConversionMode.ToFullWidth);

        Assert.Equal(BulkRenameMode.FindReplace, vm.Mode);
        Assert.Equal(CaseConversionMode.UpperCase, vm.CaseConversion);
        Assert.Equal(WidthConversionMode.ToFullWidth, vm.WidthConversion);
    }

    [Fact]
    public void CreatedAndModifiedTokens_UseEachFilesDates()
    {
        var path = _host.CreateFile("dated.txt");
        File.SetCreationTime(path, new DateTime(2020, 1, 2));
        File.SetLastWriteTime(path, new DateTime(2021, 3, 4));
        _host.Pane.RefreshCommand.Execute(null);
        var vm = new BulkRenameViewModel(new[] { _host.Pane.VisibleNodes.Single(n => n.Name == "dated.txt") });

        vm.Pattern = "{created:yyyyMMdd}_{modified:yyyyMMdd}.{ext}";

        Assert.Equal("20200102_20210304.txt", New(vm)[0]);
    }

    [Fact]
    public void ChangingAnInput_RaisesPropertyChanged_AndKeepsTheItemCount()
    {
        var vm = Create("a.txt", "b.txt", "c.txt");
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.Pattern = "x{n}.{ext}";
        vm.Pattern = "x{n}.{ext}"; // 同じ値は、通知しない

        Assert.Equal(new[] { nameof(vm.Pattern) }, raised);
        Assert.Equal(3, vm.PreviewItems.Count);
    }
}
