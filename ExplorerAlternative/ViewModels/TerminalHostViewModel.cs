using System.Collections.ObjectModel;
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
    private readonly bool _syncByDefault;
    private int _counter;
    private bool _isVisible;
    private TerminalViewModel? _activeTerminal;

    public TerminalHostViewModel(Func<IPowerShellTerminalService> serviceFactory, bool syncByDefault)
    {
        _serviceFactory = serviceFactory;
        _syncByDefault = syncByDefault;

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

    private void AddTerminal()
    {
        _counter++;
        var terminal = new TerminalViewModel(_serviceFactory(), _syncByDefault, $"PowerShell {_counter}");
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
    }
}
