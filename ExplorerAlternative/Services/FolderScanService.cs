using System.IO;
using System.Security.Cryptography;
using ExplorerAlternative.Models;
using ExplorerAlternative.Services.Abstractions;

namespace ExplorerAlternative.Services;

/// <summary>
/// 仕様書12章・38章・58章・59章の実装。すべてTask.Runでバックグラウンド実行し、
/// アクセス不可なフォルダは（27章に従い）例外で中断せず読み飛ばす。
/// </summary>
public sealed class FolderScanService : IFolderScanService
{
    public Task<IReadOnlyList<FileSystemEntry>> SearchAsync(string rootPath, string query, CancellationToken cancellationToken)
    {
        return Task.Run<IReadOnlyList<FileSystemEntry>>(() =>
        {
            var results = new List<FileSystemEntry>();

            foreach (var info in EnumerateAll(rootPath, cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (info.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
                {
                    results.Add(ToEntry(info));
                }
            }

            return results;
        }, cancellationToken);
    }

    public Task<IReadOnlyList<LargeFileResult>> FindLargeFilesAsync(string rootPath, long minSizeBytes, CancellationToken cancellationToken)
    {
        return Task.Run<IReadOnlyList<LargeFileResult>>(() =>
        {
            var results = new List<LargeFileResult>();

            foreach (var info in EnumerateAll(rootPath, cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (info is FileInfo file)
                {
                    long length;
                    try
                    {
                        length = file.Length;
                    }
                    catch (IOException)
                    {
                        continue;
                    }

                    if (length >= minSizeBytes)
                    {
                        results.Add(new LargeFileResult { FullPath = file.FullName, SizeBytes = length });
                    }
                }
            }

            return results.OrderByDescending(r => r.SizeBytes).ToList();
        }, cancellationToken);
    }

    public Task<IReadOnlyList<DuplicateFileGroup>> FindDuplicateFilesAsync(string rootPath, CancellationToken cancellationToken)
    {
        return Task.Run<IReadOnlyList<DuplicateFileGroup>>(() =>
        {
            var bySize = new Dictionary<long, List<string>>();

            foreach (var info in EnumerateAll(rootPath, cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (info is FileInfo file)
                {
                    long length;
                    try
                    {
                        length = file.Length;
                    }
                    catch (IOException)
                    {
                        continue;
                    }

                    if (!bySize.TryGetValue(length, out var list))
                    {
                        list = new List<string>();
                        bySize[length] = list;
                    }

                    list.Add(file.FullName);
                }
            }

            var results = new List<DuplicateFileGroup>();

            foreach (var (size, paths) in bySize)
            {
                if (paths.Count < 2)
                {
                    continue;
                }

                cancellationToken.ThrowIfCancellationRequested();

                var byHash = new Dictionary<string, List<string>>();
                foreach (var path in paths)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var hash = TryComputeHash(path);
                    if (hash is null)
                    {
                        continue;
                    }

                    if (!byHash.TryGetValue(hash, out var list))
                    {
                        list = new List<string>();
                        byHash[hash] = list;
                    }

                    list.Add(path);
                }

                foreach (var (hash, hashPaths) in byHash)
                {
                    if (hashPaths.Count >= 2)
                    {
                        results.Add(new DuplicateFileGroup { Hash = hash, SizeBytes = size, Paths = hashPaths });
                    }
                }
            }

            return results.OrderByDescending(g => g.SizeBytes).ToList();
        }, cancellationToken);
    }

    public Task<IReadOnlyList<string>> FindEmptyFoldersAsync(string rootPath, CancellationToken cancellationToken)
    {
        return Task.Run<IReadOnlyList<string>>(() =>
        {
            var results = new List<string>();
            ScanEmptyDirectories(rootPath, results, cancellationToken);
            return results;
        }, cancellationToken);
    }

    // 再帰の後行きがけ順（post-order）で判定する：直下にファイルがなく、かつ
    // 全てのサブフォルダも（再帰的に）空である場合のみ「空フォルダ」とする。
    private static bool ScanEmptyDirectories(string path, List<string> results, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        bool hasFiles;
        List<string> subDirectories;

        try
        {
            hasFiles = Directory.EnumerateFiles(path).Any();
            subDirectories = Directory.EnumerateDirectories(path).ToList();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            return false;
        }

        var allChildrenEmpty = true;
        foreach (var subDirectory in subDirectories)
        {
            if (!ScanEmptyDirectories(subDirectory, results, cancellationToken))
            {
                allChildrenEmpty = false;
            }
        }

        var isEmpty = !hasFiles && allChildrenEmpty;
        if (isEmpty)
        {
            results.Add(path);
        }

        return isEmpty;
    }

    private static string? TryComputeHash(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var sha256 = SHA256.Create();
            var hashBytes = sha256.ComputeHash(stream);
            return Convert.ToHexString(hashBytes);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static FileSystemEntry ToEntry(FileSystemInfo info)
    {
        return info switch
        {
            FileInfo file => new FileSystemEntry
            {
                Name = file.Name,
                FullPath = file.FullName,
                IsDirectory = false,
                SizeBytes = TryGetLength(file),
                LastModified = TryGetLastWriteTime(info)
            },
            _ => new FileSystemEntry
            {
                Name = info.Name,
                FullPath = info.FullName,
                IsDirectory = true,
                LastModified = TryGetLastWriteTime(info)
            }
        };
    }

    private static long? TryGetLength(FileInfo info)
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

    private static DateTime? TryGetLastWriteTime(FileSystemInfo info)
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

    // 反復（スタック）方式で再帰を避け、巨大なツリーでもスタックオーバーフローしないようにする。
    // アクセス不可なフォルダは読み飛ばし、走査全体を中断させない（27章）。
    private static IEnumerable<FileSystemInfo> EnumerateAll(string rootPath, CancellationToken cancellationToken)
    {
        var stack = new Stack<string>();
        stack.Push(rootPath);

        while (stack.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = stack.Pop();

            List<string> subDirectories;
            List<string> files;

            try
            {
                subDirectories = Directory.EnumerateDirectories(current).ToList();
                files = Directory.EnumerateFiles(current).ToList();
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                continue;
            }

            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return new FileInfo(file);
            }

            foreach (var subDirectory in subDirectories)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return new DirectoryInfo(subDirectory);
                stack.Push(subDirectory);
            }
        }
    }
}
