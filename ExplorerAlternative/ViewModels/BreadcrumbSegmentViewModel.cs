using System.Collections.ObjectModel;
using ExplorerAlternative.Mvvm;

namespace ExplorerAlternative.ViewModels;

/// <summary>
/// パンくずアドレスバー（仕様書7章）の1階層分。ドロップダウン（7.2章）用に
/// 同階層のフォルダ一覧を遅延読み込みできるようにしている。
/// </summary>
public sealed class BreadcrumbSegmentViewModel : ObservableObject
{
    private readonly Func<string, IReadOnlyList<(string Name, string Path)>> _loadChildren;
    private bool _isDropdownOpen;

    public BreadcrumbSegmentViewModel(string displayName, string path, Action<string> navigate, Func<string, IReadOnlyList<(string Name, string Path)>> loadChildren)
    {
        DisplayName = displayName;
        Path = path;
        NavigateAction = navigate;
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

        foreach (var (name, path) in _loadChildren(Path))
        {
            DropdownItems.Add(new BreadcrumbDropdownItem(name, path, NavigateAction));
        }
    }
}

public sealed class BreadcrumbDropdownItem
{
    public BreadcrumbDropdownItem(string name, string path, Action<string> navigateAction)
    {
        Name = name;
        Path = path;
        NavigateCommand = new RelayCommand(_ => navigateAction(path));
    }

    public string Name { get; }

    public string Path { get; }

    public RelayCommand NavigateCommand { get; }
}
