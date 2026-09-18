using System.Collections.ObjectModel;
using ExplorerAlternative.Services.Abstractions;
using ExplorerAlternative.Mvvm;

namespace ExplorerAlternative.ViewModels;

/// <summary>
/// 操作履歴ダイアログ（仕様書30章「GUI Undo履歴」）。IUndoServiceの記録を一覧表示し、
/// 選択した地点までさかのぼって元に戻せるようにする。
/// </summary>
public sealed class UndoHistoryViewModel : ObservableObject
{
    private readonly IUndoService _undoService;
    private readonly IDialogService _dialogService;

    public UndoHistoryViewModel(IUndoService undoService, IDialogService dialogService)
    {
        _undoService = undoService;
        _dialogService = dialogService;

        Refresh();
        _undoService.Changed += OnUndoServiceChanged;

        UndoToCommand = new RelayCommand(p => UndoTo((UndoHistoryEntry)p!));
    }

    /// <summary>古い順（実行順）。「戻したい地点」＝この項目とそれ以降すべてが元に戻る。</summary>
    public ObservableCollection<UndoHistoryEntry> Entries { get; } = new();

    public bool HasEntries => Entries.Count > 0;

    public RelayCommand UndoToCommand { get; }

    /// <summary>ダイアログを閉じる際にイベント購読を解除する。</summary>
    public void Detach() => _undoService.Changed -= OnUndoServiceChanged;

    private void OnUndoServiceChanged() => Refresh();

    private void Refresh()
    {
        Entries.Clear();
        foreach (var entry in _undoService.History)
        {
            Entries.Add(entry);
        }

        OnPropertyChanged(nameof(HasEntries));
    }

    private void UndoTo(UndoHistoryEntry entry)
    {
        var index = Entries.IndexOf(entry);
        if (index < 0)
        {
            return;
        }

        var count = Entries.Count - index;
        if (!_dialogService.Confirm($"「{entry.Description}」を含む、以降の{count}件の操作をすべて元に戻します。よろしいですか？"))
        {
            return;
        }

        var failed = _undoService.UndoTo(entry.Id);
        if (failed.Count > 0)
        {
            _dialogService.ShowError($"以下の操作は元に戻せませんでした：\n{string.Join("\n", failed)}");
        }
    }
}
