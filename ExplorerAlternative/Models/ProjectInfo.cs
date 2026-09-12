namespace ExplorerAlternative.Models;

/// <summary>仕様書54章「プロジェクト認識」で検出したプロジェクトの情報。</summary>
public sealed class ProjectInfo
{
    public required string Name { get; init; }

    public required string RootPath { get; init; }

    /// <summary>検出の決め手になったファイル名（例："MyApp.csproj"、"package.json"）。</summary>
    public required string MarkerFile { get; init; }
}
