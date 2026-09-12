using System.Collections.ObjectModel;
using ExplorerAlternative.Models;
using ExplorerAlternative.Mvvm;
using ExplorerAlternative.Services;
using ExplorerAlternative.Services.Abstractions;

namespace ExplorerAlternative.ViewModels;

/// <summary>
/// メインペイン階層表示（仕様書4章）の1ノード。フォルダは "&gt;" / "v" の記号で展開状態を表す。
/// </summary>
public sealed class FileSystemNodeViewModel : ObservableObject
{
    private readonly IFileSystemService _fileSystemService;
    private readonly IDialogService _dialogService;
    private readonly ISettingsService _settingsService;
    private readonly Action? _onTreeChanged;
    private bool _isExpanded;
    private bool _isSelected;
    private bool _childrenLoaded;

    public FileSystemNodeViewModel(
        FileSystemEntry entry,
        int depth,
        IFileSystemService fileSystemService,
        IDialogService dialogService,
        ISettingsService settingsService,
        Action? onTreeChanged = null)
    {
        Entry = entry;
        Depth = depth;
        _fileSystemService = fileSystemService;
        _dialogService = dialogService;
        _settingsService = settingsService;
        _onTreeChanged = onTreeChanged;

        if (entry.IsDirectory)
        {
            Children = new ObservableCollection<FileSystemNodeViewModel>();
        }
    }

    public FileSystemEntry Entry { get; }

    public string Name => Entry.Name;

    public string FullPath => Entry.FullPath;

    public bool IsDirectory => Entry.IsDirectory;

    public int Depth { get; }

    public long? SizeBytes => Entry.SizeBytes;

    public DateTime? LastModified => Entry.LastModified;

    public DateTime? Created => Entry.Created;

    public ObservableCollection<FileSystemNodeViewModel>? Children { get; }

    /// <summary>仕様書4.2章：折りたたみは "&gt;"、展開は "v"。+/-は使用しない。</summary>
    public string ExpandSymbol => IsDirectory ? (IsExpanded ? "v" : ">") : string.Empty;

    /// <summary>一覧表示用のアイコン（フォルダ/開いたフォルダ/ファイル）。</summary>
    public string IconGlyph => IsDirectory ? (IsExpanded ? "📂" : "📁") : "📄";

    public string SizeDisplay => IsDirectory || SizeBytes is null ? string.Empty : FormatSize(SizeBytes.Value);

    public string LastModifiedDisplay => LastModified?.ToString("yyyy/MM/dd HH:mm") ?? string.Empty;

    /// <summary>仕様書6.2章：ファイル・フォルダに設定されたタグ。</summary>
    public IReadOnlyList<string> Tags =>
        (IReadOnlyList<string>?)_settingsService.Current.TagAssignments
            .FirstOrDefault(a => string.Equals(a.Path, FullPath, StringComparison.OrdinalIgnoreCase))
            ?.Tags
        ?? Array.Empty<string>();

    public bool HasTags => Tags.Count > 0;

    public string TagsDisplay => HasTags ? $"🏷 {string.Join(", ", Tags)}" : string.Empty;

    /// <summary>タグの付与・解除後、表示を更新するために呼び出す（ノードの再生成は不要）。</summary>
    public void RaiseTagsChanged()
    {
        OnPropertyChanged(nameof(Tags));
        OnPropertyChanged(nameof(HasTags));
        OnPropertyChanged(nameof(TagsDisplay));
    }

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (SetProperty(ref _isExpanded, value))
            {
                OnPropertyChanged(nameof(ExpandSymbol));
                OnPropertyChanged(nameof(IconGlyph));

                if (value && IsDirectory && !_childrenLoaded)
                {
                    LoadChildren();
                }

                _onTreeChanged?.Invoke();
            }
        }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public void LoadChildren()
    {
        if (Children is null)
        {
            return;
        }

        Children.Clear();

        try
        {
            var entries = _fileSystemService.GetChildren(FullPath)
                .OrderByDescending(e => e.IsDirectory)
                .ThenBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase);

            foreach (var entry in entries)
            {
                Children.Add(new FileSystemNodeViewModel(entry, Depth + 1, _fileSystemService, _dialogService, _settingsService, _onTreeChanged));
            }

            _childrenLoaded = true;
        }
        catch (AppOperationException ex)
        {
            _dialogService.ShowError(ex.Message);
        }
    }

    public void Refresh()
    {
        _childrenLoaded = false;

        if (IsExpanded)
        {
            LoadChildren();
        }
    }

    private static string FormatSize(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double size = bytes;
        var unitIndex = 0;

        while (size >= 1024 && unitIndex < units.Length - 1)
        {
            size /= 1024;
            unitIndex++;
        }

        return unitIndex == 0 ? $"{size:0} {units[unitIndex]}" : $"{size:0.#} {units[unitIndex]}";
    }
}
