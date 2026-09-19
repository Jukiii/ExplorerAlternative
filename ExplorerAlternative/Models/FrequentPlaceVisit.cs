namespace ExplorerAlternative.Models;

/// <summary>「よく使う場所」のアクセス回数記録（仕様書39章）。</summary>
public sealed class FrequentPlaceVisit
{
    public required string Path { get; set; }

    public int VisitCount { get; set; }
}
