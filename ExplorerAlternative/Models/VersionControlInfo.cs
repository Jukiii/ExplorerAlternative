namespace ExplorerAlternative.Models;

public sealed class VersionControlInfo
{
    public static VersionControlInfo None { get; } = new() { Kind = VersionControlKind.None };

    public required VersionControlKind Kind { get; init; }

    public string? RootPath { get; init; }

    public string? BranchName { get; init; }

    public string? StatusSummary { get; init; }

    /// <summary>
    /// 同じ場所に、もう一方の管理情報（.gitと.svnの併存。仕様書20章「両方存在する場合は両方を認識する」）も
    /// 存在する場合の、その種別。無い場合はNone。表示・操作の対象は<see cref="Kind"/>で、ペインの操作で切り替えられる。
    /// </summary>
    public VersionControlKind OtherKind { get; init; } = VersionControlKind.None;

    public string? OtherRootPath { get; init; }

    public bool HasOther => OtherKind != VersionControlKind.None;
}
