namespace ExplorerAlternative.Models;

public sealed class TagAssignment
{
    public required string Path { get; set; }

    public List<string> Tags { get; set; } = new();
}
