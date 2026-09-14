using System.Collections.ObjectModel;
using System.IO;
using ExplorerAlternative.Models;
using ExplorerAlternative.Mvvm;
using ExplorerAlternative.Services;
using ExplorerAlternative.Services.Abstractions;

namespace ExplorerAlternative.ViewModels;

/// <summary>
/// 仕様書45章「フォルダ同期」。2つのフォルダを比較し、差分を確認したうえで
/// 片方向（またはコマンドを両方向に使うことで双方向）にコピーで同期する。
/// 既存ファイルを不用意に削除しないよう、削除操作は行わない（コピーのみ）。
/// </summary>
public sealed class FolderCompareViewModel : ObservableObject
{
    private readonly IFolderCompareService _folderCompareService;
    private readonly IFileSystemService _fileSystemService;
    private readonly IDialogService _dialogService;
    private CancellationTokenSource? _cts;
    private string _leftFolder;
    private string _rightFolder;
    private bool _isRunning;
    private string _statusMessage = string.Empty;

    public FolderCompareViewModel(
        string initialLeftFolder,
        string initialRightFolder,
        IFolderCompareService folderCompareService,
        IFileSystemService fileSystemService,
        IDialogService dialogService)
    {
        _leftFolder = initialLeftFolder;
        _rightFolder = initialRightFolder;
        _folderCompareService = folderCompareService;
        _fileSystemService = fileSystemService;
        _dialogService = dialogService;

        CompareCommand = new RelayCommand(_ => Compare(), _ => !IsRunning && Directory.Exists(LeftFolder) && Directory.Exists(RightFolder));
        CopyToRightCommand = new RelayCommand(_ => CopySelected(toRight: true), _ => !IsRunning && HasSelection());
        CopyToLeftCommand = new RelayCommand(_ => CopySelected(toRight: false), _ => !IsRunning && HasSelection());
        SelectAllCommand = new RelayCommand(_ => SetAllSelected(true), _ => Entries.Count > 0);
        SelectNoneCommand = new RelayCommand(_ => SetAllSelected(false), _ => Entries.Count > 0);
    }

    public string LeftFolder
    {
        get => _leftFolder;
        set
        {
            if (SetProperty(ref _leftFolder, value))
            {
                CompareCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string RightFolder
    {
        get => _rightFolder;
        set
        {
            if (SetProperty(ref _rightFolder, value))
            {
                CompareCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            if (SetProperty(ref _isRunning, value))
            {
                CompareCommand.RaiseCanExecuteChanged();
                CopyToRightCommand.RaiseCanExecuteChanged();
                CopyToLeftCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public ObservableCollection<FolderCompareRowViewModel> Entries { get; } = new();

    public RelayCommand CompareCommand { get; }

    public RelayCommand CopyToRightCommand { get; }

    public RelayCommand CopyToLeftCommand { get; }

    public RelayCommand SelectAllCommand { get; }

    public RelayCommand SelectNoneCommand { get; }

    private bool HasSelection() => Entries.Any(e => e.IsSelected);

    private async void Compare()
    {
        Entries.Clear();
        StatusMessage = "比較中...";
        IsRunning = true;
        _cts = new CancellationTokenSource();

        try
        {
            var results = await _folderCompareService.CompareAsync(LeftFolder, RightFolder, _cts.Token);
            foreach (var entry in results.Where(e => e.Status != FolderCompareStatus.Same))
            {
                Entries.Add(new FolderCompareRowViewModel(entry));
            }

            StatusMessage = Entries.Count == 0
                ? "差分はありません（2つのフォルダは一致しています）。"
                : $"{Entries.Count} 件の差分が見つかりました。";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "キャンセルしました。";
        }
        finally
        {
            IsRunning = false;
            CopyToRightCommand.RaiseCanExecuteChanged();
            CopyToLeftCommand.RaiseCanExecuteChanged();
            SelectAllCommand.RaiseCanExecuteChanged();
            SelectNoneCommand.RaiseCanExecuteChanged();
        }
    }

    // 仕様書45章「片方向同期」「双方向同期」：選択項目をコピーする。方向を逆に選んで
    // 2回実行すれば双方向の同期になる。削除は行わない（既存ファイルを不用意に消さないため）。
    private void CopySelected(bool toRight)
    {
        var targets = Entries.Where(e => e.IsSelected).ToList();
        if (targets.Count == 0)
        {
            return;
        }

        var direction = toRight ? "左 → 右" : "右 → 左";
        if (!_dialogService.Confirm($"選択した{targets.Count}件を{direction}へコピーします。よろしいですか？\n（既存ファイルは上書きされます。削除は行いません。）"))
        {
            return;
        }

        foreach (var row in targets)
        {
            var entry = row.Entry;
            var sourcePath = toRight ? entry.LeftFullPath : entry.RightFullPath;
            if (sourcePath is null)
            {
                // コピー元がそもそも存在しない（例：右のみに存在する項目を左→右にコピーしようとした場合）。
                continue;
            }

            var destinationRoot = toRight ? RightFolder : LeftFolder;
            var destinationPath = Path.Combine(destinationRoot, entry.RelativePath);

            try
            {
                _fileSystemService.CopyFileTo(sourcePath, destinationPath);
            }
            catch (AppOperationException ex)
            {
                _dialogService.ShowError(ex.Message);
            }
        }

        Compare();
    }

    private void SetAllSelected(bool value)
    {
        foreach (var entry in Entries)
        {
            entry.IsSelected = value;
        }
    }
}

public sealed class FolderCompareRowViewModel : ObservableObject
{
    private bool _isSelected;

    public FolderCompareRowViewModel(FolderCompareEntry entry)
    {
        Entry = entry;
        _isSelected = true;
    }

    public FolderCompareEntry Entry { get; }

    public string RelativePath => Entry.RelativePath;

    public FolderCompareStatus Status => Entry.Status;

    public string StatusDisplay => Entry.Status switch
    {
        FolderCompareStatus.OnlyLeft => "左のみ",
        FolderCompareStatus.OnlyRight => "右のみ",
        FolderCompareStatus.Different => "内容が異なる",
        _ => "同一"
    };

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}
