using System.Collections.ObjectModel;
using ExplorerAlternative.Models;
using ExplorerAlternative.Mvvm;
using ExplorerAlternative.Services;
using ExplorerAlternative.Services.Abstractions;

namespace ExplorerAlternative.ViewModels;

/// <summary>
/// 仕様書44章「SFTPリモートファイル操作」。SSH接続プロファイルからSFTPセッションを
/// 確立し、ローカルのドライブと同じ感覚でリモートフォルダを閲覧・転送できるようにする。
/// </summary>
public sealed class SftpBrowserViewModel : ObservableObject, IDisposable
{
    private readonly IDialogService _dialogService;
    private ISftpSession? _session;
    private string _currentPath = "/";
    private RemoteFileEntry? _selectedEntry;
    private string _statusMessage = string.Empty;
    private bool _isConnected;

    public SftpBrowserViewModel(SshConnectionProfile profile, ISftpService sftpService, string? password, IDialogService dialogService)
    {
        Profile = profile;
        _dialogService = dialogService;

        GoUpCommand = new RelayCommand(_ => NavigateUp(), _ => IsConnected && CurrentPath != "/");
        RefreshCommand = new RelayCommand(_ => Refresh(), _ => IsConnected);
        OpenSelectedCommand = new RelayCommand(_ => OpenSelected(), _ => IsConnected && SelectedEntry is { IsDirectory: true });
        UploadCommand = new RelayCommand(_ => RequestUpload(), _ => IsConnected);
        DownloadCommand = new RelayCommand(_ => RequestDownload(), _ => IsConnected && SelectedEntry is { IsDirectory: false });
        NewFolderCommand = new RelayCommand(_ => CreateFolder(), _ => IsConnected);
        RenameCommand = new RelayCommand(_ => Rename(), _ => IsConnected && SelectedEntry is not null);
        DeleteCommand = new RelayCommand(_ => Delete(), _ => IsConnected && SelectedEntry is not null);

        Connect(sftpService, password);
    }

    public SshConnectionProfile Profile { get; }

    public ObservableCollection<RemoteFileEntry> Entries { get; } = new();

    /// <summary>アップロードするローカルファイルのパスを、View（ダイアログ表示）へ依頼する。</summary>
    public event Func<string?>? RequestLocalFileForUpload;

    /// <summary>ダウンロード先のローカルフォルダを、View（ダイアログ表示）へ依頼する。</summary>
    public event Func<string?>? RequestLocalFolderForDownload;

