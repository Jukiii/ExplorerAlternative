using System.IO;
using ExplorerAlternative.Models;
using ExplorerAlternative.Services.Abstractions;
using Renci.SshNet;
using Renci.SshNet.Common;

namespace ExplorerAlternative.Services;

/// <summary>仕様書44章「SFTPリモートファイル操作」の実装。SSH.NET（Renci.SshNet）を使用する。
/// パスフレーズ付き秘密鍵には対応しない（パスフレーズはどこにも保存しない方針のため）。</summary>
public sealed class SftpService : ISftpService
{
    public ISftpSession Connect(SshConnectionProfile profile, string? password)
    {
        var authMethods = new List<AuthenticationMethod>();

        if (!string.IsNullOrWhiteSpace(profile.IdentityFilePath) && File.Exists(profile.IdentityFilePath))
        {
            try
            {
                var keyFile = new PrivateKeyFile(profile.IdentityFilePath);
                authMethods.Add(new PrivateKeyAuthenticationMethod(profile.UserName ?? Environment.UserName, keyFile));
            }
            catch (Exception ex) when (ex is SshException or UnauthorizedAccessException or IOException)
            {
                // パスフレーズ付き等、読み込めない鍵は無視してパスワード認証にフォールバックする。
            }
        }

        if (!string.IsNullOrEmpty(password))
        {
            authMethods.Add(new PasswordAuthenticationMethod(profile.UserName ?? Environment.UserName, password));
        }

        if (authMethods.Count == 0)
        {
            throw new AppOperationException(
                "SFTP接続に使える認証情報がありません。パスワードを保存するか、パスフレーズなしの秘密鍵を指定してください。");
        }

        var connectionInfo = new ConnectionInfo(profile.Host, profile.Port, profile.UserName ?? Environment.UserName, authMethods.ToArray());
        var client = new SftpClient(connectionInfo);

        try
        {
            client.Connect();
        }
        catch (Exception ex) when (ex is SshException or System.Net.Sockets.SocketException or TimeoutException)
        {
            client.Dispose();
            throw new AppOperationException($"SFTP接続に失敗しました：{ex.Message}", ex);
        }

        return new SftpSession(client);
    }

    private sealed class SftpSession : ISftpSession
    {
        private readonly SftpClient _client;

        public SftpSession(SftpClient client)
        {
            _client = client;
            HomeDirectory = client.WorkingDirectory;
        }

        public string HomeDirectory { get; }

        public IReadOnlyList<RemoteFileEntry> ListDirectory(string remotePath)
        {
            try
            {
                return _client.ListDirectory(remotePath)
                    .Where(entry => entry.Name is not "." and not "..")
                    .Select(entry => new RemoteFileEntry
                    {
                        Name = entry.Name,
                        FullPath = entry.FullName,
                        IsDirectory = entry.IsDirectory,
                        SizeBytes = entry.IsDirectory ? 0 : entry.Length,
                        LastModifiedUtc = entry.LastWriteTimeUtc
                    })
                    .OrderByDescending(e => e.IsDirectory)
                    .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            catch (Exception ex) when (ex is SftpPathNotFoundException or SshException)
            {
                throw new AppOperationException($"「{remotePath}」の一覧を取得できませんでした。", ex);
            }
        }

        public void UploadFile(string localFullPath, string remoteDirectory)
        {
            try
            {
                var remotePath = CombineRemotePath(remoteDirectory, Path.GetFileName(localFullPath));
                using var stream = File.OpenRead(localFullPath);
                _client.UploadFile(stream, remotePath);
            }
            catch (Exception ex) when (ex is SshException or IOException or UnauthorizedAccessException)
            {
                throw new AppOperationException($"「{Path.GetFileName(localFullPath)}」のアップロードに失敗しました。", ex);
            }
        }

        public void DownloadFile(string remoteFullPath, string localDirectory)
        {
            try
            {
                var fileName = remoteFullPath[(remoteFullPath.LastIndexOf('/') + 1)..];
                var localPath = Path.Combine(localDirectory, fileName);
                using var stream = File.Create(localPath);
                _client.DownloadFile(remoteFullPath, stream);
            }
            catch (Exception ex) when (ex is SshException or IOException or UnauthorizedAccessException)
            {
                throw new AppOperationException($"「{remoteFullPath}」のダウンロードに失敗しました。", ex);
            }
        }

        public void CreateDirectory(string remotePath)
        {
            try
            {
                _client.CreateDirectory(remotePath);
            }
            catch (Exception ex) when (ex is SshException)
            {
                throw new AppOperationException("フォルダを作成できませんでした。", ex);
            }
        }

        public void Rename(string remotePath, string newName)
        {
            try
            {
                var parent = remotePath[..remotePath.LastIndexOf('/')];
                _client.RenameFile(remotePath, CombineRemotePath(parent, newName));
            }
            catch (Exception ex) when (ex is SshException)
            {
                throw new AppOperationException("名前を変更できませんでした。", ex);
            }
        }

        public void Delete(string remotePath, bool isDirectory)
        {
            try
            {
                if (isDirectory)
                {
                    _client.DeleteDirectory(remotePath);
                }
                else
                {
                    _client.DeleteFile(remotePath);
                }
            }
            catch (Exception ex) when (ex is SshException)
            {
                throw new AppOperationException($"「{remotePath}」を削除できませんでした。", ex);
            }
        }

        private static string CombineRemotePath(string directory, string name) =>
            directory.EndsWith('/') ? $"{directory}{name}" : $"{directory}/{name}";

        public void Dispose()
        {
            if (_client.IsConnected)
            {
                _client.Disconnect();
            }

            _client.Dispose();
        }
    }
}
