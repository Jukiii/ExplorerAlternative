using System.Collections.ObjectModel;
using ExplorerAlternative.Mvvm;

namespace ExplorerAlternative.ViewModels;

/// <summary>
/// 一括名前変更ダイアログ（仕様書21章）。パターンまたは検索と置換のいずれかのモードで
/// 新しい名前を組み立て、大文字小文字変換・全角半角変換・Unicode正規化を追加で適用できる。
/// 入力を変更するたびに新しい名前のプレビューを再計算する。
/// </summary>
public sealed class BulkRenameViewModel : ObservableObject
{
    private BulkRenameMode _mode = BulkRenameMode.Pattern;
    private string _pattern = "{name}_{n}.{ext}";
    private string _findText = string.Empty;
    private string _replaceText = string.Empty;
    private bool _useRegex;
    private bool _caseSensitive;
    private CaseConversionMode _caseConversion = CaseConversionMode.None;
    private WidthConversionMode _widthConversion = WidthConversionMode.None;
    private bool _normalizeUnicode;

    public BulkRenameViewModel(IReadOnlyList<FileSystemNodeViewModel> targets)
    {
        Targets = targets;
        SetModeCommand = new RelayCommand(p => Mode = (BulkRenameMode)p!);
        SetCaseConversionCommand = new RelayCommand(p => CaseConversion = (CaseConversionMode)p!);
        SetWidthConversionCommand = new RelayCommand(p => WidthConversion = (WidthConversionMode)p!);
        RebuildPreview();
    }

    public IReadOnlyList<FileSystemNodeViewModel> Targets { get; }

    public ObservableCollection<BulkRenamePreviewItem> PreviewItems { get; } = new();

    public RelayCommand SetModeCommand { get; }

    public RelayCommand SetCaseConversionCommand { get; }

    public RelayCommand SetWidthConversionCommand { get; }

    public BulkRenameMode Mode
    {
        get => _mode;
        set
        {
            if (SetProperty(ref _mode, value))
            {
                RebuildPreview();
            }
        }
    }

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

    public string FindText
    {
        get => _findText;
        set
        {
            if (SetProperty(ref _findText, value))
            {
                RebuildPreview();
            }
        }
    }

    public string ReplaceText
    {
        get => _replaceText;
        set
        {
            if (SetProperty(ref _replaceText, value))
            {
                RebuildPreview();
            }
        }
    }

    public bool UseRegex
    {
        get => _useRegex;
        set
        {
            if (SetProperty(ref _useRegex, value))
            {
                RebuildPreview();
            }
        }
    }

    public bool CaseSensitive
    {
        get => _caseSensitive;
        set
        {
            if (SetProperty(ref _caseSensitive, value))
            {
                RebuildPreview();
            }
        }
    }

    public CaseConversionMode CaseConversion
    {
        get => _caseConversion;
        set
        {
            if (SetProperty(ref _caseConversion, value))
            {
                RebuildPreview();
            }
        }
    }

    public WidthConversionMode WidthConversion
    {
        get => _widthConversion;
        set
        {
            if (SetProperty(ref _widthConversion, value))
            {
                RebuildPreview();
            }
        }
    }

    public bool NormalizeUnicode
    {
        get => _normalizeUnicode;
        set
        {
            if (SetProperty(ref _normalizeUnicode, value))
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
            var originalName = Targets[i].Name;

            var newName = Mode == BulkRenameMode.Pattern
                ? RenamePatternExpander.Expand(Pattern, originalName, i)
                : RenamePatternExpander.ApplyFindReplace(originalName, FindText, ReplaceText, UseRegex, CaseSensitive);

            newName = RenamePatternExpander.ApplyTransforms(newName, CaseConversion, WidthConversion, NormalizeUnicode);

            PreviewItems.Add(new BulkRenamePreviewItem(originalName, newName));
        }
    }
}

public sealed record BulkRenamePreviewItem(string OriginalName, string NewName);
