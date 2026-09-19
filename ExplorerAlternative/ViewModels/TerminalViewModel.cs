using System.Text;
using ExplorerAlternative.Models;
using ExplorerAlternative.Mvvm;
using ExplorerAlternative.Rendering;
using ExplorerAlternative.Services.Abstractions;

namespace ExplorerAlternative.ViewModels;

/// <summary>
/// PowerShellターミナルのタブ1枚分（仕様書17章）。
///
/// VS Codeの統合ターミナルと同じ構造にするため、出力欄と入力欄を分けず「1つの
/// ターミナル画面」として扱う。入力中のテキストを保持するのはView側（RichTextBox）で、
/// ここではシェルプロセス・スクロールバック（色付き断片）・コマンド履歴を担当する。
/// </summary>
public sealed class TerminalViewModel : ObservableObject, IDisposable
{
    /// <summary>スクロールバックの上限（文字数）。超えた分は古い方から捨てる。</summary>
    private const int MaxBufferChars = 200_000;

    private const int MaxHistory = 200;

    private readonly IPowerShellTerminalService _terminalService;
    private readonly AnsiTextParser _ansiParser = new();
    private readonly List<TerminalSegment> _buffer = new();
    private readonly List<string> _history = new();
    private readonly StringBuilder _currentLine = new();

    private int _bufferChars;
    private int _historyIndex;
    private bool _isSyncEnabled;
    private bool _isActive;
    private string? _pendingPassword;

    public TerminalViewModel(IPowerShellTerminalService terminalService, bool syncByDefault, string name)
    {
        _terminalService = terminalService;
        _isSyncEnabled = syncByDefault;
        Name = name;

        _terminalService.OutputReceived += (_, text) => AppendOutput(text);
        _terminalService.ErrorOccurred += (_, message) => AppendOutput($"{Environment.NewLine}[エラー] {message}{Environment.NewLine}");

        ToggleSyncCommand = new RelayCommand(_ => IsSyncEnabled = !IsSyncEnabled);
    }

    public string Name { get; }

    /// <summary>シェルから新しい出力が届いたときに発火する（View側が画面へ追記する）。</summary>
    public event Action<IReadOnlyList<TerminalSegment>>? SegmentsAppended;

    /// <summary>ドラッグ&amp;ドロップ等で入力欄へ文字列を挿入してほしいときに発火する（仕様書19章）。</summary>
    public event Action<string>? InsertTextRequested;

    /// <summary>タブ切り替え時の再描画用スクロールバック。</summary>
    public IReadOnlyList<TerminalSegment> Buffer => _buffer;

    /// <summary>タブストリップでの選択表示用。<see cref="TerminalHostViewModel"/>が管理する。</summary>
    public bool IsActive
    {
        get => _isActive;
        internal set => SetProperty(ref _isActive, value);
    }

    public RelayCommand ToggleSyncCommand { get; }

    public bool IsSyncEnabled
    {
        get => _isSyncEnabled;
        set => SetProperty(ref _isSyncEnabled, value);
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

    /// <summary>画面で入力された1行を実行する。入力テキスト自体はシェルがエコーバック
    /// するため、ここでは画面へ書き戻さない（二重表示を避けるため）。</summary>
    public void SendInput(string command)
    {
        Start();

        if (!string.IsNullOrWhiteSpace(command))
        {
            _history.Remove(command);
            _history.Add(command);

            if (_history.Count > MaxHistory)
            {
                _history.RemoveAt(0);
            }
        }

        _historyIndex = _history.Count;
        _terminalService.SendCommand(command);
    }

    /// <summary>Ctrl+C相当の中断要求。</summary>
    public void Interrupt() => _terminalService.Interrupt();

    /// <summary>↑キー：1つ前のコマンド履歴。これ以上ない場合はnull。</summary>
    public string? MovePreviousHistory()
    {
        if (_history.Count == 0 || _historyIndex <= 0)
        {
            return null;
        }

        _historyIndex--;
        return _history[_historyIndex];
    }

    /// <summary>↓キー：1つ後のコマンド履歴。末尾を超えた場合は空文字（入力クリア）。</summary>
    public string? MoveNextHistory()
    {
        if (_history.Count == 0 || _historyIndex >= _history.Count)
        {
            return null;
        }

        _historyIndex++;
        return _historyIndex >= _history.Count ? string.Empty : _history[_historyIndex];
    }

    /// <summary>外部（SSH接続・Git操作・「ここでターミナルを開く」など）から生成したコマンドを実行する。</summary>
    public void SendRawCommand(string command)
    {
        Start();
        SendInput(command);
    }

    /// <summary>
    /// 仕様書44章：SSH接続時、Windows Credential Managerに保存済みのパスワードがあれば
    /// 自動入力する。出力に"password"（大文字小文字不問、"passphrase"とは区別）を含む行が
    /// 現れた最初の1回だけ、パスワードを（画面には表示せず）送信する。
    /// </summary>
    public void SendRawCommand(string command, string? password)
    {
        _pendingPassword = string.IsNullOrEmpty(password) ? null : password;
        SendRawCommand(command);
    }

    /// <summary>仕様書19章：ファイル・フォルダのドラッグ＆ドロップによるパス入力。</summary>
    public void InsertPathIntoInput(string path)
    {
        var quoted = path.Contains(' ') ? $"\"{path}\"" : path;
        InsertTextRequested?.Invoke(quoted);
    }

    private void AppendOutput(string text)
    {
        var segments = _ansiParser.Parse(text);
        if (segments.Count == 0)
        {
            return;
        }

        foreach (var segment in segments)
        {
            _buffer.Add(segment);
            _bufferChars += segment.Text.Length;
        }

        TrimBuffer();
        DetectPasswordPrompt(segments);
        SegmentsAppended?.Invoke(segments);
    }

    private void TrimBuffer()
    {
        while (_bufferChars > MaxBufferChars && _buffer.Count > 0)
        {
            _bufferChars -= _buffer[0].Text.Length;
            _buffer.RemoveAt(0);
        }
    }

    // 出力はチャンク単位で届くため、行が完成した時点でパスワードプロンプトかを判定する。
    private void DetectPasswordPrompt(IReadOnlyList<TerminalSegment> segments)
    {
        if (_pendingPassword is null)
        {
            return;
        }

        foreach (var segment in segments)
        {
            foreach (var c in segment.Text)
            {
                if (c == '\n')
                {
                    _currentLine.Clear();
                    continue;
                }

                _currentLine.Append(c);
            }
        }

        var line = _currentLine.ToString();

        if (line.Contains("password", StringComparison.OrdinalIgnoreCase) &&
            !line.Contains("passphrase", StringComparison.OrdinalIgnoreCase))
        {
            var password = _pendingPassword;
            _pendingPassword = null;
            _currentLine.Clear();
            _terminalService.SendCommand(password);
        }
    }

    public void Dispose() => _terminalService.Dispose();
}
