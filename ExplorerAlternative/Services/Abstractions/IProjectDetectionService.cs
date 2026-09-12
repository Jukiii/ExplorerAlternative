using ExplorerAlternative.Models;

namespace ExplorerAlternative.Services.Abstractions;

/// <summary>
/// 仕様書54章「プロジェクト認識」・56章「プロジェクトルート」：
/// 現在パスから親方向へ探索し、プロジェクト／ソリューションのルートを検出する。
/// </summary>
public interface IProjectDetectionService
{
    /// <summary>現在パスまたはその祖先で最初に見つかったプロジェクトマーカーを返す。</summary>
    ProjectInfo? Detect(string path);

    /// <summary>現在パスまたはその祖先で最初に見つかった.slnファイルのあるフォルダを返す。</summary>
    string? FindSolutionRoot(string path);
}
