using ExplorerAlternative.Services.Abstractions;

namespace ExplorerAlternative.Services;

/// <summary>仕様書15章の拡張ポイント。Phase 1では未実装（構造のみ）。</summary>
public sealed class SshService : ISshService
{
    public bool IsSupported => false;
}
