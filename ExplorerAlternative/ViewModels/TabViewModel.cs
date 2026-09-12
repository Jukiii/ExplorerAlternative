using System.Collections.ObjectModel;
using ExplorerAlternative.Mvvm;

namespace ExplorerAlternative.ViewModels;

/// <summary>
/// タブ1枚分（仕様書18章）。Panesは19章の分割ペイン拡張に備えた構造で、
/// Phase 1では常に1件のみを保持する。
/// </summary>
public sealed class TabViewModel : ObservableObject
{
    private string _header;
    private int _activePaneIndex;

    public TabViewModel(PaneViewModel initialPane, string header)
    {
        Panes.Add(initialPane);
        initialPane.IsActive = true;
        _header = header;
    }

    public ObservableCollection<PaneViewModel> Panes { get; } = new();

    public string Header
    {
        get => _header;
        set => SetProperty(ref _header, value);
    }

    public int ActivePaneIndex
    {
        get => _activePaneIndex;
        set => SetProperty(ref _activePaneIndex, value);
    }

    public PaneViewModel ActivePane => Panes[Math.Clamp(ActivePaneIndex, 0, Panes.Count - 1)];
}
