namespace ExplorerAlternative.Models;

/// <summary>
/// ワークスペースに保存する、統合ターミナルパネルの状態（仕様書43章）。
/// ターミナルの出力内容・カレントディレクトリ・実行中のコマンドは保存しない
/// （復元時は、新しいシェルを起動する）。
/// </summary>
public sealed class TerminalWorkspaceState
{
    /// <summary>パネルを表示していたか。</summary>
    public bool IsVisible { get; set; }

    /// <summary>パネルの高さ（仕様書17章「高さはドラッグ変更可能」）。</summary>
    public double PanelHeight { get; set; } = 230;

    /// <summary>アクティブだったタブの位置（0始まり）。</summary>
    public int ActiveIndex { get; set; }

    /// <summary>開いていたタブ（左から順）。</summary>
    public List<TerminalTabState> Tabs { get; set; } = new();
}

/// <summary>ターミナルのタブ1枚分の状態。</summary>
public sealed class TerminalTabState
{
    /// <summary>Explorerとのカレントディレクトリ同期（Sync）がONだったか（仕様書18章）。</summary>
    public bool SyncEnabled { get; set; } = true;
}
