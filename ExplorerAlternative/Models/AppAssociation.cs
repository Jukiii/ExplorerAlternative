namespace ExplorerAlternative.Models;

/// <summary>
/// 仕様書34章「常にこのアプリで開く」：このアプリの中だけで有効な、拡張子ごとの「開くアプリ」の関連付け。
/// Windowsのファイル関連付け（システム設定）は変更しない。
/// </summary>
public sealed class AppAssociation
{
    /// <summary>拡張子（ドット付き・小文字。例：<c>.cs</c>）。</summary>
    public required string Extension { get; set; }

    /// <summary>開くアプリの実行ファイルのフルパス。</summary>
    public required string ExecutablePath { get; set; }
}
