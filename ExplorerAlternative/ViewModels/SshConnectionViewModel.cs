using ExplorerAlternative.Models;
using ExplorerAlternative.Mvvm;

namespace ExplorerAlternative.ViewModels;

/// <summary>SSH接続プロファイルの追加・編集ダイアログ（仕様書44章）の入力値。</summary>
public sealed class SshConnectionViewModel : ObservableObject
{
    private string _id = Guid.NewGuid().ToString("N");
    private string _displayName = string.Empty;
    private string _host = string.Empty;
    private string _port = "22";
    private string _userName = string.Empty;
    private string _identityFilePath = string.Empty;

    public static SshConnectionViewModel Create() => new();

    public static SshConnectionViewModel FromProfile(SshConnectionProfile profile) => new()
    {
        _id = profile.Id,
        DisplayName = profile.DisplayName,
        Host = profile.Host,
        Port = profile.Port.ToString(),
        UserName = profile.UserName ?? string.Empty,
        IdentityFilePath = profile.IdentityFilePath ?? string.Empty
    };

    public string DisplayName
    {
        get => _displayName;
        set => SetProperty(ref _displayName, value);
    }

    public string Host
    {
        get => _host;
        set => SetProperty(ref _host, value);
    }

    public string Port
    {
        get => _port;
        set => SetProperty(ref _port, value);
    }

    public string UserName
    {
        get => _userName;
        set => SetProperty(ref _userName, value);
    }

    public string IdentityFilePath
    {
        get => _identityFilePath;
        set => SetProperty(ref _identityFilePath, value);
    }

    public SshConnectionProfile ToProfile()
    {
        var port = int.TryParse(Port, out var parsed) && parsed > 0 ? parsed : 22;
        var host = Host.Trim();

        return new SshConnectionProfile
        {
            Id = _id,
            DisplayName = string.IsNullOrWhiteSpace(DisplayName) ? host : DisplayName.Trim(),
            Host = host,
            Port = port,
            UserName = string.IsNullOrWhiteSpace(UserName) ? null : UserName.Trim(),
            IdentityFilePath = string.IsNullOrWhiteSpace(IdentityFilePath) ? null : IdentityFilePath.Trim()
        };
    }
}
