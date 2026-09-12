using ExplorerAlternative.Mvvm;

namespace ExplorerAlternative.ViewModels;

/// <summary>重複ファイル検索（仕様書58章）の結果1行。削除候補チェックボックスを持つ。</summary>
public sealed class DuplicateFileRowViewModel : ObservableObject
{
    private bool _isSelected;

    public DuplicateFileRowViewModel(string hash, long sizeBytes, string fullPath, bool isFirstInGroup)
    {
        Hash = hash;
        SizeBytes = sizeBytes;
        FullPath = fullPath;
        // グループの先頭（1件目）は既定でチェックを外し、「1つは残す」を分かりやすくする。
        _isSelected = !isFirstInGroup;
    }

    public string Hash { get; }

    public long SizeBytes { get; }

    public string FullPath { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}
