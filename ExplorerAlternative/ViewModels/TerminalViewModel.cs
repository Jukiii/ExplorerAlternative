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
/// ターミナル画面」として扱う。ここではシェルプロセス・スクロールバック（色付き断片）・
/// コマンド履歴・入力行の編集状態（<see cref="Input"/>）を担当し、画面（RichTextBox）への
/// 描画とキー入力の受け取りはView側（<c>TerminalSurfaceController</c>）が担当する。
/// 入力行の状態をViewModelが持つため、タブを切り替えても打ちかけの入力は保持される。
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

    /// <summary>入力行の内容またはカーソル位置が変わったときに発火する（View側が入力行を再描画する）。</summary>
    public event Action? InputChanged;

    /// <summary>プロンプトの後ろに打ちかけているコマンド（編集中の1行）。</summary>
    public TerminalInputBuffer Input { get; } = new();

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

    // ===== 入力行の編集（キー操作に対応する。仕様書17章） =====

    /// <summary>カーソル位置へ文字列を挿入する（文字入力・貼り付け・ドラッグ&amp;ドロップ）。</summary>
    public void InsertInput(string text)
    {
        if (Input.Insert(text))
        {
            InputChanged?.Invoke();
        }
    }

    /// <summary>
    /// クリップボードの文字列を挿入する。複数行の貼り付けは改行を空白に潰す
    /// （誤って複数のコマンドが実行されないように）。
    /// </summary>
    public void PasteText(string text) => InsertInput(NormalizePastedText(text));

    public static string NormalizePastedText(string text) =>
        text.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ');

    public void Backspace() => RaiseIfChanged(Input.Backspace());

    public void DeleteForward() => RaiseIfChanged(Input.DeleteForward());

    public void MoveCaretLeft() => RaiseIfChanged(Input.MoveLeft());

    public void MoveCaretRight() => RaiseIfChanged(Input.MoveRight());

    public void MoveCaretToStart() => RaiseIfChanged(Input.MoveToStart());

    public void MoveCaretToEnd() => RaiseIfChanged(Input.MoveToEnd());

    /// <summary>
    /// Enter：入力行を確定して実行する。入力テキストはシェルがエコーバックするため、
    /// 画面の入力行は先に空にしてから送る（残したままだと同じ行が二重に表示される）。
    /// </summary>
    public void SubmitInput()
    {
        var command = Input.Take();
        InputChanged?.Invoke();
        SendInput(command);
    }

    /// <summary>Ctrl+C（選択なし）：実行中のコマンドを中断し、打ちかけの入力も捨てる。</summary>
    public void InterruptAndClearInput()
    {
        Interrupt();
        Input.Set(string.Empty);
        InputChanged?.Invoke();
    }

    /// <summary>↑キー：1つ前のコマンド履歴を入力行へ呼び出す。これ以上ない場合は何もしない。</summary>
    public void RecallPreviousCommand()
    {
        if (_history.Count == 0 || _historyIndex <= 0)
        {
            return;
        }

        _historyIndex--;
        SetInput(_history[_historyIndex]);
    }

    /// <summary>↓キー：1つ後のコマンド履歴を呼び出す。末尾を超えた場合は入力行を空にする。</summary>
    public void RecallNextCommand()
    {
        if (_history.Count == 0 || _historyIndex >= _history.Count)
        {
            return;
        }

        _historyIndex++;
        SetInput(_historyIndex >= _history.Count ? string.Empty : _history[_historyIndex]);
    }

    private void SetInput(string text)
    {
        Input.Set(text);
        InputChanged?.Invoke();
    }

    private void RaiseIfChanged(bool changed)
    {
        if (changed)
        {
            InputChanged?.Invoke();
        }
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
        InsertInput(quoted);
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
