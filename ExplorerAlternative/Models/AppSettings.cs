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

    /// <summary>仕様書37章「スマートタブ」。</summary>
    public TabSettings Tabs { get; set; } = new();

    /// <summary>最近使った場所（仕様書50章）。先頭が最新。</summary>
    public List<string> RecentPlaces { get; set; } = new();

    /// <summary>最近開いたプロジェクト（仕様書55章）。先頭が最新。</summary>
    public List<FavoriteEntry> RecentProjects { get; set; } = new();

    /// <summary>登録済みSSH接続先（仕様書44章）。パスワード・パスフレーズは含まない。</summary>
    public List<SshConnectionProfile> SshProfiles { get; set; } = new();

    /// <summary>システムトレイ・グローバルホットキー（仕様書40章・41章）。</summary>
    public WindowsIntegrationSettings WindowsIntegration { get; set; } = new();
}
