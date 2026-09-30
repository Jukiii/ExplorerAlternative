using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ExplorerAlternative.Models;
using ExplorerAlternative.ViewModels;

namespace ExplorerAlternative.Views;

/// <summary>
/// ターミナル画面（<see cref="RichTextBox"/>）の描画とキー入力の仲介（仕様書17章）。
///
/// VS Codeの統合ターミナルと同じ「1つのターミナル画面」構造で、プロンプトはシェル自身の
/// 出力（"PS C:\...&gt; "）であり、その末尾にユーザーが直接入力する。入力行の編集状態と
/// 編集ロジックは<see cref="TerminalViewModel"/>（<see cref="TerminalInputBuffer"/>）が持ち、
/// このクラスは「ViewModelの状態を画面へ描く」「WPFのキー入力をViewModelの操作へ変換する」
/// ことだけを行う（CLAUDE.md 26章：Viewにロジックを書かない）。
///
/// RichTextBoxは読み取り専用として扱い、入力行の編集はすべてここで制御する。WPFの編集機能に
/// 任せると、出力の追記とユーザーの編集が競合して文書が壊れるため。
/// </summary>
public sealed class TerminalSurfaceController
{
    // 表示が重くならないよう、古い出力から間引く（スクロールバックの上限）。
    private const int MaxInlines = 4000;
    private const int TrimCount = 1000;

    private readonly RichTextBox _surface;
    private TerminalViewModel? _terminal;
    private Paragraph? _paragraph;
    private Run? _inputRun;

    public TerminalSurfaceController(RichTextBox surface)
    {
        _surface = surface;

        _surface.PreviewKeyDown += OnPreviewKeyDown;
        _surface.PreviewTextInput += OnPreviewTextInput;
        _surface.IsVisibleChanged += OnIsVisibleChanged;
        _surface.DragOver += OnDragOver;
        _surface.Drop += OnDrop;
    }

    /// <summary>ターミナル画面へキーボードフォーカスを移す。</summary>
    public void Focus() => _surface.Focus();

    /// <summary>タブ切り替え・ターミナル生成に追従して、画面を指定のターミナルへ張り替える。</summary>
    public void Bind(TerminalViewModel? terminal)
    {
        if (ReferenceEquals(_terminal, terminal))
        {
            return;
        }

        if (_terminal is not null)
        {
            _terminal.SegmentsAppended -= OnSegmentsAppended;
            _terminal.InputChanged -= OnInputChanged;
        }

        _terminal = terminal;

        _paragraph = new Paragraph { Margin = new Thickness(0) };
        _surface.Document = new FlowDocument(_paragraph)
        {
            PagePadding = new Thickness(0),
            FontFamily = _surface.FontFamily,
            FontSize = _surface.FontSize
        };

        _inputRun = new Run(terminal?.Input.Text ?? string.Empty);
        _paragraph.Inlines.Add(_inputRun);

        if (terminal is null)
        {
            return;
        }

        AppendSegments(terminal.Buffer);
        UpdateCaret();
        terminal.SegmentsAppended += OnSegmentsAppended;
        terminal.InputChanged += OnInputChanged;

        // 「＋」で新しいタブを作った直後や、タブを切り替えた直後に、そのまま入力できるようにする
        // （VS Codeと同じ）。ボタンやタブのクリックで奪われたフォーカスを画面へ戻す。
        // 非表示の間（起動時の初期バインド等）は、表示時の処理（OnIsVisibleChanged）に任せる。
        if (_surface.IsVisible)
        {
            FocusSoon();
        }
    }

    // クリック処理の完了後にフォーカスを移す（同じ処理内で移しても、ボタン側に奪い返されるため）。
    private void FocusSoon() =>
        _surface.Dispatcher.BeginInvoke(new Action(() => _surface.Focus()), DispatcherPriority.Input);

    private void OnSegmentsAppended(IReadOnlyList<TerminalSegment> segments) => AppendSegments(segments);

    private void OnInputChanged()
    {
        if (_inputRun is null || _terminal is null)
        {
            return;
        }

        _inputRun.Text = _terminal.Input.Text;
        UpdateCaret();
        _surface.ScrollToEnd();
    }

    // 出力は常に入力行の「前」へ挿入する（入力途中でも打ちかけの文字が消えないように）。
    private void AppendSegments(IReadOnlyList<TerminalSegment> segments)
    {
        if (_paragraph is null || _inputRun is null || segments.Count == 0)
        {
            return;
        }

        foreach (var segment in segments)
        {
            foreach (var inline in CreateInlines(segment))
            {
                _paragraph.Inlines.InsertBefore(_inputRun, inline);
            }
        }

        TrimScrollback();
        UpdateCaret();
        _surface.ScrollToEnd();
    }

