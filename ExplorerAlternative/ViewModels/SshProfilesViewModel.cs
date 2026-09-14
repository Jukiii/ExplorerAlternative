using System.Collections.ObjectModel;
using ExplorerAlternative.Models;
using ExplorerAlternative.Mvvm;
using ExplorerAlternative.Services;
using ExplorerAlternative.Services.Abstractions;

namespace ExplorerAlternative.ViewModels;

/// <summary>
/// SSH接続の登録・管理ダイアログ（仕様書44章「SSH接続先を登録・管理」）。
/// 接続実行そのものはRequestConnectで呼び出し側（MainWindowViewModel）へ委譲し、
/// 統合ターミナル（9章）上でOpenSSHクライアントを起動してもらう。
/// パスワードはsettings.jsonに含めず、Windows Credential Manager
/// （<see cref="ISshCredentialStore"/>）にのみ保存する。
/// </summary>
public sealed class SshProfilesViewModel : ObservableObject
{
    private readonly ISettingsService _settingsService;
    private readonly IDialogService _dialogService;
    private readonly ISshService _sshService;
    private readonly ISshCredentialStore _credentialStore;
    private SshConnectionProfile? _selectedProfile;

    public SshProfilesViewModel(
        ISettingsService settingsService,
        IDialogService dialogService,
        ISshService sshService,
        ISshCredentialStore credentialStore)
    {
        _settingsService = settingsService;
        _dialogService = dialogService;
        _sshService = sshService;
        _credentialStore = credentialStore;

        foreach (var profile in settingsService.Current.SshProfiles)
        {
            Profiles.Add(profile);
        }

        AddCommand = new RelayCommand(_ => Add());
        EditCommand = new RelayCommand(_ => Edit(), _ => SelectedProfile is not null);
        DeleteCommand = new RelayCommand(_ => Delete(), _ => SelectedProfile is not null);
        ConnectCommand = new RelayCommand(_ => Connect(), _ => SelectedProfile is not null);
        ConnectSftpCommand = new RelayCommand(_ => ConnectSftp(), _ => SelectedProfile is not null);
    }

    public ObservableCollection<SshConnectionProfile> Profiles { get; } = new();

    public SshConnectionProfile? SelectedProfile
    {
        get => _selectedProfile;
        set
        {
            if (SetProperty(ref _selectedProfile, value))
            {
                EditCommand.RaiseCanExecuteChanged();
                DeleteCommand.RaiseCanExecuteChanged();
                ConnectCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public RelayCommand AddCommand { get; }

    public RelayCommand EditCommand { get; }

    public RelayCommand DeleteCommand { get; }

    public RelayCommand ConnectCommand { get; }

    /// <summary>仕様書44章「SFTPリモートファイル操作」：SFTPブラウザを開く。</summary>
    public RelayCommand ConnectSftpCommand { get; }

    /// <summary>「接続」実行時に、組み立てたsshコマンドと（保存されていれば）パスワードを呼び出し側へ通知する。</summary>
    public event Action<string, string?>? RequestConnect;

    /// <summary>「SFTPで開く」実行時に、対象プロファイルと（保存されていれば）パスワードを呼び出し側へ通知する。</summary>
    public event Action<SshConnectionProfile, string?>? RequestSftpBrowser;

    public event Action? RequestClose;

    private void Add()
    {
        var editViewModel = SshConnectionViewModel.Create();
        if (!_dialogService.ShowSshConnection(editViewModel))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(editViewModel.Host))
        {
            _dialogService.ShowError("接続先ホストを入力してください。");
            return;
        }

        var profile = editViewModel.ToProfile();
        Profiles.Add(profile);
        SelectedProfile = profile;

        if (!string.IsNullOrEmpty(editViewModel.Password))
        {
            _credentialStore.SavePassword(profile.Id, editViewModel.Password);
        }

        Save();
    }

    private void Edit()
    {
        var target = SelectedProfile;
        if (target is null)
        {
            return;
        }

        var hasStoredPassword = _credentialStore.TryGetPassword(target.Id) is not null;
        var editViewModel = SshConnectionViewModel.FromProfile(target, hasStoredPassword);
        if (!_dialogService.ShowSshConnection(editViewModel))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(editViewModel.Host))
        {
            _dialogService.ShowError("接続先ホストを入力してください。");
            return;
        }

        var index = Profiles.IndexOf(target);
        var updated = editViewModel.ToProfile();
        Profiles[index] = updated;
        SelectedProfile = updated;

        if (editViewModel.ClearStoredPassword)
        {
            _credentialStore.DeletePassword(updated.Id);
        }

        if (!string.IsNullOrEmpty(editViewModel.Password))
        {
            _credentialStore.SavePassword(updated.Id, editViewModel.Password);
        }

        Save();
    }

    private void Delete()
    {
        var target = SelectedProfile;
        if (target is null)
        {
            return;
        }

        if (!_dialogService.Confirm($"「{target.DisplayName}」を削除します。よろしいですか？"))
        {
            return;
        }

        Profiles.Remove(target);
        SelectedProfile = null;
        _credentialStore.DeletePassword(target.Id);
        Save();
    }

    private void Connect()
    {
        var target = SelectedProfile;
        if (target is null)
        {
            return;
        }

        try
        {
            var command = _sshService.BuildConnectCommand(target);
            var password = _credentialStore.TryGetPassword(target.Id);
            RequestConnect?.Invoke(command, password);
            RequestClose?.Invoke();
        }
        catch (AppOperationException ex)
        {
            _dialogService.ShowError(ex.Message);
        }
    }

    private void ConnectSftp()
    {
        var target = SelectedProfile;
        if (target is null)
        {
            return;
        }

        var password = _credentialStore.TryGetPassword(target.Id);
        RequestSftpBrowser?.Invoke(target, password);
        RequestClose?.Invoke();
    }

    private void Save()
    {
        _settingsService.Current.SshProfiles = Profiles.ToList();
        _settingsService.Save();
    }
}
