namespace ExplorerAlternative.Services.Abstractions;

/// <summary>仕様書40章「システムトレイ」のメニュー項目。子を持つ場合はサブメニューになる。</summary>
public sealed class TrayMenuItem
{
    public required string Text { get; init; }

    public Action? Execute { get; init; }

    public IReadOnlyList<TrayMenuItem>? Children { get; init; }

    public bool IsSeparator { get; init; }
}
