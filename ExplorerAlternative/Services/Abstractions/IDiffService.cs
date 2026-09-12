using ExplorerAlternative.Models;

namespace ExplorerAlternative.Services.Abstractions;

/// <summary>仕様書23章「Diff」・25章「ファイル比較」で共通利用する行単位の比較。</summary>
public interface IDiffService
{
    /// <summary>2つのテキストを行単位で比較する。Git管理外のファイル同士でも利用できる。</summary>
    IReadOnlyList<DiffLine> Compare(string? leftText, string? rightText);
}
