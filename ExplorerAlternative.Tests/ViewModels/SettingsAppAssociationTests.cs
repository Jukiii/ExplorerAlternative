using ExplorerAlternative.Models;
using ExplorerAlternative.Services.Abstractions;
using ExplorerAlternative.Tests.TestDoubles;
using ExplorerAlternative.ViewModels;

namespace ExplorerAlternative.Tests.ViewModels;

// 仕様書34章：設定画面の「関連付け」（追加・削除・保存）。
public sealed class SettingsAppAssociationTests
{
    private readonly FakeSettingsService _settings = new();
    private readonly IDialogService _dialog = StubProxy.Create<IDialogService>();

    private StubProxy DialogControl => StubProxy.Of(_dialog);

    private SettingsViewModel Create() => new(
        _settings,
        _dialog,
        StubProxy.Create<IThemeService>(),
        StubProxy.Create<IExplorerIntegrationService>(),
        _ => { },
        (_, _, _) => true);

    private void Answer(string? extension, string? exe)
    {
        DialogControl.On("PromptText", args => extension);
        DialogControl.On("ShowOpenFileDialog", args => exe);
    }

    [Fact]
    public void Constructor_LoadsTheSavedAssociations_AsCopies()
    {
        var saved = new AppAssociation { Extension = ".md", ExecutablePath = @"C:\Tools\md.exe" };
        _settings.Current.AppAssociations.Add(saved);

        var vm = Create();

        var shown = Assert.Single(vm.AppAssociations);
        Assert.Equal(".md", shown.Extension);
        Assert.NotSame(saved, shown); // 「保存」を押すまで、保存済みの設定には影響しない
    }

    [Fact]
    public void Add_RegistersTheAssociation_WithANormalizedExtension()
    {
        Answer("MD", @"C:\Tools\md.exe");
        var vm = Create();

        vm.AddAppAssociationCommand.Execute(null);

        var added = Assert.Single(vm.AppAssociations);
        Assert.Equal(".md", added.Extension);
        Assert.Equal(@"C:\Tools\md.exe", added.ExecutablePath);
    }

    [Fact]
    public void Add_ReplacesTheExistingAssociationForTheSameExtension()
    {
        _settings.Current.AppAssociations.Add(new AppAssociation { Extension = ".md", ExecutablePath = @"C:\Old\old.exe" });
        Answer(".MD", @"C:\New\new.exe");
        var vm = Create();

        vm.AddAppAssociationCommand.Execute(null);

        var only = Assert.Single(vm.AppAssociations);
        Assert.Equal(@"C:\New\new.exe", only.ExecutablePath);
    }

    [Fact]
    public void Add_WithAnInvalidExtension_ShowsAnError_AndAddsNothing()
    {
        Answer("a b", @"C:\Tools\x.exe");
        var vm = Create();

        vm.AddAppAssociationCommand.Execute(null);

        Assert.Empty(vm.AppAssociations);
        var message = (string)Assert.Single(DialogControl.ArgsOf("ShowError"))[0]!;
        Assert.Contains("a b", message);
        Assert.Equal(0, DialogControl.CountOf("ShowOpenFileDialog")); // アプリは尋ねない
    }

    [Theory]
    [InlineData(null, @"C:\Tools\x.exe")]
    [InlineData("", @"C:\Tools\x.exe")]
    [InlineData("   ", @"C:\Tools\x.exe")]
    [InlineData(".md", null)]
    [InlineData(".md", "")]
    public void Add_Cancelled_AddsNothing(string? extension, string? exe)
    {
        Answer(extension, exe);
        var vm = Create();

        vm.AddAppAssociationCommand.Execute(null);

        Assert.Empty(vm.AppAssociations);
        Assert.Equal(0, DialogControl.CountOf("ShowError"));
    }

    [Fact]
    public void Remove_DeletesTheChosenAssociation()
    {
        _settings.Current.AppAssociations.Add(new AppAssociation { Extension = ".md", ExecutablePath = @"C:\Tools\md.exe" });
        _settings.Current.AppAssociations.Add(new AppAssociation { Extension = ".cs", ExecutablePath = @"C:\Tools\ide.exe" });
        var vm = Create();
        var target = vm.AppAssociations.Single(a => a.Extension == ".md");

        vm.RemoveAppAssociationCommand.Execute(target);

        Assert.Equal(".cs", Assert.Single(vm.AppAssociations).Extension);
    }

    [Fact]
    public void Changes_AreNotPersisted_UntilSave()
    {
        Answer(".md", @"C:\Tools\md.exe");
        var vm = Create();

        vm.AddAppAssociationCommand.Execute(null);

        Assert.Empty(_settings.Current.AppAssociations);
        Assert.Equal(0, _settings.SaveCount);
    }

    [Fact]
    public void Save_PersistsTheAssociations()
    {
        Answer(".md", @"C:\Tools\md.exe");
        var vm = Create();
        vm.AddAppAssociationCommand.Execute(null);

        vm.Save();

        var saved = Assert.Single(_settings.Current.AppAssociations);
        Assert.Equal(".md", saved.Extension);
        Assert.Equal(@"C:\Tools\md.exe", saved.ExecutablePath);
        Assert.True(_settings.SaveCount >= 1);
    }

    [Fact]
    public void Save_AfterRemoval_PersistsTheRemoval()
    {
        _settings.Current.AppAssociations.Add(new AppAssociation { Extension = ".md", ExecutablePath = @"C:\Tools\md.exe" });
        var vm = Create();
        vm.RemoveAppAssociationCommand.Execute(vm.AppAssociations.Single());

        vm.Save();

        Assert.Empty(_settings.Current.AppAssociations);
    }
}
