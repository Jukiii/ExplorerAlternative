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

    /// <summary>選択したファイル・フォルダを同じ場所に複製する。名前は自動的に一意な名前（「名前 (2)」等）を採番する。</summary>
    void Duplicate(IEnumerable<string> fullPaths);

    string ReadTextPreview(string filePath, int maxBytes, out bool truncated);
}
