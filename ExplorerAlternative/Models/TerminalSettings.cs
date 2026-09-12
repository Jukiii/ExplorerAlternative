namespace ExplorerAlternative.Models;

public sealed class TerminalSettings
{
    public string ShellExecutable { get; set; } = "powershell.exe";

    public bool SyncByDefault { get; set; } = true;

    /// <summary>仕様書17章「PowerShellプロファイル」。既定はOFF（起動を高速にするため -NoProfile）。</summary>
    public bool LoadProfile { get; set; }
}
