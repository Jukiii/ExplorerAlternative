using System.IO;
using System.Text;
using System.Text.RegularExpressions;
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
    private readonly ITabCompletionService? _tabCompletionService;

    // 直近のプロンプト（"PS C:\dir> "）から読み取った、このターミナルの現在のフォルダ（Tab補完の基準）。
    private static readonly Regex PromptPattern = new(@"^PS (?<dir>.+)> ?$", RegexOptions.Compiled);
    private readonly StringBuilder _lastLine = new();
    private string? _currentDirectory;
    private TabCompletionCycle? _completionCycle;
    private int _completionGeneration;

    private int _bufferChars;
    private int _historyIndex;
    private bool _isSyncEnabled;
    private bool _isActive;
    private string? _pendingPassword;

    /// <param name="tabCompletionService">Tab補完の候補を求めるサービス。省略すると、Tabを押しても何も起きない。</param>
    /// <param name="initialDirectory">起動直後の現在のフォルダ（最初のプロンプトが出るまでの、Tab補完の基準）。</param>
    public TerminalViewModel(
        IPowerShellTerminalService terminalService,
        bool syncByDefault,
        string name,
        ITabCompletionService? tabCompletionService = null,
        string? initialDirectory = null)
    {
        _terminalService = terminalService;
        _isSyncEnabled = syncByDefault;
        Name = name;
        _tabCompletionService = tabCompletionService;
        _currentDirectory = initialDirectory;

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

    /// <summary>このターミナルの現在のフォルダ（直近のプロンプトから読み取った値。不明なら<c>null</c>）。</summary>
    public string? CurrentDirectory => _currentDirectory;

    public void SyncCurrentDirectory(string path)
    {
        if (IsSyncEnabled && _terminalService.IsRunning)
        {
            _terminalService.ChangeDirectory(path);
            _currentDirectory = path;
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

    // ===== Tab補完（仕様書17章） =====

    /// <summary>
    /// Tab（<paramref name="backwards"/>がtrueならShift+Tab）：カーソル位置の単語を補完する。
    /// 候補が複数ある場合は、PowerShellと同じく、押すたびに次の候補（Shift+Tabなら前の候補）へ入れ替える。
    /// 候補を求めている間に入力が変わった場合は、その結果を捨てる。
    /// </summary>
    public async Task CompleteTabAsync(bool backwards = false)
    {
        if (_tabCompletionService is null)
        {
            return;
        }

        // 直前のTabで入れた候補のまま（入力もカーソルも動かしていない）なら、次の候補へ進める。
        if (_completionCycle is { } cycle && Input.Text == cycle.AppliedText && Input.Caret == cycle.AppliedCaret)
        {
            cycle.MoveNext(backwards);
            ApplyCompletion(cycle);
            return;
        }

        _completionCycle = null;
        var generation = ++_completionGeneration;
        var text = Input.Text;
        var caret = Input.Caret;

        TabCompletionResult? result;

        try
        {
            result = await _tabCompletionService.CompleteAsync(text, caret, _currentDirectory, CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException)
        {
            AppendOutput($"{Environment.NewLine}[エラー] Tab補完に失敗しました。({ex.Message}){Environment.NewLine}");
            return;
        }

        if (result is null || result.Matches.Count == 0)
        {
            return;
        }

        // 待っている間に、別のTabや入力があった場合は、古い結果で上書きしない。
        if (generation != _completionGeneration || Input.Text != text || Input.Caret != caret)
        {
            return;
        }

        var newCycle = new TabCompletionCycle(text, result, backwards);
        _completionCycle = newCycle;
        ApplyCompletion(newCycle);
    }

    private void ApplyCompletion(TabCompletionCycle cycle)
    {
        cycle.Apply();
        Input.Set(cycle.AppliedText, cycle.AppliedCaret);
        InputChanged?.Invoke();
    }

    /// <summary>Tab補完の、いま入れている候補の位置（押すたびに次へ進む）。</summary>
    private sealed class TabCompletionCycle
    {
        private readonly string _originalText;
        private readonly int _index;
        private readonly int _length;
        private readonly IReadOnlyList<string> _matches;
        private int _current;

        public TabCompletionCycle(string originalText, TabCompletionResult result, bool startFromLast)
        {
            _originalText = originalText;
            _index = Math.Clamp(result.ReplacementIndex, 0, originalText.Length);
            _length = Math.Clamp(result.ReplacementLength, 0, originalText.Length - _index);
            _matches = result.Matches;
            _current = startFromLast ? _matches.Count - 1 : 0;
            Apply();
        }

        public string AppliedText { get; private set; } = string.Empty;

        public int AppliedCaret { get; private set; }

        public void MoveNext(bool backwards)
        {
            _current = (_current + (backwards ? -1 : 1) + _matches.Count) % _matches.Count;
            Apply();
        }

        public void Apply()
        {
            var match = _matches[_current];
            AppliedText = string.Concat(_originalText.AsSpan(0, _index), match, _originalText.AsSpan(_index + _length));
            AppliedCaret = _index + match.Length;
        }
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
            if (segment.IsLineReset)
            {
                // 行の上書き（プログレスバー等）。スクロールバックには印を残さず、現在の行を消す。
                RemoveCurrentLineFromBuffer();
                continue;
            }

            _buffer.Add(segment);
            _bufferChars += segment.Text.Length;
        }

        TrimBuffer();
        TrackPromptDirectory(segments);
        DetectPasswordPrompt(segments);

        // 画面側へは、行リセットの印を含めて元の順序のまま渡す（画面の現在の行も消すため）。
        SegmentsAppended?.Invoke(segments);
    }

    // スクロールバックの末尾にある、まだ改行で終わっていない行（現在の行）を取り除く。
    private void RemoveCurrentLineFromBuffer()
    {
        while (_buffer.Count > 0)
        {
            var last = _buffer[^1];
            var lastLineFeed = last.Text.LastIndexOf('\n');

            if (lastLineFeed < 0)
            {
                // 改行を含まない断片は、まるごと現在の行。
                _bufferChars -= last.Text.Length;
                _buffer.RemoveAt(_buffer.Count - 1);
                continue;
            }

            // 改行より後ろだけを取り除き、改行までは残す。
            var keepLength = lastLineFeed + 1;
            if (keepLength < last.Text.Length)
            {
                _bufferChars -= last.Text.Length - keepLength;
                _buffer[^1] = new TerminalSegment
                {
                    Text = last.Text[..keepLength],
                    Foreground = last.Foreground,
                    Background = last.Background,
                    IsBold = last.IsBold
                };
            }

            break;
        }
    }

    private void TrimBuffer()
    {
        while (_bufferChars > MaxBufferChars && _buffer.Count > 0)
        {
            _bufferChars -= _buffer[0].Text.Length;
            _buffer.RemoveAt(0);
        }
    }

    // 出力の最後の行が"PS C:\dir> "というプロンプトなら、そのフォルダを現在のフォルダとして覚える
    // （Tab補完の基準。cdで移動したあとの場所も追えるようにする）。フォルダとして存在しない場合
    // （レジストリ等のPowerShellドライブ）は、無視する。
    private void TrackPromptDirectory(IReadOnlyList<TerminalSegment> segments)
    {
        foreach (var segment in segments)
        {
            if (segment.IsLineReset)
            {
                _lastLine.Clear();
                continue;
            }

            foreach (var c in segment.Text)
            {
                if (c == '\n')
                {
                    _lastLine.Clear();
                }
                else if (c != '\r')
                {
                    _lastLine.Append(c);
                }
            }
        }

        var match = PromptPattern.Match(_lastLine.ToString());
        if (match.Success && Directory.Exists(match.Groups["dir"].Value))
        {
            _currentDirectory = match.Groups["dir"].Value;
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
            if (segment.IsLineReset)
            {
                _currentLine.Clear();
                continue;
            }

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
