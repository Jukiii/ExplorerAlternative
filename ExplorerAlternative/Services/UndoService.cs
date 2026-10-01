using ExplorerAlternative.Services.Abstractions;

namespace ExplorerAlternative.Services;

/// <summary>仕様書62章「操作履歴・GUI Undo」。アプリ全体で1つ共有するスタックとして扱う。</summary>
public sealed class UndoService : IUndoService
{
    private const int MaxHistory = 50;

    private sealed record Entry(Guid Id, string Description, Action Undo, Action? Redo);

    // 元に戻せる操作（古い順）と、元に戻した結果やり直せる操作（最後に戻したものが末尾）。
    private readonly List<Entry> _history = new();
    private readonly List<Entry> _redoStack = new();

    public bool CanUndo => _history.Count > 0;

    public string? NextUndoDescription => _history.Count > 0 ? _history[^1].Description : null;

    public bool CanRedo => _redoStack.Count > 0;

    public string? NextRedoDescription => _redoStack.Count > 0 ? _redoStack[^1].Description : null;

    public IReadOnlyList<UndoHistoryEntry> History =>
        _history.Select(e => new UndoHistoryEntry(e.Id, e.Description)).ToList();

    public event Action? Changed;

    public void Record(string description, Action undo, Action? redo = null)
    {
        AddToHistory(new Entry(Guid.NewGuid(), description, undo, redo));

        // 新しい操作を行ったら、それまで「やり直せた」操作は、状態が合わなくなるため無効にする
        // （一般的なUndo/Redoと同じ）。
        _redoStack.Clear();

        Changed?.Invoke();
    }

    public void Undo()
    {
        if (_history.Count == 0)
        {
            return;
        }

        var entry = _history[^1];
        _history.RemoveAt(_history.Count - 1);

        try
        {
            entry.Undo();
            PushToRedoStack(entry);
        }
        catch
        {
            // 戻せなかった操作があると、状態が想定と変わるため、やり直しの履歴は信用できない。
            _redoStack.Clear();
            throw;
        }
        finally
        {
            Changed?.Invoke();
        }
    }

    public void Redo()
    {
        if (_redoStack.Count == 0)
        {
            return;
        }

        var entry = _redoStack[^1];
        _redoStack.RemoveAt(_redoStack.Count - 1);

        try
        {
            // Redoが無い操作は、そもそもRedoスタックに積まない（PushToRedoStack）。
            entry.Redo!();

            // やり直した操作は、再び元に戻せる操作として履歴へ戻す（やり直しの履歴は消さない）。
            AddToHistory(entry);
        }
        catch
        {
            _redoStack.Clear();
            throw;
        }
        finally
        {
            Changed?.Invoke();
        }
    }

    public IReadOnlyList<string> UndoTo(Guid id)
    {
        var index = _history.FindIndex(e => e.Id == id);
        if (index < 0)
        {
            return Array.Empty<string>();
        }

        // 依存関係（例：移動してから名前変更した場合、名前変更を先に戻す必要がある）を
        // 崩さないよう、最新の操作から順に戻す。
        var failed = new List<string>();
        var toUndo = _history.Skip(index).Reverse().ToList();
        _history.RemoveRange(index, _history.Count - index);

        try
        {
            foreach (var entry in toUndo)
            {
                try
                {
                    entry.Undo();

                    // 失敗した操作があると、その影響が残ったままになり、それ以降に戻した操作の
                    // やり直しは、状態と合わなくなる。失敗以降は、やり直し履歴に積まない。
                    if (failed.Count == 0)
                    {
                        PushToRedoStack(entry);
                    }
                }
                catch (AppOperationException)
                {
                    failed.Add(entry.Description);
                    _redoStack.Clear();
                }
            }
        }
        finally
        {
            Changed?.Invoke();
        }

        return failed;
    }

    private void AddToHistory(Entry entry)
    {
        _history.Add(entry);

        if (_history.Count > MaxHistory)
        {
            _history.RemoveAt(0);
        }
    }

    // 元に戻した操作を、やり直せる操作として積む。やり直し処理が無い操作を戻した場合は、それより
    // あとに戻した操作（時間的には先に行った操作）のやり直しが、状態と合わなくなるため、全体を捨てる。
    private void PushToRedoStack(Entry entry)
    {
        if (entry.Redo is null)
        {
            _redoStack.Clear();
            return;
        }

        _redoStack.Add(entry);
    }
}
