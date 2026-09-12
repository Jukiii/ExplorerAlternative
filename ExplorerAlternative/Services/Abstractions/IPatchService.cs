namespace ExplorerAlternative.Services.Abstractions;

/// <summary>
/// 仕様書14章：Patch作成・適用機能の拡張ポイント。
/// Phase 1では構造のみを定義し、実装は将来のフェーズで行う（仕様書28章のPhase 1優先方針に基づく）。
/// </summary>
public interface IPatchService
{
    bool IsSupported { get; }
}
