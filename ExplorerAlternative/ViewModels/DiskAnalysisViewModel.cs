using System.Collections.ObjectModel;
using System.IO;
using ExplorerAlternative.Models;
using ExplorerAlternative.Mvvm;
using ExplorerAlternative.Services;
using ExplorerAlternative.Services.Abstractions;

namespace ExplorerAlternative.ViewModels;

/// <summary>
/// ディスク解析ダイアログ（仕様書38章「巨大ファイル検索」・58章「重複ファイル検索」・
/// 59章「空フォルダ検索」）。3つの検索は走査対象・バックグラウンド実行・キャンセルの
/// 仕組みを共有するため1つのダイアログにまとめている。
/// </summary>
public sealed class DiskAnalysisViewModel : ObservableObject
{
    private readonly IFolderScanService _scanService;
    private readonly IFileSystemService _fileSystemService;
    private readonly IDialogService _dialogService;
    private CancellationTokenSource? _cts;
    private DiskAnalysisMode _mode = DiskAnalysisMode.LargeFiles;
    private string _targetPath;
    private string _minSizeMb = "500";
    private bool _isRunning;
    private string _statusMessage = string.Empty;

    public DiskAnalysisViewModel(string initialTargetPath, IFolderScanService scanService, IFileSystemService fileSystemService, IDialogService dialogService)
    {
        _targetPath = initialTargetPath;
        _scanService = scanService;
        _fileSystemService = fileSystemService;
        _dialogService = dialogService;

        RunCommand = new RelayCommand(_ => Run(), _ => !IsRunning && Directory.Exists(TargetPath));
        CancelCommand = new RelayCommand(_ => _cts?.Cancel(), _ => IsRunning);
        DeleteSelectedCommand = new RelayCommand(_ => DeleteSelected(), _ => !IsRunning && HasSelection());
        SetModeCommand = new RelayCommand(p => Mode = (DiskAnalysisMode)p!);
    }

    public RelayCommand SetModeCommand { get; }

    public DiskAnalysisMode Mode
    {
        get => _mode;
        set => SetProperty(ref _mode, value);
    }

    public string TargetPath
    {
        get => _targetPath;
        set
        {
            if (SetProperty(ref _targetPath, value))
            {
                RunCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string MinSizeMb
    {
        get => _minSizeMb;
        set => SetProperty(ref _minSizeMb, value);
    }

    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            if (SetProperty(ref _isRunning, value))
            {
                RunCommand.RaiseCanExecuteChanged();
                CancelCommand.RaiseCanExecuteChanged();
                DeleteSelectedCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public ObservableCollection<LargeFileResult> LargeFiles { get; } = new();

    public ObservableCollection<DuplicateFileRowViewModel> DuplicateFiles { get; } = new();

    public ObservableCollection<EmptyFolderRowViewModel> EmptyFolders { get; } = new();

    public RelayCommand RunCommand { get; }

    public RelayCommand CancelCommand { get; }

    public RelayCommand DeleteSelectedCommand { get; }

    private bool HasSelection() =>
        DuplicateFiles.Any(f => f.IsSelected) || EmptyFolders.Any(f => f.IsSelected);

    private async void Run()
    {
        LargeFiles.Clear();
        DuplicateFiles.Clear();
        EmptyFolders.Clear();
        StatusMessage = "検索中...";
        IsRunning = true;
        _cts = new CancellationTokenSource();

        try
        {
            switch (Mode)
            {
                case DiskAnalysisMode.LargeFiles:
                    await RunLargeFilesAsync(_cts.Token);
                    break;
                case DiskAnalysisMode.Duplicates:
                    await RunDuplicatesAsync(_cts.Token);
                    break;
                case DiskAnalysisMode.EmptyFolders:
                    await RunEmptyFoldersAsync(_cts.Token);
                    break;
            }
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "キャンセルしました。";
        }
        catch (Exception ex) when (ex is AppOperationException or IOException or UnauthorizedAccessException)
        {
            StatusMessage = "検索中にエラーが発生しました。";
            _dialogService.ShowError(ex.Message);
        }
        finally
        {
            IsRunning = false;
        }
    }

    private async Task RunLargeFilesAsync(CancellationToken token)
    {
        var minBytes = (long)(ParseMinSizeMb() * 1024 * 1024);
        var results = await _scanService.FindLargeFilesAsync(TargetPath, minBytes, token);
        foreach (var result in results)
        {
            LargeFiles.Add(result);
        }

        StatusMessage = $"{LargeFiles.Count} 件見つかりました。";
    }

    private async Task RunDuplicatesAsync(CancellationToken token)
    {
        var groups = await _scanService.FindDuplicateFilesAsync(TargetPath, token);
        foreach (var group in groups)
        {
            for (var i = 0; i < group.Paths.Count; i++)
            {
                // 各グループの先頭は既定で削除候補から外す（「1つは残す」を分かりやすくするため）。
                DuplicateFiles.Add(new DuplicateFileRowViewModel(group.Hash, group.SizeBytes, group.Paths[i], isFirstInGroup: i == 0));
            }
        }

        StatusMessage = $"{groups.Count} 組の重複グループが見つかりました。";
    }

    private async Task RunEmptyFoldersAsync(CancellationToken token)
    {
        var results = await _scanService.FindEmptyFoldersAsync(TargetPath, token);
        foreach (var path in results)
        {
            EmptyFolders.Add(new EmptyFolderRowViewModel(path));
        }

        StatusMessage = $"{EmptyFolders.Count} 件見つかりました。";
    }

    private double ParseMinSizeMb() => double.TryParse(MinSizeMb, out var value) && value > 0 ? value : 500;

    // 仕様書58章・59章：削除は必ず確認する。
    private void DeleteSelected()
    {
        var duplicatesToDelete = DuplicateFiles.Where(f => f.IsSelected).ToList();
        var foldersToDelete = EmptyFolders.Where(f => f.IsSelected).ToList();
        var total = duplicatesToDelete.Count + foldersToDelete.Count;

        if (total == 0)
        {
            return;
        }

        if (!_dialogService.Confirm($"選択した{total}件を削除します。この操作は元に戻せません。よろしいですか？"))
        {
            return;
        }

        foreach (var file in duplicatesToDelete)
        {
            try
            {
                _fileSystemService.Delete(new[] { file.FullPath });
                DuplicateFiles.Remove(file);
            }
            catch (AppOperationException ex)
            {
                _dialogService.ShowError(ex.Message);
            }
        }

        foreach (var folder in foldersToDelete)
        {
            try
            {
                _fileSystemService.Delete(new[] { folder.FullPath });
                EmptyFolders.Remove(folder);
            }
            catch (AppOperationException ex)
            {
                _dialogService.ShowError(ex.Message);
            }
        }
    }
}
