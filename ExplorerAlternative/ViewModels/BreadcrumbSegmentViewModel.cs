using System.Collections.ObjectModel;
using ExplorerAlternative.Mvvm;

namespace ExplorerAlternative.ViewModels;

/// <summary>
/// パンくずアドレスバー（仕様書7章・11章）の1階層分。ドロップダウン（11章）には、
/// この階層自身の子ではなく親フォルダの子（＝同階層の兄弟フォルダ）を表示する
/// （例：「aa」のドロップダウンには C:\ の中身である aa/ab/ac/Documents が並ぶ）。
/// </summary>
public sealed class BreadcrumbSegmentViewModel : ObservableObject
{
    private readonly Func<string, IReadOnlyList<(string Name, string Path)>> _loadChildren;
    private readonly string? _parentPath;
    private readonly string _remainder;
    private readonly Action<string, string> _navigatePartial;
    private bool _isDropdownOpen;

    public BreadcrumbSegmentViewModel(
        string displayName,
        string path,
        string? parentPath,
        string remainder,
        Action<string> navigate,
        Action<string, string> navigatePartial,
        Func<string, IReadOnlyList<(string Name, string Path)>> loadChildren)
    {
        DisplayName = displayName;
        Path = path;
        _parentPath = parentPath;
        _remainder = remainder;
        NavigateAction = navigate;
        _navigatePartial = navigatePartial;
        _loadChildren = loadChildren;
        NavigateCommand = new RelayCommand(_ => navigate(Path));
    }

    public string DisplayName { get; }

    public string Path { get; }

    public Action<string> NavigateAction { get; }

    public RelayCommand NavigateCommand { get; }

    public ObservableCollection<BreadcrumbDropdownItem> DropdownItems { get; } = new();

    public bool IsDropdownOpen
    {
        get => _isDropdownOpen;
        set
        {
            if (SetProperty(ref _isDropdownOpen, value) && value)
            {
                PopulateDropdown();
            }
        }
    }

    private void PopulateDropdown()
    {
        DropdownItems.Clear();

        // 「PC」セグメント自身は親を持たないため、その場合のみ自分自身の子（＝ドライブ一覧）を表示する。
        var basisPath = _parentPath ?? Path;

        foreach (var (name, path) in _loadChildren(basisPath))
        {
            DropdownItems.Add(new BreadcrumbDropdownItem(name, path, _remainder, NavigateAction, _navigatePartial, CloseDropdown));
        }
    }

    private void CloseDropdown() => IsDropdownOpen = false;
}

/// <summary>
/// パンくずドロップダウンの1項目（仕様書11章）。
/// 左クリックはパス全体を置き換え、右クリックは現階層より下のパスを維持したまま
/// この項目のパスへ差し替える（下層が存在しない場合は安全にそのフォルダへ移動する）。
/// </summary>
public sealed class BreadcrumbDropdownItem
{
    public BreadcrumbDropdownItem(
        string name,
        string path,
        string remainder,
        Action<string> navigateAction,
        Action<string, string> navigatePartialAction,
        Action closeDropdown)
    {
        Name = name;
        Path = path;
        NavigateCommand = new RelayCommand(_ =>
        {
            navigateAction(path);
            closeDropdown();
        });
        NavigatePartialCommand = new RelayCommand(_ =>
        {
            navigatePartialAction(path, remainder);
            closeDropdown();
        });
    }

    public string Name { get; }

    public string Path { get; }

    public RelayCommand NavigateCommand { get; }

    public RelayCommand NavigatePartialCommand { get; }
}
