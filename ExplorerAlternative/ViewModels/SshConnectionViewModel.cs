using ExplorerAlternative.Models;
using ExplorerAlternative.Mvvm;

namespace ExplorerAlternative.ViewModels;

/// <summary>SSH接続ダイアログ（仕様書15章）の入力値。</summary>
public sealed class SshConnectionViewModel : ObservableObject
{
    private string _host = string.Empty;
    private string _port = "22";
    private string _userName = string.Empty;
    private string _identityFilePath = string.Empty;

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

        return new SshConnectionProfile
        {
            Host = Host.Trim(),
            Port = port,
            UserName = string.IsNullOrWhiteSpace(UserName) ? null : UserName.Trim(),
            IdentityFilePath = string.IsNullOrWhiteSpace(IdentityFilePath) ? null : IdentityFilePath.Trim()
        };
    }
}
