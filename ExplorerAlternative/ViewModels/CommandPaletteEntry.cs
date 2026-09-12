namespace ExplorerAlternative.ViewModels;

/// <summary>コマンドパレット（仕様書46章）に表示する1項目。</summary>
public sealed class CommandPaletteEntry
{
    public required string Name { get; init; }

    public required Action Execute { get; init; }

    public Func<bool> CanExecute { get; init; } = () => true;
}
