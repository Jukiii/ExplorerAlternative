using System.IO;
using ExplorerAlternative.Models;
using ExplorerAlternative.Services.Abstractions;
using Microsoft.VisualBasic.FileIO;

namespace ExplorerAlternative.Services;

/// <summary>
/// ファイルシステム操作の実装。仕様書27章に従い、失敗時は例外を握りつぶさず
/// <see cref="AppOperationException"/>（日本語メッセージ）に変換して上位層へ伝える。
/// </summary>
public sealed class FileSystemService : IFileSystemService
{
    public IReadOnlyList<FileSystemEntry> GetDrives()
    {
        var result = new List<FileSystemEntry>();

        foreach (var drive in DriveInfo.GetDrives())
        {
            if (!drive.IsReady)
            {
                continue;
            }

            result.Add(new FileSystemEntry
            {
                Name = drive.Name.TrimEnd('\\'),
                FullPath = drive.Name,
                IsDirectory = true
            });
        }

        return result;
    }

    public IReadOnlyList<FileSystemEntry> GetChildren(string path)
    {
        try
        {
            var result = new List<FileSystemEntry>();

            foreach (var directory in Directory.EnumerateDirectories(path))
            {
                var info = new DirectoryInfo(directory);
                result.Add(new FileSystemEntry
                {
                    Name = info.Name,
                    FullPath = info.FullName,
                    IsDirectory = true,
                    LastModified = SafeGetLastWriteTime(info),
                    Created = SafeGetCreationTime(info),
                    IsHidden = IsHiddenOrSystem(info.Attributes, info.Name)
                });
            }

            foreach (var file in Directory.EnumerateFiles(path))
            {
                var info = new FileInfo(file);
                result.Add(new FileSystemEntry
                {
                    Name = info.Name,
                    FullPath = info.FullName,
                    IsDirectory = false,
                    SizeBytes = SafeGetLength(info),
                    LastModified = SafeGetLastWriteTime(info),
                    Created = SafeGetCreationTime(info),
                    IsHidden = IsHiddenOrSystem(info.Attributes, info.Name)
                });
            }

            return result;
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new AppOperationException($"「{path}」へのアクセス権限がありません。", ex);
        }
        catch (DirectoryNotFoundException ex)
        {
            throw new AppOperationException($"フォルダ「{path}」が見つかりません。", ex);
        }
        catch (IOException ex)
        {
            throw new AppOperationException($"「{path}」の読み取り中にエラーが発生しました。", ex);
        }
    }

