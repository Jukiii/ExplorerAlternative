using System.Text.Json;
using System.Text.Json.Serialization;
using ExplorerAlternative.Models;
using ExplorerAlternative.Tests.TestDoubles;
using ExplorerAlternative.ViewModels;

namespace ExplorerAlternative.Tests.ViewModels;

// 仕様書43章：ワークスペースに保存する状態（ウィンドウの最大化・ターミナル・階層表示の展開状態）。
public sealed class WorkspaceStateTests : IDisposable
{
    private readonly List<FakeTerminalService> _terminalServices = new();
    private readonly List<PaneTestHost> _hosts = new();

    public void Dispose()
    {
        foreach (var host in _hosts)
        {
            host.Dispose();
        }
    }

    private TerminalHostViewModel CreateTerminalHost(bool syncByDefault = true) =>
        new(() =>
        {
            var service = new FakeTerminalService();
            _terminalServices.Add(service);
            return service;
        }, syncByDefault);

    // ===== ターミナル：保存 =====

    [Fact]
    public void Capture_WithNoTerminals_IsHiddenAndEmpty()
    {
        var host = CreateTerminalHost();

        var state = host.CaptureState();

        Assert.False(state.IsVisible);
        Assert.Empty(state.Tabs);
    }

    [Fact]
    public void Capture_RecordsVisibilityTabsSyncAndActiveTab()
    {
        var host = CreateTerminalHost();
        host.Show();                        // タブ1が自動で作られる
        host.AddTerminalCommand.Execute(null); // タブ2
        host.AddTerminalCommand.Execute(null); // タブ3（アクティブ）
        host.Terminals[1].IsSyncEnabled = false;
        host.ActiveTerminal = host.Terminals[1];
        host.PanelHeight = 340;

        var state = host.CaptureState();

        Assert.True(state.IsVisible);
        Assert.Equal(340, state.PanelHeight);
        Assert.Equal(1, state.ActiveIndex);
        Assert.Equal(new[] { true, false, true }, state.Tabs.Select(t => t.SyncEnabled).ToArray());
    }

    // ===== ターミナル：復元 =====

    [Fact]
    public void Restore_Null_DoesNothing()
    {
        var host = CreateTerminalHost();

        host.RestoreState(null);

        Assert.False(host.IsVisible);
        Assert.Empty(host.Terminals);
    }

    [Fact]
    public void Restore_Visible_CreatesTheSavedTabsWithSyncAndActiveTab()
    {
        var host = CreateTerminalHost();
        var state = new TerminalWorkspaceState
        {
            IsVisible = true,
            PanelHeight = 300,
            ActiveIndex = 1,
            Tabs = { new TerminalTabState { SyncEnabled = true }, new TerminalTabState { SyncEnabled = false } }
        };

        host.RestoreState(state);

        Assert.True(host.IsVisible);
        Assert.Equal(2, host.Terminals.Count);
        Assert.False(host.Terminals[1].IsSyncEnabled);
        Assert.Same(host.Terminals[1], host.ActiveTerminal);
        Assert.Equal(300, host.PanelHeight);
    }

    [Fact]
    public void Restore_Hidden_DoesNotStartAnyShell_AndHidesThePanel()
    {
        var host = CreateTerminalHost();
        host.Show();
        var before = host.Terminals.Count;

        host.RestoreState(new TerminalWorkspaceState { IsVisible = false, Tabs = { new TerminalTabState(), new TerminalTabState() } });

        Assert.False(host.IsVisible);
        Assert.Equal(before, host.Terminals.Count); // 非表示で保存されたときは、タブを増やさない
    }

    [Fact]
    public void Restore_Hidden_OnAFreshHost_CreatesNoTerminals()
    {
        var host = CreateTerminalHost();

        host.RestoreState(new TerminalWorkspaceState { IsVisible = false, Tabs = { new TerminalTabState() } });

        Assert.Empty(host.Terminals);
        Assert.Empty(_terminalServices);
    }

