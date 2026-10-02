using System.IO;
using ExplorerAlternative.Models;
using ExplorerAlternative.Services.Abstractions;
using Renci.SshNet;
using Renci.SshNet.Common;

namespace ExplorerAlternative.Services;

/// <summary>仕様書44章「SFTPリモートファイル操作」の実装。SSH.NET（Renci.SshNet）を使用する。
/// パスフレーズ付きの秘密鍵は、接続のたびに利用者に入力してもらう（パスフレーズはどこにも保存しない）。</summary>
public sealed class SftpService : ISftpService
{
    /// <summary>
    /// リモートのパスの、最後の名前を、ローカルに保存するファイル名として返す。リモート側（サーバー）が
    /// 返す名前に、Windowsのパス区切り（\）や、使えない文字・「..」が含まれていると、保存先のフォルダの
    /// 外へ書き込まれてしまうため、そのような名前は受け付けず、例外にする。
    /// </summary>
    internal static string GetSafeLocalFileName(string remoteFullPath)
    {
        var name = remoteFullPath[(remoteFullPath.LastIndexOf('/') + 1)..];

        if (string.IsNullOrWhiteSpace(name)
            || name is "." or ".."
            || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new AppOperationException($"「{remoteFullPath}」は、ローカルに保存できないファイル名のため、ダウンロードできません。");
        }

        return name;
    }

    /// <summary>
    /// 秘密鍵を読み込む。パスフレーズで保護されていて、パスフレーズが無い・違う場合は、
    /// <see cref="SftpKeyPassphraseException"/>を投げる。それ以外の理由で読めない鍵は、<c>null</c>を返す
    /// （パスワード認証にフォールバックさせるため）。
    /// </summary>
    internal static PrivateKeyFile? LoadPrivateKey(string path, string? passphrase)
    {
        try
        {
            return string.IsNullOrEmpty(passphrase) ? new PrivateKeyFile(path) : new PrivateKeyFile(path, passphrase);
        }
        catch (SshPassPhraseNullOrEmptyException)
        {
            throw new SftpKeyPassphraseException(Path.GetFileName(path), wasWrong: false);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            return null;
        }
        catch (Exception) when (!string.IsNullOrEmpty(passphrase))
        {
            // パスフレーズが違うと、鍵の形式（RSA・Ed25519等）によって、SshException・CryptographicException・
            // ASN.1の例外などの、様々な例外になる。パスフレーズなしで開こうとして、パスフレーズが要る鍵だと
            // 分かれば「パスフレーズが違う」、そうでなければ「読めない鍵」として扱う。
            if (IsProtectedByPassphrase(path))
            {
                throw new SftpKeyPassphraseException(Path.GetFileName(path), wasWrong: true);
            }

            return null;
        }
        catch (SshException)
        {
            return null;
        }
    }

    private static bool IsProtectedByPassphrase(string path)
    {
        try
        {
            _ = new PrivateKeyFile(path);
            return false;
        }
        catch (SshPassPhraseNullOrEmptyException)
        {
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public ISftpSession Connect(SshConnectionProfile profile, string? password, string? keyPassphrase = null)
    {
        var authMethods = new List<AuthenticationMethod>();

        if (!string.IsNullOrWhiteSpace(profile.IdentityFilePath) && File.Exists(profile.IdentityFilePath))
        {
            var keyFile = LoadPrivateKey(profile.IdentityFilePath, keyPassphrase);
            if (keyFile is not null)
            {
                authMethods.Add(new PrivateKeyAuthenticationMethod(profile.UserName ?? Environment.UserName, keyFile));
            }
        }

        if (!string.IsNullOrEmpty(password))
        {
            authMethods.Add(new PasswordAuthenticationMethod(profile.UserName ?? Environment.UserName, password));
        }

        if (authMethods.Count == 0)
        {
            throw new AppOperationException(
                "SFTP接続に使える認証情報がありません。パスワードを保存するか、秘密鍵を指定してください。");
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
                var localPath = Path.Combine(localDirectory, GetSafeLocalFileName(remoteFullPath));
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
