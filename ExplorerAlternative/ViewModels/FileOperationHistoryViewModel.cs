using System.Collections.ObjectModel;
using ExplorerAlternative.Models;
using ExplorerAlternative.Mvvm;
using ExplorerAlternative.Services.Abstractions;

namespace ExplorerAlternative.ViewModels;

/// <summary>
/// ファイル操作履歴ダイアログ（仕様書31章）。IFileOperationHistoryServiceに永続化された
/// 操作履歴を一覧表示し、検索・削除に対応する。
/// </summary>
public sealed class FileOperationHistoryViewModel : ObservableObject
{
    private readonly IFileOperationHistoryService _historyService;
    private readonly IDialogService _dialogService;
    private string _searchText = string.Empty;

    public FileOperationHistoryViewModel(IFileOperationHistoryService historyService, IDialogService dialogService)
    {
        _historyService = historyService;
        _dialogService = dialogService;

        Refresh();

        RemoveCommand = new RelayCommand(p => Remove((FileOperationHistoryEntry)p!));
        ClearCommand = new RelayCommand(_ => Clear(), _ => Entries.Count > 0);
    }

    public ObservableCollection<FileOperationHistoryEntry> Entries { get; } = new();

    public bool HasEntries => Entries.Count > 0;

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                Refresh();
            }
        }
    }

    public RelayCommand RemoveCommand { get; }

    public RelayCommand ClearCommand { get; }

    private void Refresh()
    {
        Entries.Clear();

        var all = _historyService.GetAll();
        var filtered = string.IsNullOrWhiteSpace(SearchText)
            ? all
            : all.Where(e =>
                e.Target.Contains(SearchText, StringComparison.CurrentCultureIgnoreCase) ||
                e.Operation.Contains(SearchText, StringComparison.CurrentCultureIgnoreCase) ||
                (e.OriginalLocation?.Contains(SearchText, StringComparison.CurrentCultureIgnoreCase) ?? false) ||
                (e.Destination?.Contains(SearchText, StringComparison.CurrentCultureIgnoreCase) ?? false));

        foreach (var entry in filtered)
        {
            Entries.Add(entry);
        }

        OnPropertyChanged(nameof(HasEntries));
        ClearCommand.RaiseCanExecuteChanged();
    }

    private void Remove(FileOperationHistoryEntry entry)
    {
        _historyService.Remove(entry);
        Refresh();
    }

    private void Clear()
    {
        if (!_dialogService.Confirm("ファイル操作履歴をすべて削除しますか？"))
        {
            return;
        }

        _historyService.Clear();
        Refresh();
    }
}
