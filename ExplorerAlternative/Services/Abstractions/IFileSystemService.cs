using ExplorerAlternative.Models;

namespace ExplorerAlternative.Services.Abstractions;

public interface IFileSystemService
{
    /// <summary>「PC」階層（アドレスバー最上位）に表示するドライブ一覧を取得する。</summary>
    IReadOnlyList<FileSystemEntry> GetDrives();

    IReadOnlyList<FileSystemEntry> GetChildren(string path);

    string? GetParent(string path);

    bool DirectoryExists(string path);

    bool FileExists(string path);

    void CreateDirectory(string parentPath, string name);

    void CreateFile(string parentPath, string name);

    void Rename(string fullPath, string newName);

    void Delete(IEnumerable<string> fullPaths);

    void Copy(IEnumerable<string> sourcePaths, string destinationDirectory);

    void Move(IEnumerable<string> sourcePaths, string destinationDirectory);

    string ReadTextPreview(string filePath, int maxBytes, out bool truncated);
}
