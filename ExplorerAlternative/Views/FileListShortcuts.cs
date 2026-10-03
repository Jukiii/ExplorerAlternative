using System.Windows.Input;
using ExplorerAlternative.Mvvm;
using ExplorerAlternative.ViewModels;

namespace ExplorerAlternative.Views;

/// <summary>
/// ファイル・フォルダの一覧（階層表示・詳細表示）のキー操作のうち、クリップボードの操作（仕様書20章）。
/// どのキーがどのコマンドかの判断だけを、画面の部品から分けて持つ（テストで確かめられるように）。
/// </summary>
public static class FileListShortcuts
{
    /// <summary>
    /// Ctrl+C（コピー）・Ctrl+X（切り取り）・Ctrl+V（貼り付け）に対応するコマンドを返す。
    /// 該当しなければ<c>null</c>（Shift・Altなど、ほかの修飾キーが付いているものは、対象にしない）。
    /// </summary>
    public static RelayCommand? ResolveClipboardCommand(PaneViewModel pane, Key key, ModifierKeys modifiers)
    {
        if (modifiers != ModifierKeys.Control)
        {
            return null;
        }

        return key switch
        {
            Key.C => pane.CopyCommand,
            Key.X => pane.CutCommand,
            Key.V => pane.PasteCommand,
            _ => null
        };
    }
}
