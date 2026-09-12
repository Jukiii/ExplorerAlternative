namespace ExplorerAlternative.Models;

public sealed class TerminalSettings
{
    public string ShellExecutable { get; set; } = "powershell.exe";

    public bool SyncByDefault { get; set; } = true;
}