    // FlowDocumentのRunは改行文字を改行として描画しないため、LineBreakへ分解する。
    private static IEnumerable<Inline> CreateInlines(TerminalSegment segment)
    {
        var lines = segment.Text.Split('\n');

        for (var i = 0; i < lines.Length; i++)
        {
            if (i > 0)
            {
                yield return new LineBreak();
            }

            if (lines[i].Length == 0)
            {
                continue;
            }

            var run = new Run(lines[i]);

            if (segment.Foreground is { } foreground)
            {
                run.Foreground = new SolidColorBrush(foreground);
            }

            if (segment.Background is { } background)
            {
                run.Background = new SolidColorBrush(background);
            }

            if (segment.IsBold)
            {
                run.FontWeight = FontWeights.Bold;
            }

            yield return run;
        }
    }

    private void TrimScrollback()
    {
        if (_paragraph is null || _paragraph.Inlines.Count <= MaxInlines)
        {
            return;
        }

        for (var i = 0; i < TrimCount && _paragraph.Inlines.Count > 1; i++)
        {
            var first = _paragraph.Inlines.FirstInline;
            if (first is null || ReferenceEquals(first, _inputRun))
            {
                break;
            }

            _paragraph.Inlines.Remove(first);
        }
    }

    private void UpdateCaret()
    {
        if (_inputRun is null || _terminal is null)
        {
            return;
        }

        var position = _inputRun.ContentStart.GetPositionAtOffset(_terminal.Input.Caret) ?? _inputRun.ContentEnd;
        _surface.CaretPosition = position;
    }

    private void OnPreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (_terminal is null || e.Text.Length == 0)
        {
            return;
        }

        // 制御文字（Enter・Tab等）はPreviewKeyDown側で扱う。
        if (!char.IsControl(e.Text[0]))
        {
            _terminal.InsertInput(e.Text);
            e.Handled = true;
        }
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_terminal is null)
        {
            return;
        }

        var ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);

        // Ctrl+C：選択中ならコピー、そうでなければ実行中コマンドの中断（VS Code/一般的な
        // ターミナルと同じ挙動）。Ctrl+VとCtrl+Aは通常どおり貼り付け・全選択として扱う。
        if (ctrl && e.Key == Key.C)
        {
            if (_surface.Selection.IsEmpty)
            {
                _terminal.InterruptAndClearInput();
                e.Handled = true;
            }

            return;
        }

        if (ctrl && e.Key == Key.V)
        {
            if (Clipboard.ContainsText())
            {
                _terminal.PasteText(Clipboard.GetText());
            }

            e.Handled = true;
            return;
        }

        if (ctrl && e.Key == Key.A)
        {
            return;
        }

        switch (e.Key)
        {
            // スペースはWPFの読み取り専用エディターがKeyDown段階で消費してしまい、
            // TextInputイベントが発生しない（英字はWM_CHAR経由で届くため影響を受けない）。
            // そのためここで明示的に入力へ反映する。
            case Key.Space:
                _terminal.InsertInput(" ");
                e.Handled = true;
                break;

            case Key.Enter:
                _terminal.SubmitInput();
                e.Handled = true;
                break;

            case Key.Back:
                _terminal.Backspace();
                e.Handled = true;
                break;

            case Key.Delete:
                _terminal.DeleteForward();
                e.Handled = true;
                break;

            case Key.Left:
                _terminal.MoveCaretLeft();
                e.Handled = true;
                break;

            case Key.Right:
                _terminal.MoveCaretRight();
                e.Handled = true;
                break;

            case Key.Home:
                _terminal.MoveCaretToStart();
                e.Handled = true;
                break;

            case Key.End:
                _terminal.MoveCaretToEnd();
                e.Handled = true;
                break;

            // ↑↓：コマンド履歴。
            case Key.Up:
                _terminal.RecallPreviousCommand();
                e.Handled = true;
                break;

            case Key.Down:
                _terminal.RecallNextCommand();
                e.Handled = true;
                break;

            // 画面内容を直接編集させない（入力行以外は読み取り専用扱い）。
            case Key.Tab:
                e.Handled = true;
                break;
        }
    }

    // ターミナルを表示した際、すぐに入力できるようフォーカスする。
    private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is not true)
        {
            return;
        }

        FocusSoon();
    }

    // 仕様書19章：ファイル・フォルダをターミナル画面へドラッグ＆ドロップするとパスが入力される。
    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (_terminal is null || !e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            return;
        }

        var paths = (string[])e.Data.GetData(DataFormats.FileDrop)!;

        foreach (var path in paths)
        {
            _terminal.InsertPathIntoInput(path);
        }

        e.Handled = true;
    }
}
