using ExplorerAlternative.Mvvm;
using ExplorerAlternative.Services.Abstractions;

namespace ExplorerAlternative.ViewModels;

/// <summary>
/// PowerShellターミナルのタブ1枚分（仕様書17章）。表示/非表示のパネル制御は
/// <see cref="TerminalHostViewModel"/>が担い、ここでは個々のPowerShellプロセスと
/// Explorerとの現在フォルダ同期ON/OFF（18章、タブごとに保持）を扱う。
/// </summary>
public sealed class TerminalViewModel : ObservableObject, IDisposable
{
    private readonly IPowerShellTerminalService _terminalService;
    private bool _isSyncEnabled;
    private bool _isActive;
    private string _outputText = string.Empty;
    private string _inputText = string.Empty;

    public TerminalViewModel(IPowerShellTerminalService terminalService, bool syncByDefault, string name)
    {
        _terminalService = terminalService;
        _isSyncEnabled = syncByDefault;
        Name = name;

        _terminalService.OutputReceived += (_, line) => AppendOutput(line);
        _terminalService.ErrorOccurred += (_, message) => AppendOutput($"[エラー] {message}");

        SendCommand = new RelayCommand(_ => Send(), _ => !string.IsNullOrWhiteSpace(InputText));
        ToggleSyncCommand = new RelayCommand(_ => IsSyncEnabled = !IsSyncEnabled);
    }

    public string Name { get; }

    /// <summary>タブストリップでの選択表示用。<see cref="TerminalHostViewModel"/>が管理する。</summary>
    public bool IsActive
    {
        get => _isActive;
        internal set => SetProperty(ref _isActive, value);
    }

    public RelayCommand SendCommand { get; }

    public RelayCommand ToggleSyncCommand { get; }

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

    public void Start()
    {
        if (!_terminalService.IsRunning)
        {
            _terminalService.Start();
        }
    }

    public void SyncCurrentDirectory(string path)
    {
        if (IsSyncEnabled && _terminalService.IsRunning)
        {
            _terminalService.ChangeDirectory(path);
        }
    }

    /// <summary>外部（SSH接続・Git操作・「ここでターミナルを開く」など）から生成したコマンドを、
    /// ユーザー入力と同様の見た目でターミナルへ送信する。</summary>
    public void SendRawCommand(string command)
    {
        Start();
        AppendOutput($"> {command}");
        _terminalService.SendCommand(command);
    }

    /// <summary>仕様書19章：ファイル・フォルダのドラッグ＆ドロップによるパス入力。</summary>
    public void InsertPathIntoInput(string path)
    {
        var quoted = path.Contains(' ') ? $"\"{path}\"" : path;
        InputText = string.IsNullOrEmpty(InputText) ? quoted : $"{InputText} {quoted}";
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

    public void Dispose() => _terminalService.Dispose();
}
