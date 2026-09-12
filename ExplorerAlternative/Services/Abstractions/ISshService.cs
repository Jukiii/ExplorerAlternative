namespace ExplorerAlternative.Services.Abstractions;

/// <summary>
/// 仕様書15章：SSH接続機能を追加できる構造にするための拡張ポイント。
/// Phase 1では構造のみを定義し、実装は将来のフェーズで行う。
/// </summary>
public interface ISshService
{
    bool IsSupported { get; }
}
