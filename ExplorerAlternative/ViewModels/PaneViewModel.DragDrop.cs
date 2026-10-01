using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using ExplorerAlternative.Models;
using ExplorerAlternative.Mvvm;
using ExplorerAlternative.Services;
using ExplorerAlternative.Services.Abstractions;

namespace ExplorerAlternative.ViewModels;

/// <summary>
/// PaneViewModelのうち、ドラッグ&ドロップによる移動・コピー・ショートカット作成（仕様書20章）。
/// </summary>
public sealed partial class PaneViewModel
{
    public void DropFiles(IReadOnlyList<string> sourcePaths, string destinationFolder, bool isMove)
    {
        var targets = sourcePaths
            .Where(source => !IsNoOpOrInvalidDrop(source, destinationFolder))
            .ToList();

        if (targets.Count == 0)
        {
            return;
        }

        if (!ConfirmOperationIfNeeded(isMove ? "移動" : "コピー", targets, destinationFolder))
        {
            return;
        }

        EnqueueOperation(isMove ? "移動" : "コピー", targets, destinationFolder, isMove);
    }

    private static bool IsNoOpOrInvalidDrop(string sourcePath, string destinationFolder)
    {
        var normalizedSource = Path.TrimEndingDirectorySeparator(sourcePath);
        var normalizedDestination = Path.TrimEndingDirectorySeparator(destinationFolder);

        if (string.Equals(normalizedSource, normalizedDestination, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var sourceParent = Path.GetDirectoryName(normalizedSource);
        if (string.Equals(sourceParent, normalizedDestination, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // フォルダを自分自身の子孫へ移動・コピーすることはできない。
        return normalizedDestination.StartsWith(normalizedSource + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    // 仕様書20章：Ctrl+ドラッグ。同じフォルダ内へのドロップは複製として扱い、異なるフォルダで
    // 同名の項目が既に存在する場合は「名前を変更してコピー」／「上書きする」／「キャンセル」を尋ねる。
    public void DropFilesAsCopy(IReadOnlyList<string> sourcePaths, string destinationFolder)
    {
        var targets = sourcePaths.Where(source => !IsSelfOrDescendantDrop(source, destinationFolder)).ToList();
        if (targets.Count == 0)
        {
            return;
        }

        if (!ConfirmOperationIfNeeded("コピー", targets, destinationFolder))
        {
            return;
        }

        var undoActions = new List<(string Name, Action Undo, Action Redo)>();

        try
        {
            foreach (var source in targets)
            {
                var normalizedSource = Path.TrimEndingDirectorySeparator(source);
                var normalizedDestination = Path.TrimEndingDirectorySeparator(destinationFolder);
                var sourceParent = Path.GetDirectoryName(normalizedSource);
                var fileName = Path.GetFileName(source);

                if (string.Equals(sourceParent, normalizedDestination, StringComparison.OrdinalIgnoreCase))
                {
                    var created = _fileSystemService.Duplicate(new[] { source });
                    if (created.Count > 0)
                    {
                        undoActions.Add((fileName, () => _fileSystemService.Delete(created), () => _fileSystemService.Duplicate(new[] { source })));
                    }

                    continue;
                }

                var destinationPath = Path.Combine(destinationFolder, fileName);
                if (!Directory.Exists(destinationPath) && !File.Exists(destinationPath))
                {
                    _fileSystemService.Copy(new[] { source }, destinationFolder);
                    undoActions.Add((fileName, () => _fileSystemService.Delete(new[] { destinationPath }), () => _fileSystemService.Copy(new[] { source }, destinationFolder)));
                    continue;
                }

                var choice = _dialogService.SelectFromList(
                    "ファイルの競合",
                    $"「{fileName}」は移動先に既に存在します。どうしますか？",
                    new[] { "名前を変更してコピー（*_copy）", "上書きする" });

                if (choice == "名前を変更してコピー（*_copy）")
                {
                    var renamedPath = _fileSystemService.CopyRenamed(source, destinationFolder);
                    undoActions.Add((fileName, () => _fileSystemService.Delete(new[] { renamedPath }), () => _fileSystemService.CopyRenamed(source, destinationFolder)));
                }
                else if (choice == "上書きする")
                {
                    _fileSystemService.CopyReplacing(source, destinationFolder);
                    // 上書きコピーは置き換え前の内容を保持しないためUndo対象外。
                }

                // それ以外（キャンセル・ダイアログを閉じた）は何もしない。
            }

            if (undoActions.Count > 0)
            {
                var description = undoActions.Count == 1
                    ? $"「{undoActions[0].Name}」のコピー"
                    : $"{undoActions.Count}件のコピー";
                _undoService.Record(
                    description,
                    () =>
                    {
                        foreach (var (_, undo, _) in undoActions)
                        {
                            undo();
                        }
                    },
                    () =>
                    {
                        foreach (var (_, _, redo) in undoActions)
                        {
                            redo();
                        }
                    });
            }

            LogHistory("コピー", DescribeTargets(targets), originalLocation: DescribeSourceFolder(targets), destination: destinationFolder);
            RefreshCurrentFolder();
        }
        catch (AppOperationException ex)
        {
            LogHistory("コピー", DescribeTargets(targets), originalLocation: DescribeSourceFolder(targets), destination: destinationFolder,
                success: false, errorMessage: ex.Message);
            _dialogService.ShowError(ex.Message);
        }
    }

    // 仕様書20章：Alt+ドラッグでのショートカット作成。
    public void DropFilesAsShortcuts(IReadOnlyList<string> sourcePaths, string destinationFolder)
    {
        var targets = sourcePaths.Where(source => !IsSelfOrDescendantDrop(source, destinationFolder)).ToList();
        if (targets.Count == 0)
        {
            return;
        }

        try
        {
            var created = _fileSystemService.CreateShortcuts(targets, destinationFolder);

            if (created.Count > 0)
            {
                var description = created.Count == 1
                    ? $"「{Path.GetFileName(created[0])}」のショートカット作成"
                    : $"{created.Count}件のショートカット作成";
                _undoService.Record(
                    description,
                    () => _fileSystemService.Delete(created),
                    () => _fileSystemService.CreateShortcuts(targets, destinationFolder));
            }

            LogHistory("ショートカット作成", DescribeTargets(targets), originalLocation: DescribeSourceFolder(targets), destination: destinationFolder);
            RefreshCurrentFolder();
        }
        catch (AppOperationException ex)
        {
            LogHistory("ショートカット作成", DescribeTargets(targets), originalLocation: DescribeSourceFolder(targets),
                destination: destinationFolder, success: false, errorMessage: ex.Message);
            _dialogService.ShowError(ex.Message);
        }
    }

    private static bool IsSelfOrDescendantDrop(string sourcePath, string destinationFolder)
    {
        var normalizedSource = Path.TrimEndingDirectorySeparator(sourcePath);
        var normalizedDestination = Path.TrimEndingDirectorySeparator(destinationFolder);

        if (string.Equals(normalizedSource, normalizedDestination, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return normalizedDestination.StartsWith(normalizedSource + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
}
