using System.Collections.ObjectModel;
using ExplorerAlternative.Models;
using ExplorerAlternative.Mvvm;
using ExplorerAlternative.Services.Abstractions;

namespace ExplorerAlternative.ViewModels;

/// <summary>
/// 統合ターミナルパネルのタブ管理（仕様書17章「複数タブ」・18章「Sync状態はターミナルごとに保持」）。
/// パネル自体の表示/非表示（Ctrl+@）はここで一元管理し、各タブは独立したPowerShellプロセスを持つ。
/// </summary>
public sealed class TerminalHostViewModel : ObservableObject, IDisposable
{
    private readonly Func<IPowerShellTerminalService> _serviceFactory;
    private readonly ITabCompletionService? _tabCompletionService;
    private readonly bool _syncByDefault;
    private const double MinPanelHeight = 80;
    private const double MaxPanelHeight = 900;

    private int _counter;
    private bool _isVisible;
    private TerminalViewModel? _activeTerminal;
    private double _panelHeight = 230;

    /// <param name="tabCompletionService">全タブで共有するTab補完のサービス（破棄はこのクラスが行う）。省略すると補完は使えない。</param>
    public TerminalHostViewModel(Func<IPowerShellTerminalService> serviceFactory, bool syncByDefault, ITabCompletionService? tabCompletionService = null)
    {
        _serviceFactory = serviceFactory;
        _syncByDefault = syncByDefault;
        _tabCompletionService = tabCompletionService;

        AddTerminalCommand = new RelayCommand(_ => AddTerminal());
        CloseTerminalCommand = new RelayCommand(p => CloseTerminal(p as TerminalViewModel));
        ToggleVisibilityCommand = new RelayCommand(_ => IsVisible = !IsVisible);
    }

    public ObservableCollection<TerminalViewModel> Terminals { get; } = new();

    public TerminalViewModel? ActiveTerminal
    {
        get => _activeTerminal;
        set
        {
            if (ReferenceEquals(_activeTerminal, value))
            {
                return;
            }

            if (_activeTerminal is not null)
            {
                _activeTerminal.IsActive = false;
            }

            SetProperty(ref _activeTerminal, value);

            if (value is not null)
            {
                value.IsActive = true;
            }
        }
    }

    public bool IsVisible
    {
        get => _isVisible;
        set
        {
            if (SetProperty(ref _isVisible, value) && value && Terminals.Count == 0)
            {
                AddTerminal();
            }
        }
    }

    /// <summary>仕様書17章「高さはドラッグ変更可能」。パネル上端のつまみをドラッグして変更する。</summary>
    public double PanelHeight
    {
        get => _panelHeight;
        set => SetProperty(ref _panelHeight, Math.Clamp(value, MinPanelHeight, MaxPanelHeight));
    }

    /// <summary>仕様書43章：ワークスペースへ保存する、ターミナルパネルの状態。</summary>
    public TerminalWorkspaceState CaptureState() => new()
    {
        IsVisible = IsVisible,
        PanelHeight = PanelHeight,
        ActiveIndex = ActiveTerminal is null ? 0 : Math.Max(0, Terminals.IndexOf(ActiveTerminal)),
        Tabs = Terminals.Select(t => new TerminalTabState { SyncEnabled = t.IsSyncEnabled }).ToList()
    };

    /// <summary>
    /// 仕様書43章：保存した状態を復元する。実行中のターミナルは閉じない（実行中の作業を失わないため）。
    /// 保存時にパネルを表示していた場合は、タブが足りなければ追加し、Syncの設定とアクティブなタブを
    /// 保存時に合わせる。非表示だった場合は、パネルを隠すだけで、タブは作らない（シェルを無駄に起動しない）。
    /// 状態が無い（古い保存データ）場合は、何もしない。
    /// </summary>
    public void RestoreState(TerminalWorkspaceState? state)
    {
        if (state is null)
        {
            return;
        }

        if (!double.IsNaN(state.PanelHeight))
        {
            PanelHeight = state.PanelHeight;
        }

        if (!state.IsVisible)
        {
            IsVisible = false;
            return;
        }

        var wanted = Math.Max(1, state.Tabs.Count);
        while (Terminals.Count < wanted)
        {
            AddTerminal();
        }

        for (var i = 0; i < Math.Min(Terminals.Count, state.Tabs.Count); i++)
        {
            Terminals[i].IsSyncEnabled = state.Tabs[i].SyncEnabled;
        }

        ActiveTerminal = Terminals[Math.Clamp(state.ActiveIndex, 0, Terminals.Count - 1)];
        IsVisible = true;
    }

    public RelayCommand AddTerminalCommand { get; }

    public RelayCommand CloseTerminalCommand { get; }

    public RelayCommand ToggleVisibilityCommand { get; }

    public void Show() => IsVisible = true;

    public void SyncCurrentDirectory(string path) => ActiveTerminal?.SyncCurrentDirectory(path);

    public void SendRawCommand(string command)
    {
        Show();
        ActiveTerminal?.SendRawCommand(command);
    }

    /// <summary>仕様書44章：SSH接続時、保存済みパスワードがあれば自動入力する。</summary>
    public void SendRawCommand(string command, string? password)
    {
        Show();
        ActiveTerminal?.SendRawCommand(command, password);
    }

    private void AddTerminal()
    {
        _counter++;
        var terminal = new TerminalViewModel(
            _serviceFactory(), _syncByDefault, $"PowerShell {_counter}", _tabCompletionService, Environment.CurrentDirectory);
        terminal.Start();
        Terminals.Add(terminal);
        ActiveTerminal = terminal;
    }

    private void CloseTerminal(TerminalViewModel? terminal)
    {
        if (terminal is null)
        {
            return;
        }

        var index = Terminals.IndexOf(terminal);
        if (index < 0)
        {
            return;
        }

        Terminals.Remove(terminal);
        terminal.Dispose();

        if (Terminals.Count == 0)
        {
            ActiveTerminal = null;
            IsVisible = false;
            return;
        }

        if (ReferenceEquals(ActiveTerminal, terminal))
        {
            ActiveTerminal = Terminals[Math.Min(index, Terminals.Count - 1)];
        }
    }

    public void Dispose()
    {
        foreach (var terminal in Terminals)
        {
            terminal.Dispose();
        }

        _tabCompletionService?.Dispose();
    }
}
