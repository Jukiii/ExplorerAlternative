using ExplorerAlternative.Models;

namespace ExplorerAlternative.Services.Abstractions;

/// <summary>
/// フォルダ以下を再帰的に走査する各種検索（仕様書12章・38章・58章・59章）。
/// いずれもバックグラウンドスレッドで実行し、CancellationTokenでキャンセル可能にする。
/// </summary>
public interface IFolderScanService
{
    /// <summary>仕様書12章「検索」：ファイル名・フォルダ名（拡張子を含む）を部分一致で検索する。</summary>
    Task<IReadOnlyList<FileSystemEntry>> SearchAsync(string rootPath, string query, CancellationToken cancellationToken);

    /// <summary>仕様書38章「巨大ファイル検索」：指定サイズ以上のファイルをサイズ降順で返す。</summary>
    Task<IReadOnlyList<LargeFileResult>> FindLargeFilesAsync(string rootPath, long minSizeBytes, CancellationToken cancellationToken);

    /// <summary>仕様書58章「重複ファイル検索」：同一サイズ→同一ハッシュ（SHA-256）でグループ化する。</summary>
    Task<IReadOnlyList<DuplicateFileGroup>> FindDuplicateFilesAsync(string rootPath, CancellationToken cancellationToken);

    /// <summary>仕様書59章「空フォルダ検索」：ファイルを一切含まない（再帰的に空の）フォルダを返す。</summary>
    Task<IReadOnlyList<string>> FindEmptyFoldersAsync(string rootPath, CancellationToken cancellationToken);
}
