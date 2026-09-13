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

    /// <summary>仕様書20章：Ctrl+ドラッグ時、移動先に同名の項目が既に存在する場合の解決方法。</summary>
    void CopyRenamed(string sourcePath, string destinationDirectory);

    /// <summary>移動先の同名の項目を削除してから上書きコピーする。</summary>
    void CopyReplacing(string sourcePath, string destinationDirectory);

    /// <summary>仕様書20章：Alt+ドラッグでのショートカット（.lnk）作成。</summary>
    void CreateShortcuts(IEnumerable<string> sourcePaths, string destinationDirectory);

    string ReadTextPreview(string filePath, int maxBytes, out bool truncated);
}
