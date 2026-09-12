using ExplorerAlternative.Services.Abstractions;
using Microsoft.Win32;

namespace ExplorerAlternative.Services;

/// <summary>
/// 仕様書35章：Windowsのフォルダ右クリックメニュー（フォルダ自体・フォルダの背景の両方）に
/// 「ExplorerAlternativeで開く」を登録する。管理者権限を必要としないよう、
/// HKEY_CURRENT_USER配下のみを使用する（無効化時は登録したキーを削除するだけで元に戻る）。
/// </summary>
public sealed class ExplorerIntegrationService : IExplorerIntegrationService
{
    private const string MenuText = "ExplorerAlternativeで開く";
    private const string KeyName = "ExplorerAlternative";

    private static readonly string[] DirectoryShellPaths =
    {
        @"Software\Classes\Directory\shell\" + KeyName,
        @"Software\Classes\Directory\Background\shell\" + KeyName,
        @"Software\Classes\Drive\shell\" + KeyName,
    };

    public bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(DirectoryShellPaths[0]);
            return key is not null;
        }
    }

    public void Enable()
    {
        var exePath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exePath))
        {
            return;
        }

        // Directory/Driveは選択したフォルダ自体（%1）、Background は現在開いているフォルダ（%V）を渡す。
        WriteShellEntry(DirectoryShellPaths[0], $"\"{exePath}\" \"%1\"");
        WriteShellEntry(DirectoryShellPaths[1], $"\"{exePath}\" \"%V\"");
        WriteShellEntry(DirectoryShellPaths[2], $"\"{exePath}\" \"%1\"");
    }

    public void Disable()
    {
        foreach (var path in DirectoryShellPaths)
        {
            Registry.CurrentUser.DeleteSubKeyTree(path, throwOnMissingSubKey: false);
        }
    }

    private static void WriteShellEntry(string shellKeyPath, string command)
    {
        using var shellKey = Registry.CurrentUser.CreateSubKey(shellKeyPath);
        shellKey.SetValue(string.Empty, MenuText);
        shellKey.SetValue("Icon", $"\"{Environment.ProcessPath}\"");

        using var commandKey = shellKey.CreateSubKey("command");
        commandKey.SetValue(string.Empty, command);
    }
}
