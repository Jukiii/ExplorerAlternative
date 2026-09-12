namespace ExplorerAlternative.Models;

/// <summary>
/// アプリケーション設定の一元管理データ。仕様書25章。
/// </summary>
public sealed class AppSettings
{
    public List<string> TextFileExtensions { get; set; } = new();

    public List<ExternalToolDefinition> ExternalTools { get; set; } = new();

    public List<FavoriteEntry> Favorites { get; set; } = new();

    public List<TagDefinition> TagDefinitions { get; set; } = new();

    public List<TagAssignment> TagAssignments { get; set; } = new();

    public TerminalSettings Terminal { get; set; } = new();

    public ViewSettings View { get; set; } = new();

    public AppearanceSettings Appearance { get; set; } = new();

    public List<WorkspaceState> Workspaces { get; set; } = new();
}
