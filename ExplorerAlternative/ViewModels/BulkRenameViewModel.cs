using System.Collections.ObjectModel;
using ExplorerAlternative.Mvvm;

namespace ExplorerAlternative.ViewModels;

/// <summary>
/// 一括名前変更ダイアログ（仕様書21章）。パターンを変更するたびに新しい名前のプレビューを再計算する。
/// </summary>
public sealed class BulkRenameViewModel : ObservableObject
{
    private string _pattern = "{name}_{n}.{ext}";

    public BulkRenameViewModel(IReadOnlyList<FileSystemNodeViewModel> targets)
    {
        Targets = targets;
        RebuildPreview();
    }

    public IReadOnlyList<FileSystemNodeViewModel> Targets { get; }

    public ObservableCollection<BulkRenamePreviewItem> PreviewItems { get; } = new();

    public string Pattern
    {
        get => _pattern;
        set
        {
            if (SetProperty(ref _pattern, value))
            {
                RebuildPreview();
            }
        }
    }

    private void RebuildPreview()
    {
        PreviewItems.Clear();

        for (var i = 0; i < Targets.Count; i++)
        {
            var newName = RenamePatternExpander.Expand(Pattern, Targets[i].Name, i);
            PreviewItems.Add(new BulkRenamePreviewItem(Targets[i].Name, newName));
        }
    }
}

public sealed record BulkRenamePreviewItem(string OriginalName, string NewName);