    // 実行中のターミナルは、ワークスペースを読み込んでも閉じない（実行中の作業を失わないため）。
    [Fact]
    public void Restore_NeverClosesRunningTerminals()
    {
        var host = CreateTerminalHost();
        host.Show();
        host.AddTerminalCommand.Execute(null);
        host.AddTerminalCommand.Execute(null);
        var existing = host.Terminals.ToList();

        host.RestoreState(new TerminalWorkspaceState { IsVisible = true, Tabs = { new TerminalTabState() } });

        Assert.Equal(3, host.Terminals.Count);
        Assert.Equal(existing, host.Terminals.ToList());
    }

    [Fact]
    public void Restore_WithFewerTabsThanSaved_AddsOnlyTheMissingOnes()
    {
        var host = CreateTerminalHost();
        host.Show(); // 1つ

        host.RestoreState(new TerminalWorkspaceState
        {
            IsVisible = true,
            Tabs = { new TerminalTabState(), new TerminalTabState(), new TerminalTabState() }
        });

        Assert.Equal(3, host.Terminals.Count);
    }

    [Fact]
    public void Restore_OutOfRangeActiveIndex_IsClamped()
    {
        var host = CreateTerminalHost();

        host.RestoreState(new TerminalWorkspaceState
        {
            IsVisible = true,
            ActiveIndex = 99,
            Tabs = { new TerminalTabState(), new TerminalTabState() }
        });

        Assert.Same(host.Terminals[1], host.ActiveTerminal);
    }

    [Fact]
    public void Restore_VisibleWithNoSavedTabs_StillShowsOneTerminal()
    {
        var host = CreateTerminalHost();

        host.RestoreState(new TerminalWorkspaceState { IsVisible = true });

        Assert.True(host.IsVisible);
        Assert.Single(host.Terminals);
    }

    [Fact]
    public void Restore_PanelHeight_IsClampedAndNanIsIgnored()
    {
        var host = CreateTerminalHost();

        host.RestoreState(new TerminalWorkspaceState { IsVisible = false, PanelHeight = 99999 });
        Assert.Equal(900, host.PanelHeight);

        host.RestoreState(new TerminalWorkspaceState { IsVisible = false, PanelHeight = double.NaN });
        Assert.Equal(900, host.PanelHeight); // NaNは無視して、直前の高さのまま
    }

    // ===== 階層表示の展開状態 =====

    private PaneTestHost CreateTreeHost()
    {
        var host = new PaneTestHost();
        _hosts.Add(host);
        Directory.CreateDirectory(Path.Combine(host.Root, "a", "b"));
        Directory.CreateDirectory(Path.Combine(host.Root, "a", "c"));
        Directory.CreateDirectory(Path.Combine(host.Root, "z"));
        host.Pane.RefreshCommand.Execute(null);
        return host;
    }

    [Fact]
    public void ExpandedPaths_AreEmpty_WhenNothingIsExpanded()
    {
        var host = CreateTreeHost();

        Assert.Empty(host.Pane.GetExpandedFolderPaths());
    }

    [Fact]
    public void ExpandedPaths_ListTheExpandedFolders_IncludingNestedOnes()
    {
        var host = CreateTreeHost();
        var a = host.Pane.VisibleNodes.Single(n => n.Name == "a");
        a.IsExpanded = true;
        var b = host.Pane.VisibleNodes.Single(n => n.Name == "b");
        b.IsExpanded = true;

        var paths = host.Pane.GetExpandedFolderPaths();

        Assert.Equal(
            new[] { Path.Combine(host.Root, "a"), Path.Combine(host.Root, "a", "b") }.OrderBy(p => p).ToArray(),
            paths.OrderBy(p => p).ToArray());
    }

