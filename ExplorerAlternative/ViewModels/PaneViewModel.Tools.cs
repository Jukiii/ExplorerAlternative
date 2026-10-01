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
/// PaneViewModelのうち、タグ・外部ツール・一括名前変更（仕様書22・27章）。
/// </summary>
public sealed partial class PaneViewModel
{
    // 仕様書6.2章：ファイル・フォルダへのタグ付与/解除。選択中に未付与のノードが1件でもあれば
    // 選択全体に付与し、全て付与済みであれば選択全体から解除する（Finderのタグ操作に準拠）。
    private void ToggleTag(TagDefinition tag)
    {
        if (SelectedNodes.Count == 0)
        {
            return;
        }

        var shouldAssign = SelectedNodes.Any(n => !n.Tags.Contains(tag.Name));

        foreach (var node in SelectedNodes)
        {
            var assignment = _settingsService.Current.TagAssignments
                .FirstOrDefault(a => string.Equals(a.Path, node.FullPath, StringComparison.OrdinalIgnoreCase));

            if (shouldAssign)
            {
                if (assignment is null)
                {
                    assignment = new TagAssignment { Path = node.FullPath };
                    _settingsService.Current.TagAssignments.Add(assignment);
                }

                if (!assignment.Tags.Contains(tag.Name))
                {
                    assignment.Tags.Add(tag.Name);
                }
            }
            else if (assignment is not null)
            {
                assignment.Tags.Remove(tag.Name);

                if (assignment.Tags.Count == 0)
                {
                    _settingsService.Current.TagAssignments.Remove(assignment);
                }
            }

            node.RaiseTagsChanged();
        }

        _settingsService.Save();
    }

    public void RunExternalTool(ExternalToolDefinition tool)
    {
        var target = PrimarySelectedNode;
        if (target is null)
        {
            return;
        }

        try
        {
            _externalToolService.Run(tool, target.FullPath);
        }
        catch (AppOperationException ex)
        {
            _dialogService.ShowError(ex.Message);
        }
    }

    // プレビュー（BulkRenameViewModel.PreviewItems）で計算済みの新しい名前をそのまま使う。
    // パターン展開と検索/置換のどちらのモードでも、プレビューと実際の結果が食い違わないようにするため。
    public void BulkRename(IReadOnlyList<FileSystemNodeViewModel> targets, IReadOnlyList<BulkRenamePreviewItem> previewItems)
    {
        var renamed = new List<(string OldPath, string NewPath, string OldName, string NewName)>();

        for (var i = 0; i < targets.Count && i < previewItems.Count; i++)
        {
            try
            {
                var oldName = targets[i].Name;
                var oldPath = targets[i].FullPath;
                var newName = previewItems[i].NewName;
                var newPath = Path.Combine(Path.GetDirectoryName(oldPath) ?? CurrentPath, newName);
                _fileSystemService.Rename(oldPath, newName);
                renamed.Add((oldPath, newPath, oldName, newName));
            }
            catch (AppOperationException ex)
            {
                _dialogService.ShowError(ex.Message);
                break;
            }
        }

        if (renamed.Count > 0)
        {
            var description = renamed.Count == 1 ? "1件の名前変更" : $"{renamed.Count}件の名前変更";
            _undoService.Record(
                description,
                () =>
                {
                    foreach (var (_, newPath, oldName, _) in renamed)
                    {
                        _fileSystemService.Rename(newPath, oldName);
                    }
                },
                () =>
                {
                    foreach (var (oldPath, _, _, newName) in renamed)
                    {
                        _fileSystemService.Rename(oldPath, newName);
                    }
                });

            LogHistory("一括リネーム", description, originalLocation: CurrentPath);
        }

        RefreshCurrentFolder();
    }

    private void BulkRenameSelection()
    {
        var targets = SelectedNodes.ToList();
        if (targets.Count <= 1)
        {
            return;
        }

        var viewModel = new BulkRenameViewModel(targets);

        if (_dialogService.ShowBulkRename(viewModel))
        {
            BulkRename(targets, viewModel.PreviewItems.ToList());
        }
    }
}
