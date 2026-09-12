using System.Collections.ObjectModel;
using System.IO;
using ExplorerAlternative.Models;
using ExplorerAlternative.Mvvm;
using ExplorerAlternative.Services;
using ExplorerAlternative.Services.Abstractions;

namespace ExplorerAlternative.ViewModels;

/// <summary>
/// 検索ダイアログ（仕様書12章）。`Ctrl + F`で開き、現在フォルダ以下をファイル名・
/// フォルダ名・拡張子で検索する（将来のファイル内容検索拡張を妨げないよう、
/// 検索処理自体は<see cref="IFolderScanService"/>に切り出してある）。
/// </summary>
public sealed class SearchViewModel : ObservableObject
{
    private readonly IFolderScanService _scanService;
    private readonly IDialogService _dialogService;
    private CancellationTokenSource? _cts;
    private string _query = string.Empty;
    private bool _isSearching;
    private string _statusMessage = string.Empty;

    public SearchViewModel(string rootPath, IFolderScanService scanService, IDialogService dialogService)
    {
        RootPath = rootPath;
        _scanService = scanService;
        _dialogService = dialogService;

        SearchCommand = new RelayCommand(_ => RunSearch(), _ => !IsSearching && !string.IsNullOrWhiteSpace(Query));
        CancelCommand = new RelayCommand(_ => _cts?.Cancel(), _ => IsSearching);
        NavigateToResultCommand = new RelayCommand(p => NavigateToResult((FileSystemEntry)p!));
    }

    public string RootPath { get; }

    public string Query
    {
        get => _query;
        set
        {
            if (SetProperty(ref _query, value))
            {
                SearchCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsSearching
    {
        get => _isSearching;
        private set
        {
            if (SetProperty(ref _isSearching, value))
            {
                SearchCommand.RaiseCanExecuteChanged();
                CancelCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public ObservableCollection<FileSystemEntry> Results { get; } = new();

    public RelayCommand SearchCommand { get; }

    public RelayCommand CancelCommand { get; }

    public RelayCommand NavigateToResultCommand { get; }

    /// <summary>検索結果をダブルクリックした際、そのフォルダへ移動してもらうための通知。</summary>
    public event Action<string>? NavigateRequested;

    private async void RunSearch()
    {
        Results.Clear();
        StatusMessage = "検索中...";
        IsSearching = true;
        _cts = new CancellationTokenSource();

        try
        {
            var results = await _scanService.SearchAsync(RootPath, Query, _cts.Token);
            foreach (var entry in results)
            {
                Results.Add(entry);
            }

            StatusMessage = $"{Results.Count} 件見つかりました。";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "検索をキャンセルしました。";
        }
        catch (Exception ex) when (ex is AppOperationException or IOException or UnauthorizedAccessException)
        {
            StatusMessage = "検索中にエラーが発生しました。";
            _dialogService.ShowError(ex.Message);
        }
        finally
        {
            IsSearching = false;
        }
    }

    private void NavigateToResult(FileSystemEntry entry)
    {
        var folder = entry.IsDirectory ? entry.FullPath : Path.GetDirectoryName(entry.FullPath);
        if (!string.IsNullOrEmpty(folder))
        {
            NavigateRequested?.Invoke(folder);
        }
    }
}