    [Fact]
    public void RestoreExpandedFolders_ExpandsTheSavedFoldersInANewPane()
    {
        var source = CreateTreeHost();
        source.Pane.VisibleNodes.Single(n => n.Name == "a").IsExpanded = true;
        source.Pane.VisibleNodes.Single(n => n.Name == "b").IsExpanded = true;
        var saved = source.Pane.GetExpandedFolderPaths().ToList();

        // 同じフォルダを、新しいペインで開き直して復元する。
        var restored = new PaneTestHost(source.Root);
        restored.Pane.RestoreExpandedFolders(saved);

        var names = restored.Pane.VisibleNodes.Select(n => n.Name).ToList();
        Assert.Contains("b", names);
        Assert.Contains("c", names);
        Assert.True(restored.Pane.VisibleNodes.Single(n => n.Name == "a").IsExpanded);
        Assert.False(restored.Pane.VisibleNodes.Single(n => n.Name == "z").IsExpanded);
    }

    // 保存後に消えたフォルダのパスは、無視される（エラーにしない）。
    [Fact]
    public void RestoreExpandedFolders_IgnoresPathsThatNoLongerExist()
    {
        var host = CreateTreeHost();

        host.Pane.RestoreExpandedFolders(new[] { Path.Combine(host.Root, "gone"), Path.Combine(host.Root, "a") });

        Assert.True(host.Pane.VisibleNodes.Single(n => n.Name == "a").IsExpanded);
        Assert.DoesNotContain(host.Pane.VisibleNodes, n => n.Name == "gone");
    }

    [Fact]
    public void RestoreExpandedFolders_EmptyList_ChangesNothing()
    {
        var host = CreateTreeHost();
        var before = host.Pane.VisibleNodes.Count;

        host.Pane.RestoreExpandedFolders(Array.Empty<string>());

        Assert.Equal(before, host.Pane.VisibleNodes.Count);
    }

    [Fact]
    public void RestoreExpandedFolders_IgnoresCaseAndBlankEntries()
    {
        var host = CreateTreeHost();

        host.Pane.RestoreExpandedFolders(new[] { "", "   ", Path.Combine(host.Root, "a").ToUpperInvariant() });

        Assert.True(host.Pane.VisibleNodes.Single(n => n.Name == "a").IsExpanded);
    }

    // ===== 保存データ（JSON）の互換 =====

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    [Fact]
    public void WorkspaceState_NewFields_RoundTripThroughJson()
    {
        var state = new WorkspaceState
        {
            Name = "ws",
            IsWindowMaximized = true,
            Terminal = new TerminalWorkspaceState
            {
                IsVisible = true,
                PanelHeight = 311,
                ActiveIndex = 1,
                Tabs = { new TerminalTabState { SyncEnabled = false }, new TerminalTabState() }
            },
            Tabs = { new TabState { Panes = { new PaneState { CurrentPath = @"C:\x", ExpandedPaths = { @"C:\x\a" } } } } }
        };

        var restored = JsonSerializer.Deserialize<WorkspaceState>(JsonSerializer.Serialize(state, Options), Options)!;

        Assert.True(restored.IsWindowMaximized);
        Assert.Equal(311, restored.Terminal!.PanelHeight);
        Assert.False(restored.Terminal.Tabs[0].SyncEnabled);
        Assert.Equal(new[] { @"C:\x\a" }, restored.Tabs[0].Panes[0].ExpandedPaths);
    }

    // この機能の追加前に保存したワークスペース（新しい項目が無いJSON）も、問題なく読み込める。
    [Fact]
    public void WorkspaceState_FromOldJson_UsesSafeDefaults()
    {
        const string oldJson = """
            { "Name": "old", "WindowWidth": 1200, "WindowHeight": 700, "WindowLeft": 10, "WindowTop": 20,
              "ActiveTabIndex": 0, "NavigationPaneCollapsed": false,
              "Tabs": [ { "Header": "t", "ActivePaneIndex": 0, "SplitOrientation": "Horizontal", "IsPinned": false,
                          "Panes": [ { "CurrentPath": "C:\\", "ViewMode": "Tree" } ] } ] }
            """;

        var restored = JsonSerializer.Deserialize<WorkspaceState>(oldJson, Options)!;

        Assert.False(restored.IsWindowMaximized);
        Assert.Null(restored.Terminal);
        Assert.Empty(restored.Tabs[0].Panes[0].ExpandedPaths);
        Assert.Equal(0.5, restored.Tabs[0].SplitRatio);
    }
}
