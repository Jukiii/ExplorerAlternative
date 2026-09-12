using System.Collections.ObjectModel;
using ExplorerAlternative.Mvvm;

namespace ExplorerAlternative.ViewModels;

/// <summary>
/// コマンドパレット（仕様書46章）。`Ctrl + Shift + P`で開き、主要操作を
/// 名前で検索して実行する。実行対象は呼び出し側（MainWindowViewModel）が
/// 都度<see cref="CommandPaletteEntry.Execute"/>に現在の状態を解決するクロージャとして渡す
/// （パレットを開いた後にアクティブタブ/ペインが変わっても安全なようにするため）。
/// </summary>
public sealed class CommandPaletteViewModel : ObservableObject
{
    private readonly IReadOnlyList<CommandPaletteEntry> _allEntries;
    private string _query = string.Empty;
    private CommandPaletteEntry? _selectedCommand;

    public CommandPaletteViewModel(IReadOnlyList<CommandPaletteEntry> allEntries)
    {
        _allEntries = allEntries;
        ExecuteSelectedCommand = new RelayCommand(_ => ExecuteSelected());
        UpdateFiltered();
    }

    public string Query
    {
        get => _query;
        set
        {
            if (SetProperty(ref _query, value))
            {
                UpdateFiltered();
            }
        }
    }

    public ObservableCollection<CommandPaletteEntry> FilteredCommands { get; } = new();

    public CommandPaletteEntry? SelectedCommand
    {
        get => _selectedCommand;
        set => SetProperty(ref _selectedCommand, value);
    }

    public RelayCommand ExecuteSelectedCommand { get; }

    public event Action? RequestClose;

    public void ExecuteEntry(CommandPaletteEntry entry)
    {
        // 先にパレットを閉じてから実行する。設定ダイアログ等はShowDialog()でブロックするため、
        // 実行を先にすると対象ダイアログが閉じるまでパレットが裏に残ってしまう。
        RequestClose?.Invoke();
        entry.Execute();
    }

    private void ExecuteSelected()
    {
        if (SelectedCommand is not null)
        {
            ExecuteEntry(SelectedCommand);
        }
    }

    private void UpdateFiltered()
    {
        FilteredCommands.Clear();

        var candidates = string.IsNullOrWhiteSpace(Query)
            ? _allEntries
            : _allEntries.Where(e => e.Name.Contains(Query, StringComparison.OrdinalIgnoreCase));

        foreach (var entry in candidates.Where(e => e.CanExecute()))
        {
            FilteredCommands.Add(entry);
        }

        SelectedCommand = FilteredCommands.FirstOrDefault();
    }
}
