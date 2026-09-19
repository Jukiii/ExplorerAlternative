using ExplorerAlternative.Services.Abstractions;

namespace ExplorerAlternative.Services;

/// <summary>仕様書62章「操作履歴・GUI Undo」。アプリ全体で1つ共有するスタックとして扱う。</summary>
public sealed class UndoService : IUndoService
{
    private const int MaxHistory = 50;

    private sealed record Entry(Guid Id, string Description, Action Undo);

    private readonly List<Entry> _history = new();

    public bool CanUndo => _history.Count > 0;

    public string? NextUndoDescription => _history.Count > 0 ? _history[^1].Description : null;

    public IReadOnlyList<UndoHistoryEntry> History =>
        _history.Select(e => new UndoHistoryEntry(e.Id, e.Description)).ToList();

    public event Action? Changed;

    public void Record(string description, Action undo)
    {
        _history.Add(new Entry(Guid.NewGuid(), description, undo));

        if (_history.Count > MaxHistory)
        {
            _history.RemoveAt(0);
        }

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
                }
                catch (AppOperationException)
                {
                    failed.Add(entry.Description);
                }
            }
        }
        finally
        {
            Changed?.Invoke();
        }

        return failed;
    }
}
