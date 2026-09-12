using System.Collections.ObjectModel;
using System.Windows.Controls;
using ExplorerAlternative.Mvvm;

namespace ExplorerAlternative.ViewModels;

/// <summary>
/// タブ1枚分（仕様書18章）。Panesは19章の分割ペインに対応し、Phase 1では最大2ペインまで
/// 保持できる（横分割/縦分割）。ツール操作の対象は常にアクティブペイン（19.3章）。
/// </summary>
public sealed class TabViewModel : ObservableObject
{
    private const int MaxPanes = 2;

    private readonly Dictionary<PaneViewModel, Action<string>> _pathHandlers = new();

    private string _header;
    private int _activePaneIndex;
    private Orientation _splitOrientation = Orientation.Horizontal;
    private bool _isPinned;

    public TabViewModel(PaneViewModel initialPane, string header)
    {
        _header = header;
        AddPane(initialPane);

        TogglePinCommand = new RelayCommand(_ => IsPinned = !IsPinned);
        SwapPanesCommand = new RelayCommand(_ => SwapPanes(), _ => Panes.Count == 2);
    }

    public RelayCommand TogglePinCommand { get; }

    public RelayCommand SwapPanesCommand { get; }

    /// <summary>仕様書10章「タブ固定」。固定中のタブは閉じるボタンの代わりにピンアイコンを表示する。</summary>
    public bool IsPinned
    {
        get => _isPinned;
        set
        {
            if (SetProperty(ref _isPinned, value))
            {
                OnPropertyChanged(nameof(PinMenuHeader));
            }
        }
    }

    public string PinMenuHeader => IsPinned ? "固定を解除" : "固定";

    /// <summary>アクティブペインの現在フォルダが変わったときに発火する（ターミナル同期用）。</summary>
    public event Action<string>? ActivePanePathChanged;

    public ObservableCollection<PaneViewModel> Panes { get; } = new();

    public string Header
    {
        get => _header;
        set => SetProperty(ref _header, value);
    }

    public Orientation SplitOrientation
    {
        get => _splitOrientation;
        set
        {
            if (SetProperty(ref _splitOrientation, value))
            {
                RaiseGridLayoutChanged();
            }
        }
    }

    public int ActivePaneIndex
    {
        get => _activePaneIndex;
        private set
        {
            if (SetProperty(ref _activePaneIndex, value))
            {
                OnPropertyChanged(nameof(ActivePane));
                UpdateActiveFlags();
            }
        }
    }

    public PaneViewModel ActivePane => Panes[Math.Clamp(ActivePaneIndex, 0, Panes.Count - 1)];

    public bool CanSplit => Panes.Count < MaxPanes;

    public bool CanClosePane => Panes.Count > 1;

    /// <summary>ペインが1件のみのときはアクティブ枠を表示する意味がないため隠す。</summary>
    public bool ShowPaneBorders => Panes.Count > 1;

    /// <summary>UniformGridのColumns算出用（横分割時のみ2列、それ以外は1列）。</summary>
    public int GridColumns => SplitOrientation == Orientation.Horizontal && Panes.Count > 1 ? 2 : 1;

    /// <summary>UniformGridのRows算出用（縦分割時のみ2行、それ以外は1行）。</summary>
    public int GridRows => SplitOrientation == Orientation.Vertical && Panes.Count > 1 ? 2 : 1;

    public void AddPane(PaneViewModel pane)
    {
        Panes.Add(pane);

        void Handler(string path) => OnPanePathChanged(pane, path);
        _pathHandlers[pane] = Handler;
        pane.PathChanged += Handler;

        _activePaneIndex = Panes.Count - 1;
        OnPropertyChanged(nameof(ActivePaneIndex));
        OnPropertyChanged(nameof(ActivePane));
        OnPropertyChanged(nameof(CanSplit));
        OnPropertyChanged(nameof(CanClosePane));
        OnPropertyChanged(nameof(ShowPaneBorders));
        UpdateActiveFlags();
        RaiseGridLayoutChanged();
        SwapPanesCommand?.RaiseCanExecuteChanged();
    }

    public void RemovePane(PaneViewModel pane)
    {
        if (Panes.Count <= 1)
        {
            return;
        }

        if (_pathHandlers.TryGetValue(pane, out var handler))
        {
            pane.PathChanged -= handler;
            _pathHandlers.Remove(pane);
        }

        var index = Panes.IndexOf(pane);
        Panes.Remove(pane);

        // ActivePaneIndexプロパティのセッターは値が変化しない場合に通知をスキップするが、
        // 削除により同じインデックスでも指す先のPaneViewModelが変わることがあるため、
        // ここでは常にActivePane変更通知を発火させる（パンくずバー等が古いペインを
        // 参照し続けてしまう不具合の修正）。
        _activePaneIndex = Math.Min(index, Panes.Count - 1);
        OnPropertyChanged(nameof(ActivePaneIndex));
        OnPropertyChanged(nameof(ActivePane));
        UpdateActiveFlags();
        OnPropertyChanged(nameof(CanSplit));
        OnPropertyChanged(nameof(CanClosePane));
        OnPropertyChanged(nameof(ShowPaneBorders));
        RaiseGridLayoutChanged();
        SwapPanesCommand.RaiseCanExecuteChanged();
    }

    // 仕様書8章「左右/上下入れ替え」。アクティブペインは同じPaneViewModelを指し続けるよう追従させる。
    private void SwapPanes()
    {
        if (Panes.Count != 2)
        {
            return;
        }

        var activePane = ActivePane;
        Panes.Move(0, 1);
        SetActivePane(activePane);
    }

    public void SetActivePane(PaneViewModel pane)
    {
        var index = Panes.IndexOf(pane);
        if (index >= 0)
        {
            ActivePaneIndex = index;
        }
    }

    private void OnPanePathChanged(PaneViewModel pane, string path)
    {
        if (ReferenceEquals(pane, ActivePane))
        {
            ActivePanePathChanged?.Invoke(path);
        }
    }

    private void UpdateActiveFlags()
    {
        for (var i = 0; i < Panes.Count; i++)
        {
            Panes[i].IsActive = i == ActivePaneIndex;
        }
    }

    private void RaiseGridLayoutChanged()
    {
        OnPropertyChanged(nameof(GridColumns));
        OnPropertyChanged(nameof(GridRows));
    }
}
