using System.Collections.ObjectModel;
using System.IO;
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
    /// <summary>プレビューのために、リモートのファイルをダウンロードしてよい最大の大きさ（画面が固まらないようにするため）。</summary>
    internal const long MaxPreviewBytes = 20L * 1024 * 1024;

    private readonly IDialogService _dialogService;
    private readonly Func<string, PreviewViewModel?>? _previewFactory;
    private readonly string _previewRoot;
    private readonly List<string> _previewFolders = new();
    private ISftpSession? _session;
    private string _currentPath = "/";
    private RemoteFileEntry? _selectedEntry;
    private string _statusMessage = string.Empty;
    private bool _isConnected;

    /// <param name="previewFactory">
    /// ダウンロードしたローカルのファイルから、プレビューを作る方法。省略すると、プレビューは使えない。
    /// </param>
    /// <param name="previewRoot">プレビュー用に一時的にダウンロードする先（省略時は、一時フォルダの下）。</param>
    public SftpBrowserViewModel(
        SshConnectionProfile profile,
        ISftpService sftpService,
        string? password,
        IDialogService dialogService,
        Func<string, PreviewViewModel?>? previewFactory = null,
        string? previewRoot = null)
    {
        Profile = profile;
        _dialogService = dialogService;
        _previewFactory = previewFactory;
        _previewRoot = previewRoot ?? Path.Combine(Path.GetTempPath(), "ExplorerAlternative", "sftp-preview");

        GoUpCommand = new RelayCommand(_ => NavigateUp(), _ => IsConnected && CurrentPath != "/");
        RefreshCommand = new RelayCommand(_ => Refresh(), _ => IsConnected);
        OpenSelectedCommand = new RelayCommand(_ => OpenSelected(), _ => IsConnected && SelectedEntry is { IsDirectory: true });
        UploadCommand = new RelayCommand(_ => RequestUpload(), _ => IsConnected);
        DownloadCommand = new RelayCommand(_ => RequestDownload(), _ => IsConnected && SelectedEntry is { IsDirectory: false });
        PreviewCommand = new RelayCommand(_ => ShowPreview(), _ => IsConnected && _previewFactory is not null && SelectedEntry is { IsDirectory: false });
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
                PreviewCommand.RaiseCanExecuteChanged();
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
                PreviewCommand.RaiseCanExecuteChanged();
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

    /// <summary>選択したリモートのファイルを、一時フォルダへダウンロードして、Quick Lookで表示する（仕様書44章「プレビュー」）。</summary>
    public RelayCommand PreviewCommand { get; }

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

    // 仕様書44章「プレビュー」：一時フォルダへダウンロードしてから、ローカルのファイルと同じ
    // プレビュー（画像・テキスト・Markdown・PDF等）で表示する。プレビューを閉じる・この画面を
    // 閉じるときに、ダウンロードした一時ファイルを消す。
    private void ShowPreview()
    {
        var entry = SelectedEntry;
        if (entry is null || entry.IsDirectory || _session is null || _previewFactory is null)
        {
            return;
        }

        if (entry.SizeBytes > MaxPreviewBytes)
        {
            _dialogService.ShowInfo($"「{entry.Name}」は大きい（{entry.SizeBytes / 1024 / 1024} MB）ため、プレビューできません。ダウンロードしてから開いてください。");
            return;
        }

        var folder = Path.Combine(_previewRoot, Guid.NewGuid().ToString("N"));

        try
        {
            Directory.CreateDirectory(folder);
            _previewFolders.Add(folder);
            _session.DownloadFile(entry.FullPath, folder);
        }
        catch (AppOperationException ex)
        {
            DeletePreviewFolder(folder);
            _dialogService.ShowError(ex.Message);
            return;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            DeletePreviewFolder(folder);
            _dialogService.ShowError($"プレビュー用のファイルを保存できませんでした。({ex.Message})");
            return;
        }

        var localPath = Path.Combine(folder, entry.Name);
        var preview = File.Exists(localPath) ? _previewFactory(localPath) : null;

        if (preview is null)
        {
            _dialogService.ShowError($"「{entry.Name}」をプレビューできませんでした。");
            DeletePreviewFolder(folder);
            return;
        }

        preview.RequestClose = () =>
        {
            _dialogService.ClosePreview();
            preview.CancelPendingWork();
            DeletePreviewFolder(folder);
        };
        preview.Closed = () =>
        {
            preview.CancelPendingWork();
            DeletePreviewFolder(folder);
        };

        _dialogService.ShowPreview(preview);
    }

    private void DeletePreviewFolder(string folder)
    {
        _previewFolders.Remove(folder);

        try
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // まだ別のアプリが開いている等で消せなくても、一時フォルダなので、処理は続ける。
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

        foreach (var folder in _previewFolders.ToList())
        {
            DeletePreviewFolder(folder);
        }
    }
}
