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
/// </summary>
public sealed class SshProfilesViewModel : ObservableObject
{
    private readonly ISettingsService _settingsService;
    private readonly IDialogService _dialogService;
    private readonly ISshService _sshService;
    private SshConnectionProfile? _selectedProfile;

    public SshProfilesViewModel(ISettingsService settingsService, IDialogService dialogService, ISshService sshService)
    {
        _settingsService = settingsService;
        _dialogService = dialogService;
        _sshService = sshService;

        foreach (var profile in settingsService.Current.SshProfiles)
        {
            Profiles.Add(profile);
        }

        AddCommand = new RelayCommand(_ => Add());
        EditCommand = new RelayCommand(_ => Edit(), _ => SelectedProfile is not null);
        DeleteCommand = new RelayCommand(_ => Delete(), _ => SelectedProfile is not null);
        ConnectCommand = new RelayCommand(_ => Connect(), _ => SelectedProfile is not null);
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

    /// <summary>「接続」実行時に、組み立てたsshコマンドを呼び出し側へ通知する。</summary>
    public event Action<string>? RequestConnect;

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
        Save();
    }

    private void Edit()
    {
        var target = SelectedProfile;
        if (target is null)
        {
            return;
        }

        var editViewModel = SshConnectionViewModel.FromProfile(target);
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
            RequestConnect?.Invoke(command);
            RequestClose?.Invoke();
        }
        catch (AppOperationException ex)
        {
            _dialogService.ShowError(ex.Message);
        }
    }

    private void Save()
    {
        _settingsService.Current.SshProfiles = Profiles.ToList();
        _settingsService.Save();
    }
}
