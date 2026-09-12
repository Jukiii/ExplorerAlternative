using ExplorerAlternative.Services.Abstractions;

namespace ExplorerAlternative.Services;

/// <summary>仕様書14章の拡張ポイント。仕様書28章のPhase 1優先方針により未実装（構造のみ）。</summary>
public sealed class PatchService : IPatchService
{
    public bool IsSupported => false;
}
