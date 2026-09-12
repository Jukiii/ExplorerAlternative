namespace ExplorerAlternative.Models;

public sealed class VersionControlInfo
{
    public static VersionControlInfo None { get; } = new() { Kind = VersionControlKind.None };

    public required VersionControlKind Kind { get; init; }

    public string? RootPath { get; init; }

    public string? BranchName { get; init; }

    public string? StatusSummary { get; init; }
}
