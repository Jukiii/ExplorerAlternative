using ExplorerAlternative.Mvvm;

namespace ExplorerAlternative.ViewModels;

/// <summary>空フォルダ検索（仕様書59章）の結果1行。削除候補チェックボックスを持つ。</summary>
public sealed class EmptyFolderRowViewModel : ObservableObject
{
    private bool _isSelected;

    public EmptyFolderRowViewModel(string fullPath)
    {
        FullPath = fullPath;
    }

    public string FullPath { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}