    public string CurrentPath
    {
        get => _currentPath;
        private set
        {
            if (SetProperty(ref _currentPath, value))
            {
                GoUpCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public RemoteFileEntry? SelectedEntry
    {
        get => _selectedEntry;
        set
        {
            if (SetProperty(ref _selectedEntry, value))
            {
                OpenSelectedCommand.RaiseCanExecuteChanged();
                DownloadCommand.RaiseCanExecuteChanged();
                RenameCommand.RaiseCanExecuteChanged();
                DeleteCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public bool IsConnected
    {
        get => _isConnected;
        private set
        {
            if (SetProperty(ref _isConnected, value))
            {
                GoUpCommand.RaiseCanExecuteChanged();
                RefreshCommand.RaiseCanExecuteChanged();
                OpenSelectedCommand.RaiseCanExecuteChanged();
                UploadCommand.RaiseCanExecuteChanged();
                DownloadCommand.RaiseCanExecuteChanged();
                NewFolderCommand.RaiseCanExecuteChanged();
                RenameCommand.RaiseCanExecuteChanged();
                DeleteCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public RelayCommand GoUpCommand { get; }

    public RelayCommand RefreshCommand { get; }

    public RelayCommand OpenSelectedCommand { get; }

    public RelayCommand UploadCommand { get; }

    public RelayCommand DownloadCommand { get; }

    public RelayCommand NewFolderCommand { get; }

    public RelayCommand RenameCommand { get; }

    public RelayCommand DeleteCommand { get; }

    private void Connect(ISftpService sftpService, string? password)
    {
        try
        {
            StatusMessage = "接続中...";
            _session = sftpService.Connect(Profile, password);
            CurrentPath = _session.HomeDirectory;
            IsConnected = true;
            Refresh();
        }
        catch (AppOperationException ex)
        {
            StatusMessage = "接続に失敗しました。";
            _dialogService.ShowError(ex.Message);
        }
    }

    private void Refresh()
    {
        if (_session is null)
        {
            return;
        }

        try
        {
            Entries.Clear();
            foreach (var entry in _session.ListDirectory(CurrentPath))
            {
                Entries.Add(entry);
            }

            StatusMessage = $"{Entries.Count} 件";
        }
        catch (AppOperationException ex)
        {
            _dialogService.ShowError(ex.Message);
        }
    }

    private void OpenSelected()
    {
        if (SelectedEntry is { IsDirectory: true } entry)
        {
            CurrentPath = entry.FullPath;
            Refresh();
        }
    }

    private void NavigateUp()
    {
        if (CurrentPath == "/")
        {
            return;
        }

        var lastSlash = CurrentPath.TrimEnd('/').LastIndexOf('/');
        CurrentPath = lastSlash <= 0 ? "/" : CurrentPath[..lastSlash];
        Refresh();
    }

    private void RequestUpload()
    {
        var localPath = RequestLocalFileForUpload?.Invoke();
        if (string.IsNullOrEmpty(localPath) || _session is null)
        {
            return;
        }

        try
        {
            _session.UploadFile(localPath, CurrentPath);
            Refresh();
        }
        catch (AppOperationException ex)
        {
            _dialogService.ShowError(ex.Message);
        }
    }

    private void RequestDownload()
    {
        var entry = SelectedEntry;
        if (entry is null || _session is null)
        {
            return;
        }

        var localFolder = RequestLocalFolderForDownload?.Invoke();
        if (string.IsNullOrEmpty(localFolder))
        {
            return;
        }

        try
        {
            _session.DownloadFile(entry.FullPath, localFolder);
            StatusMessage = $"「{entry.Name}」をダウンロードしました。";
        }
        catch (AppOperationException ex)
        {
            _dialogService.ShowError(ex.Message);
        }
    }

    private void CreateFolder()
    {
        if (_session is null)
        {
            return;
        }

        var name = _dialogService.PromptText("新しいフォルダ", "フォルダ名を入力してください。");
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        try
        {
            _session.CreateDirectory(CurrentPath.EndsWith('/') ? $"{CurrentPath}{name}" : $"{CurrentPath}/{name}");
            Refresh();
        }
        catch (AppOperationException ex)
        {
            _dialogService.ShowError(ex.Message);
        }
    }

    private void Rename()
    {
        var entry = SelectedEntry;
        if (entry is null || _session is null)
        {
            return;
        }

        var newName = _dialogService.PromptText("名前の変更", "新しい名前を入力してください。", entry.Name);
        if (string.IsNullOrWhiteSpace(newName) || newName == entry.Name)
        {
            return;
        }

        try
        {
            _session.Rename(entry.FullPath, newName);
            Refresh();
        }
        catch (AppOperationException ex)
        {
            _dialogService.ShowError(ex.Message);
        }
    }

    private void Delete()
    {
        var entry = SelectedEntry;
        if (entry is null || _session is null)
        {
            return;
        }

        if (!_dialogService.Confirm($"「{entry.Name}」を削除します。よろしいですか？"))
        {
            return;
        }

        try
        {
            _session.Delete(entry.FullPath, entry.IsDirectory);
            Refresh();
        }
        catch (AppOperationException ex)
        {
            _dialogService.ShowError(ex.Message);
        }
    }

    public void Dispose()
    {
        _session?.Dispose();
    }
}
