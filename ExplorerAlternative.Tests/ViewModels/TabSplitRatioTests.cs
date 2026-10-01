using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Controls;
using ExplorerAlternative.Models;
using ExplorerAlternative.Tests.TestDoubles;
using ExplorerAlternative.ViewModels;

namespace ExplorerAlternative.Tests.ViewModels;

// 仕様書19章：分割ペインの大きさ（比率）。タブが持ち、ワークスペースに保存される。
public sealed class TabSplitRatioTests : IDisposable
{
    private readonly PaneTestHost _first = new();
    private readonly PaneTestHost _second = new();

    public void Dispose()
    {
        _first.Dispose();
        _second.Dispose();
    }

    private TabViewModel CreateSplitTab()
    {
        var tab = new TabViewModel(_first.Pane, "tab");
        tab.AddPane(_second.Pane);
        return tab;
    }

    [Fact]
    public void NewTab_StartsWithEvenSplit()
    {
        var tab = new TabViewModel(_first.Pane, "tab");

        Assert.Equal(0.5, tab.SplitRatio);
    }

    [Fact]
    public void SplitRatio_IsClampedToTheAllowedRange()
    {
        var tab = CreateSplitTab();

        tab.SplitRatio = 0.0;
        Assert.Equal(SplitLayout.MinRatio, tab.SplitRatio);

        tab.SplitRatio = 5;
        Assert.Equal(SplitLayout.MaxRatio, tab.SplitRatio);

        tab.SplitRatio = double.NaN;
        Assert.Equal(SplitLayout.DefaultRatio, tab.SplitRatio);
    }

    [Fact]
    public void SplitRatio_RaisesPropertyChanged_OnlyWhenItChanges()
    {
        var tab = CreateSplitTab();
        var raised = 0;
        tab.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(TabViewModel.SplitRatio))
            {
                raised++;
            }
        };

        tab.SplitRatio = 0.7;
        tab.SplitRatio = 0.7;

        Assert.Equal(1, raised);
    }

    // 新しく分割したときは、前回の分割での比率に関係なく、半分ずつから始める。
    [Fact]
    public void AddingASecondPane_ResetsToEvenSplit()
    {
        var tab = CreateSplitTab();
        tab.SplitRatio = 0.8;
        tab.RemovePane(_second.Pane);

        tab.AddPane(_second.Pane);

        Assert.Equal(0.5, tab.SplitRatio);
    }

    // 入れ替えたペインの大きさも、ペインについていく。
    [Fact]
    public void SwappingPanes_MovesTheSizesWithThePanes()
    {
        var tab = CreateSplitTab();
        tab.SplitRatio = 0.7; // 最初のペインが70%

        tab.SwapPanesCommand.Execute(null);

        Assert.Same(_second.Pane, tab.Panes[0]);
        Assert.Equal(0.3, tab.SplitRatio, precision: 10);
    }

    [Fact]
    public void SwappingTwice_RestoresTheOriginalRatio()
    {
        var tab = CreateSplitTab();
        tab.SplitRatio = 0.65;

        tab.SwapPanesCommand.Execute(null);
        tab.SwapPanesCommand.Execute(null);

        Assert.Equal(0.65, tab.SplitRatio, precision: 10);
        Assert.Same(_first.Pane, tab.Panes[0]);
    }

    [Fact]
    public void SplitOrientation_CanChange_WithoutAffectingTheRatio()
    {
        var tab = CreateSplitTab();
        tab.SplitRatio = 0.4;

        tab.SplitOrientation = Orientation.Vertical;

        Assert.Equal(Orientation.Vertical, tab.SplitOrientation);
        Assert.Equal(0.4, tab.SplitRatio, precision: 10);
    }

    // ===== ワークスペースの保存データ（TabState） =====

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    [Fact]
    public void TabState_SplitRatio_RoundTripsThroughJson()
    {
        var state = new TabState { Header = "t", SplitOrientation = Orientation.Vertical, SplitRatio = 0.35 };

        var json = JsonSerializer.Serialize(state, Options);
        var restored = JsonSerializer.Deserialize<TabState>(json, Options)!;

        Assert.Equal(0.35, restored.SplitRatio, precision: 10);
        Assert.Equal(Orientation.Vertical, restored.SplitOrientation);
    }

    // この機能の追加前に保存したワークスペース（SplitRatioが無いJSON）も、半分ずつとして読み込める。
    [Fact]
    public void TabState_FromOldJsonWithoutSplitRatio_DefaultsToEvenSplit()
    {
        const string oldJson = """
            { "Header": "old", "ActivePaneIndex": 0, "SplitOrientation": "Horizontal", "IsPinned": false,
              "Panes": [ { "CurrentPath": "C:\\", "ViewMode": "Tree" }, { "CurrentPath": "D:\\", "ViewMode": "Detail" } ] }
            """;

        var restored = JsonSerializer.Deserialize<TabState>(oldJson, Options)!;

        Assert.Equal(0.5, restored.SplitRatio);
        Assert.Equal(2, restored.Panes.Count);
    }

    // 保存データが壊れていても（範囲外の値）、タブの比率は有効な範囲に収まる。
    [Fact]
    public void RestoringAnOutOfRangeRatio_IsClamped()
    {
        var tab = CreateSplitTab();
        var state = JsonSerializer.Deserialize<TabState>("""{ "SplitRatio": 12.5 }""", Options)!;

        tab.SplitRatio = state.SplitRatio;

        Assert.Equal(SplitLayout.MaxRatio, tab.SplitRatio);
    }
}
