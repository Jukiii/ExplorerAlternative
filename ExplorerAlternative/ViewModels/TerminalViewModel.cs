using ExplorerAlternative.Mvvm;
using ExplorerAlternative.Services.Abstractions;

namespace ExplorerAlternative.ViewModels;

/// <summary>
/// PowerShellターミナル（仕様書9章）。表示/非表示（Ctrl+@）と、Explorerとの
/// 現在フォルダ同期ON/OFF（9.2章）を扱う。
/// </summary>
public sealed class TerminalViewModel : ObservableObject
{
    private readonly IPowerShellTerminalService _terminalService;
    private bool _isVisible;
    private bool _isSyncEnabled;
    private string _outputText = string.Empty;
    private string _inputText = string.Empty;

    public TerminalViewModel(IPowerShellTerminalService terminalService, bool syncByDefault)
    {
        _terminalService = terminalService;
        _isSyncEnabled = syncByDefault;

        _terminalService.OutputReceived += (_, line) => AppendOutput(line);
        _terminalService.ErrorOccurred += (_, message) => AppendOutput($"[エラー] {message}");

        SendCommand = new RelayCommand(_ => Send(), _ => !string.IsNullOrWhiteSpace(InputText));
        ToggleSyncCommand = new RelayCommand(_ => IsSyncEnabled = !IsSyncEnabled);
        ToggleVisibilityCommand = new RelayCommand(_ => IsVisible = !IsVisible);
    }

    public RelayCommand SendCommand { get; }

    public RelayCommand ToggleSyncCommand { get; }

    public RelayCommand ToggleVisibilityCommand { get; }

    public bool IsVisible
    {
        get => _isVisible;
        set
        {
            if (SetProperty(ref _isVisible, value) && value && !_terminalService.IsRunning)
            {
                _terminalService.Start();
            }
        }
    }

    public bool IsSyncEnabled
    {
        get => _isSyncEnabled;
        set => SetProperty(ref _isSyncEnabled, value);
    }

    public string OutputText
    {
        get => _outputText;
        private set => SetProperty(ref _outputText, value);
    }

    public string InputText
    {
        get => _inputText;
        set => SetProperty(ref _inputText, value);
    }

    public void SyncCurrentDirectory(string path)
    {
        if (IsSyncEnabled && _terminalService.IsRunning)
        {
            _terminalService.ChangeDirectory(path);
        }
    }

    /// <summary>外部（SSH接続など）から生成したコマンドを、ユーザー入力と同様の見た目でターミナルへ送信する。</summary>
    public void SendRawCommand(string command)
    {
        if (!_terminalService.IsRunning)
        {
            _terminalService.Start();
        }

        AppendOutput($"> {command}");
        _terminalService.SendCommand(command);
    }

    private void Send()
    {
        AppendOutput($"> {InputText}");
        _terminalService.SendCommand(InputText);
        InputText = string.Empty;
    }

    private void AppendOutput(string line)
    {
        OutputText += line + Environment.NewLine;
    }
}
