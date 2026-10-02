using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Threading;
using ExplorerAlternative.Tests.TestDoubles;

namespace ExplorerAlternative.Tests.Views;

// 仕様書17章「ターミナルの高さはドラッグで変更できる」：ターミナル枠の高さが、PanelHeightで決まること。
// 以前は、枠のGridが「DataContext={Binding TerminalHost}」と「Height={Binding TerminalHost.PanelHeight}」を
// 同じ要素に持っていた。WPFでは、同じ要素の他のバインディングは、変更後のDataContext（TerminalHost）を基準にするため、
// 「TerminalHost.PanelHeight」を見つけられず、高さが指定なし（自動）になっていた。その結果、ターミナルの枠が
// 内容（出力・入力の行）に合わせて、勝手に広がり、ドラッグでの高さ変更も効かなかった。
public sealed class TerminalPanelHeightBindingTests
{
    public sealed class Host
    {
        public double PanelHeight { get; set; } = 230;
    }

    public sealed class Root
    {
        public Host TerminalHost { get; } = new();
    }

    private static double HeightOf(string heightBinding)
    {
        var height = double.NaN;

        StaTest.Run(() =>
        {
            var xaml = $$"""
                <Border xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                    <Grid Height="{{heightBinding}}" DataContext="{Binding TerminalHost}" />
                </Border>
                """;

            var border = (Border)XamlReader.Parse(xaml);
            border.DataContext = new Root();

            // バインディングの評価は、DataContextの設定のあとに非同期で行われるため、処理が済むのを待つ。
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);

            border.Measure(new Size(500, 500));
            border.Arrange(new Rect(0, 0, 500, 500));
            height = ((Grid)border.Child).Height;
        });

        return height;
    }

    [Fact]
    public void TheOldPattern_LeavesTheHeightUnset_WhichIsWhyThePanelGrewWithItsContent()
    {
        Assert.True(double.IsNaN(HeightOf("{Binding TerminalHost.PanelHeight}")));
    }

    [Fact]
    public void TheFixedPattern_TakesTheHeightFromThePanelHeight()
    {
        Assert.Equal(230, HeightOf("{Binding PanelHeight}"));
    }

    // 実際のMainWindow.xamlが、正しい書き方になっていること（画面を開かずに、XAMLの文面で確かめる）。
    [Fact]
    public void MainWindowXaml_BindsThePanelHeightRelativeToTheTerminalHostContext()
    {
        var xamlPath = FindMainWindowXaml();
        if (xamlPath is null)
        {
            return; // ソースの無い環境（成果物だけの実行）では、確かめられない
        }

        var xaml = File.ReadAllText(xamlPath);
        var panel = Regex.Match(xaml, @"<Grid[^>]*DataContext=""\{Binding TerminalHost\}""[^>]*>", RegexOptions.Singleline);

        Assert.True(panel.Success, "ターミナル枠のGridが見つかりません。");
        Assert.Contains("Height=\"{Binding PanelHeight}\"", panel.Value);
        Assert.DoesNotContain("TerminalHost.PanelHeight", panel.Value);
    }

    private static string? FindMainWindowXaml()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "ExplorerAlternative", "MainWindow.xaml");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        return null;
    }
}
