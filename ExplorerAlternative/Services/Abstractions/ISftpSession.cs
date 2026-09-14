using ExplorerAlternative.Models;

namespace ExplorerAlternative.Services.Abstractions;

/// <summary>仕様書44章「SFTPリモートファイル操作」：接続済みの1セッションに対する操作。</summary>
public interface ISftpSession : IDisposable
{
    /// <summary>接続直後のログインディレクトリ（ホームディレクトリ）。</summary>
    string HomeDirectory { get; }

    IReadOnlyList<RemoteFileEntry> ListDirectory(string remotePath);

    void UploadFile(string localFullPath, string remoteDirectory);

    void DownloadFile(string remoteFullPath, string localDirectory);

    void CreateDirectory(string remotePath);

    void Rename(string remotePath, string newName);

    void Delete(string remotePath, bool isDirectory);
}
