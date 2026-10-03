using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using ExplorerAlternative.Tests.TestDoubles;

namespace ExplorerAlternative.Tests.Views;

// 運営者の依頼（#79）：選択中のファイル行は、背景色に加えて、Windows 11のエクスプローラーのように、外枠の色も変える。
// App.xamlの行のスタイル（階層表示・詳細表示）を、テーマの色とともに読み込んで、描画の結果を確かめる。
// ソースの無い環境（成果物だけの実行）では、確かめずに終わる。
public sealed class FileRowSelectionStyleTests
{
    private static string? ReadSource(params string[] relative)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var candidate = Path.Combine(new[] { directory.FullName, "ExplorerAlternative" }.Concat(relative).ToArray());
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            directory = directory.Parent;
        }

        return null;
    }

    private static ResourceDictionary? LoadStyleWithTheme(string styleKey, string themeFile)
    {
        var app = ReadSource("App.xaml");
        var theme = ReadSource("Themes", themeFile);
        if (app is null || theme is null)
        {
            return null;
        }

        var style = Regex.Match(app, $"<Style x:Key=\"{styleKey}\".*?</Style>", RegexOptions.Singleline).Value;
        Assert.False(string.IsNullOrEmpty(style), $"{styleKey}が見つかりません。");

        var dictionary = (ResourceDictionary)XamlReader.Parse(
            "<ResourceDictionary xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">" + style + "</ResourceDictionary>");
        dictionary.MergedDictionaries.Add((ResourceDictionary)XamlReader.Parse(theme));
        return dictionary;
    }

    private static Border RowBorder(Control item, ResourceDictionary resources, string styleKey)
    {
        item.Resources = resources;
        item.Style = (Style)resources[styleKey];
        item.ApplyTemplate();
        return (Border)item.Template.FindName("Bd", item);
    }

    private static Color BrushColor(Brush? brush) => ((SolidColorBrush)brush!).Color;

    private static readonly (string Style, Func<Control> Create)[] Rows =
    {
        ("FileRowItemStyle", () => new ListBoxItem()),
        ("DetailRowItemStyle", () => new ListViewItem())
    };

    [Theory]
    [InlineData("FileRowItemStyle", "LightTheme.xaml")]
    [InlineData("FileRowItemStyle", "DarkTheme.xaml")]
    [InlineData("DetailRowItemStyle", "LightTheme.xaml")]
    [InlineData("DetailRowItemStyle", "DarkTheme.xaml")]
    public void SelectedRow_GetsASelectionColoredOutline_AndFill(string styleKey, string themeFile)
    {
        StaTest.Run(() =>
        {
            var resources = LoadStyleWithTheme(styleKey, themeFile);
            if (resources is null) return;

            var item = Rows.Single(r => r.Style == styleKey).Create();
            var selectionBorder = BrushColor((Brush)resources["SelectionBorderBrush"]);
            var selectionBackground = BrushColor((Brush)resources["SelectionBackgroundBrush"]);

            var border = RowBorder(item, resources, styleKey);
            Assert.Equal(Colors.Transparent, BrushColor(border.BorderBrush)); // 選択していないときは、枠は見えない

            if (item is ListBoxItem listBoxItem)
            {
                listBoxItem.IsSelected = true;
            }
            else
            {
                ((ListViewItem)item).IsSelected = true;
            }

            item.ApplyTemplate();
            border = (Border)item.Template.FindName("Bd", item);

            Assert.Equal(selectionBorder, BrushColor(border.BorderBrush)); // 外枠の色が変わる
            Assert.Equal(selectionBackground, BrushColor(border.Background)); // 背景の色も変わる
        });
    }

    // 運営者の依頼（#79）：ライトテーマの選択色は、ピンクっぽくない（赤みが強くない）こと。
    [Fact]
    public void LightTheme_SelectionColors_AreNotPinkish()
    {
        StaTest.Run(() =>
        {
            var resources = LoadStyleWithTheme("FileRowItemStyle", "LightTheme.xaml");
            if (resources is null) return;

            var background = BrushColor((Brush)resources["SelectionBackgroundBrush"]);
            var border = BrushColor((Brush)resources["SelectionBorderBrush"]);

            // ピンク・サーモン系は、赤が青より強い。青系・無彩色は、赤が青を上回らない。
            Assert.True(background.R <= background.B, $"背景がピンクっぽい色です: {background}");
            Assert.True(border.R <= border.B, $"外枠がピンクっぽい色です: {border}");
            // 白い背景の上で、選択が見分けられること（白との差がある）。
            Assert.True(255 - background.R >= 20, "背景が白に近すぎます。");
        });
    }

    // ダークテーマは、従来どおりのアクセント色のまま。
    [Fact]
    public void DarkTheme_SelectionColors_KeepTheAccentLook()
    {
        StaTest.Run(() =>
        {
            var resources = LoadStyleWithTheme("FileRowItemStyle", "DarkTheme.xaml");
            if (resources is null) return;

            Assert.Equal(BrushColor((Brush)resources["AccentLightBrush"]), BrushColor((Brush)resources["SelectionBackgroundBrush"]));
            Assert.Equal(BrushColor((Brush)resources["AccentBrush"]), BrushColor((Brush)resources["SelectionBorderBrush"]));
        });
    }

    // 選択の有無で、行の大きさ・文字の位置が動かないこと（枠の分だけ、余白を減らしてある）。
    [Theory]
    [InlineData("FileRowItemStyle")]
    [InlineData("DetailRowItemStyle")]
    public void RowSize_DoesNotChangeWithSelection_AndMatchesTheOldLayout(string styleKey)
    {
        StaTest.Run(() =>
        {
            var resources = LoadStyleWithTheme(styleKey, "LightTheme.xaml");
            if (resources is null) return;

            var item = Rows.Single(r => r.Style == styleKey).Create();
            var border = RowBorder(item, resources, styleKey);

            // 枠（1px）＋余白＝合計で、以前の余白（左右4・上下5）と同じ。
            var total = new Thickness(
                border.BorderThickness.Left + border.Padding.Left,
                border.BorderThickness.Top + border.Padding.Top,
                border.BorderThickness.Right + border.Padding.Right,
                border.BorderThickness.Bottom + border.Padding.Bottom);
            Assert.Equal(new Thickness(4, 5, 4, 5), total);

            // 選択しても、枠の太さは変わらない（透明な枠が、最初からある）。
            var before = border.BorderThickness;
            if (item is ListBoxItem lb) lb.IsSelected = true; else ((ListViewItem)item).IsSelected = true;
            item.ApplyTemplate();
            border = (Border)item.Template.FindName("Bd", item);
            Assert.Equal(before, border.BorderThickness);
        });
    }

    // 枠を丸める（Windows 11のエクスプローラーの選択の見た目）。
    [Theory]
    [InlineData("FileRowItemStyle")]
    [InlineData("DetailRowItemStyle")]
    public void SelectionOutline_HasRoundedCorners(string styleKey)
    {
        StaTest.Run(() =>
        {
            var resources = LoadStyleWithTheme(styleKey, "LightTheme.xaml");
            if (resources is null) return;

            var border = RowBorder(Rows.Single(r => r.Style == styleKey).Create(), resources, styleKey);

            Assert.Equal(new CornerRadius(4), border.CornerRadius);
        });
    }
}
