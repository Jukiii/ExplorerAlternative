using System.IO;
using System.Security.Cryptography;
using ExplorerAlternative.Models;
using ExplorerAlternative.Services.Abstractions;

namespace ExplorerAlternative.Services;

/// <summary>仕様書45章「フォルダ同期」の実装。バックグラウンドで再帰的に走査し、
/// アクセス不可なフォルダ・ファイルは（27章に従い）例外で中断せず読み飛ばす。</summary>
public sealed class FolderCompareService : IFolderCompareService
{
    public Task<IReadOnlyList<FolderCompareEntry>> CompareAsync(string leftRoot, string rightRoot, CancellationToken cancellationToken)
    {
        return Task.Run<IReadOnlyList<FolderCompareEntry>>(() =>
        {
            var leftFiles = EnumerateRelativeFiles(leftRoot, cancellationToken);
            var rightFiles = EnumerateRelativeFiles(rightRoot, cancellationToken);

            var allRelativePaths = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            allRelativePaths.UnionWith(leftFiles.Keys);
            allRelativePaths.UnionWith(rightFiles.Keys);

            var results = new List<FolderCompareEntry>();

            foreach (var relativePath in allRelativePaths)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var hasLeft = leftFiles.TryGetValue(relativePath, out var leftPath);
                var hasRight = rightFiles.TryGetValue(relativePath, out var rightPath);

                FolderCompareStatus status;
                if (hasLeft && !hasRight)
                {
                    status = FolderCompareStatus.OnlyLeft;
                }
                else if (!hasLeft && hasRight)
                {
                    status = FolderCompareStatus.OnlyRight;
                }
                else
                {
                    status = AreFilesEqual(leftPath!, rightPath!) ? FolderCompareStatus.Same : FolderCompareStatus.Different;
                }

                results.Add(new FolderCompareEntry
                {
                    RelativePath = relativePath,
                    Status = status,
                    LeftFullPath = leftPath,
                    RightFullPath = rightPath
                });
            }

            return results;
        }, cancellationToken);
    }

    private static bool AreFilesEqual(string leftPath, string rightPath)
    {
        try
        {
            var leftInfo = new FileInfo(leftPath);
            var rightInfo = new FileInfo(rightPath);

            if (leftInfo.Length != rightInfo.Length)
            {
                return false;
            }

            var leftHash = TryComputeHash(leftPath);
            var rightHash = TryComputeHash(rightPath);
            return leftHash is not null && leftHash == rightHash;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static string? TryComputeHash(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var sha256 = SHA256.Create();
            return Convert.ToHexString(sha256.ComputeHash(stream));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static Dictionary<string, string> EnumerateRelativeFiles(string root, CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(root))
        {
            return result;
        }

        EnumerateRelativeFiles(root, root, result, cancellationToken);
        return result;
    }

    private static void EnumerateRelativeFiles(string root, string currentDirectory, Dictionary<string, string> result, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        IEnumerable<string> files;
        IEnumerable<string> subDirectories;

        try
        {
            files = Directory.EnumerateFiles(currentDirectory);
            subDirectories = Directory.EnumerateDirectories(currentDirectory);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            return;
        }

        foreach (var file in files)
        {
            var relativePath = Path.GetRelativePath(root, file);
            result[relativePath] = file;
        }

        foreach (var subDirectory in subDirectories)
        {
            EnumerateRelativeFiles(root, subDirectory, result, cancellationToken);
        }
    }
}
