using ExplorerAlternative.Models;

namespace ExplorerAlternative.Services.Abstractions;

/// <summary>仕様書45章「フォルダ同期」：2つのフォルダを再帰的に比較する。</summary>
public interface IFolderCompareService
{
    /// <summary>
    /// leftRoot・rightRootを相対パス基準で再帰的に比較し、相対パスの昇順で結果を返す。
    /// サイズが異なる場合は即「異なる」と判定し、サイズが同じ場合のみSHA-256ハッシュを
    /// 比較する（全ファイルを毎回ハッシュ化するコストを避けるため）。
    /// </summary>
    Task<IReadOnlyList<FolderCompareEntry>> CompareAsync(string leftRoot, string rightRoot, CancellationToken cancellationToken);
}