    public string? GetParent(string path)
    {
        try
        {
            return Directory.GetParent(path)?.FullName;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    public bool DirectoryExists(string path) => Directory.Exists(path);

    public bool FileExists(string path) => File.Exists(path);

    public void CreateDirectory(string parentPath, string name)
    {
        try
        {
            Directory.CreateDirectory(Path.Combine(parentPath, name));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            throw new AppOperationException($"フォルダ「{name}」を作成できませんでした。", ex);
        }
    }

    public void CreateFile(string parentPath, string name)
    {
        try
        {
            var path = Path.Combine(parentPath, name);

            if (File.Exists(path) || Directory.Exists(path))
            {
                throw new AppOperationException($"「{name}」は既に存在します。");
            }

            using var _ = File.Create(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            throw new AppOperationException($"ファイル「{name}」を作成できませんでした。", ex);
        }
    }

    public void Rename(string fullPath, string newName)
    {
        try
        {
            var parent = Path.GetDirectoryName(fullPath)
                ?? throw new AppOperationException($"「{fullPath}」の名前を変更できませんでした。");
            var destination = Path.Combine(parent, newName);

            if (Directory.Exists(fullPath))
            {
                Directory.Move(fullPath, destination);
            }
            else if (File.Exists(fullPath))
            {
                File.Move(fullPath, destination);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            throw new AppOperationException($"「{Path.GetFileName(fullPath)}」の名前を変更できませんでした。", ex);
        }
    }

    public void Delete(IEnumerable<string> fullPaths)
    {
        foreach (var path in fullPaths)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    FileSystem.DeleteDirectory(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
                }
                else if (File.Exists(path))
                {
                    FileSystem.DeleteFile(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new AppOperationException($"「{Path.GetFileName(path)}」を削除できませんでした。", ex);
            }
        }
    }

    public void Copy(IEnumerable<string> sourcePaths, string destinationDirectory)
    {
        foreach (var source in sourcePaths)
        {
            try
            {
                var name = Path.GetFileName(source);
                var destination = Path.Combine(destinationDirectory, name);

                if (Directory.Exists(source))
                {
                    CopyDirectoryRecursive(source, destination);
                }
                else if (File.Exists(source))
                {
                    File.Copy(source, destination, overwrite: false);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new AppOperationException($"「{Path.GetFileName(source)}」をコピーできませんでした。", ex);
            }
        }
    }

    public void Move(IEnumerable<string> sourcePaths, string destinationDirectory)
    {
        foreach (var source in sourcePaths)
        {
            try
            {
                var name = Path.GetFileName(source);
                var destination = Path.Combine(destinationDirectory, name);

                if (Directory.Exists(source))
                {
                    Directory.Move(source, destination);
                }
                else if (File.Exists(source))
                {
                    File.Move(source, destination);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new AppOperationException($"「{Path.GetFileName(source)}」を移動できませんでした。", ex);
            }
        }
    }

    public void Duplicate(IEnumerable<string> fullPaths)
    {
        foreach (var source in fullPaths)
        {
            try
            {
                var parent = Path.GetDirectoryName(source);
                if (parent is null)
                {
                    continue;
                }

                var destination = GetUniqueDuplicateName(parent, source);

                if (Directory.Exists(source))
                {
                    CopyDirectoryRecursive(source, destination);
                }
                else if (File.Exists(source))
                {
                    File.Copy(source, destination, overwrite: false);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new AppOperationException($"「{Path.GetFileName(source)}」を複製できませんでした。", ex);
            }
        }
    }

    private static string GetUniqueDuplicateName(string parent, string source)
    {
        var isDirectory = Directory.Exists(source);
        var extension = isDirectory ? string.Empty : Path.GetExtension(source);
        var baseName = isDirectory ? Path.GetFileName(source) : Path.GetFileNameWithoutExtension(source);

        for (var i = 2; ; i++)
        {
            var candidateName = $"{baseName} ({i}){extension}";
            var candidatePath = Path.Combine(parent, candidateName);
            if (!Directory.Exists(candidatePath) && !File.Exists(candidatePath))
            {
                return candidatePath;
            }
        }
    }

    public string ReadTextPreview(string filePath, int maxBytes, out bool truncated)
    {
        try
        {
            var fileInfo = new FileInfo(filePath);
            truncated = fileInfo.Length > maxBytes;

            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            var buffer = new char[maxBytes];
            var readCount = reader.ReadBlock(buffer, 0, maxBytes);
            return new string(buffer, 0, readCount);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new AppOperationException($"「{Path.GetFileName(filePath)}」を読み込めませんでした。", ex);
        }
    }

    private static void CopyDirectoryRecursive(string sourceDir, string destinationDir)
    {
        Directory.CreateDirectory(destinationDir);

        foreach (var file in Directory.EnumerateFiles(sourceDir))
        {
            File.Copy(file, Path.Combine(destinationDir, Path.GetFileName(file)), overwrite: false);
        }

        foreach (var directory in Directory.EnumerateDirectories(sourceDir))
        {
            CopyDirectoryRecursive(directory, Path.Combine(destinationDir, Path.GetFileName(directory)));
        }
    }

    private static DateTime? SafeGetLastWriteTime(FileSystemInfo info)
    {
        try
        {
            return info.LastWriteTime;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static DateTime? SafeGetCreationTime(FileSystemInfo info)
    {
        try
        {
            return info.CreationTime;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static long? SafeGetLength(FileInfo info)
    {
        try
        {
            return info.Length;
        }
        catch (IOException)
        {
            return null;
        }
    }

    // 仕様書49章の例（.git, .gitignore, .env, .vscode等）はWindowsのHidden属性を
    // 持たないことが多いため、Windows属性に加えてドット始まりの名前も隠しファイルとして扱う。
    private static bool IsHiddenOrSystem(FileAttributes attributes, string name) =>
        attributes.HasFlag(FileAttributes.Hidden) || attributes.HasFlag(FileAttributes.System) || name.StartsWith('.');
}
