using ExplorerAlternative.Services.Abstractions;

namespace ExplorerAlternative.Services;

/// <summary>仕様書62章「操作履歴・GUI Undo」。アプリ全体で1つ共有するスタックとして扱う。</summary>
public sealed class UndoService : IUndoService
{
    private const int MaxHistory = 50;

    private sealed record Entry(string Description, Action Undo);

    private readonly List<Entry> _history = new();

    public bool CanUndo => _history.Count > 0;

    public string? NextUndoDescription => _history.Count > 0 ? _history[^1].Description : null;

    public event Action? Changed;

    public void Record(string description, Action undo)
    {
        _history.Add(new Entry(description, undo));

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
}
