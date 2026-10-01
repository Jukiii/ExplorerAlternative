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
/// PaneViewModelのうち、作成・名前変更・削除・コピー・貼り付け・複製と、操作キュー・履歴・Undo（仕様書20・26・30・31章）。
/// </summary>
public sealed partial class PaneViewModel
{
    private void CreateNewFolder()
    {
        var name = _dialogService.PromptText("新しいフォルダ", "フォルダ名を入力してください。", "新しいフォルダ");
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        try
        {
            var parentPath = CurrentPath;
            _fileSystemService.CreateDirectory(parentPath, name);
            var createdPath = Path.Combine(parentPath, name);
            _undoService.Record(
                $"「{name}」の新規作成",
                () => _fileSystemService.Delete(new[] { createdPath }),
                () => _fileSystemService.CreateDirectory(parentPath, name));
            LogHistory("新規作成", name, destination: CurrentPath);
            RefreshCurrentFolder();
        }
        catch (AppOperationException ex)
        {
            LogHistory("新規作成", name, destination: CurrentPath, success: false, errorMessage: ex.Message);
            _dialogService.ShowError(ex.Message);
        }
    }

    // 仕様書48章：新規ファイル作成。
    private void CreateNewFile()
    {
        var name = _dialogService.PromptText("新しいファイル", "ファイル名を入力してください。", "新しいファイル.txt");
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        try
        {
            var parentPath = CurrentPath;
            _fileSystemService.CreateFile(parentPath, name);
            var createdPath = Path.Combine(parentPath, name);
            _undoService.Record(
                $"「{name}」の新規作成",
                () => _fileSystemService.Delete(new[] { createdPath }),
                () => _fileSystemService.CreateFile(parentPath, name));
            LogHistory("新規作成", name, destination: CurrentPath);
            RefreshCurrentFolder();
        }
        catch (AppOperationException ex)
        {
            LogHistory("新規作成", name, destination: CurrentPath, success: false, errorMessage: ex.Message);
            _dialogService.ShowError(ex.Message);
        }
    }

    // 仕様書48章：プロパティ表示。
    private void ShowPropertiesForSelection()
    {
        var target = PrimarySelectedNode;
        if (target is null)
        {
            return;
        }

        var viewModel = PropertiesViewModel.Create(target, _fileSystemService);
        _dialogService.ShowProperties(viewModel);
    }

    // 仕様書28章：各種パスコピー。kindは"FullPath"/"FileName"/"FolderPath"/"RelativePath"/"Uri"。
    private void CopyPath(string kind)
    {
        var target = PrimarySelectedNode;
        if (target is null)
        {
            return;
        }

        var text = kind switch
        {
            "FullPath" => target.FullPath,
            "FileName" => target.Name,
            "FolderPath" => Path.GetDirectoryName(target.FullPath) ?? target.FullPath,
            "RelativePath" => Path.GetRelativePath(CurrentPath, target.FullPath),
            "Uri" => new Uri(target.FullPath).AbsoluteUri,
            _ => target.FullPath
        };

        try
        {
            Clipboard.SetText(text);
        }
        catch (ExternalException)
        {
            _dialogService.ShowError("パスをコピーできませんでした。");
        }
    }


    private void RenameSelection()
    {
        var target = PrimarySelectedNode;
        if (target is null)
        {
            return;
        }

        var newName = _dialogService.PromptText("名前の変更", "新しい名前を入力してください。", target.Name);
        if (string.IsNullOrWhiteSpace(newName) || newName == target.Name)
        {
            return;
        }

        try
        {
            var oldName = target.Name;
            var oldParent = Path.GetDirectoryName(target.FullPath) ?? CurrentPath;
            var oldPath = target.FullPath;
            var newPath = Path.Combine(oldParent, newName);
            _fileSystemService.Rename(oldPath, newName);
            _undoService.Record(
                $"「{oldName}」→「{newName}」の名前変更",
                () => _fileSystemService.Rename(newPath, oldName),
                () => _fileSystemService.Rename(oldPath, newName));
            LogHistory("名前変更", oldName, originalLocation: oldParent, destination: newName);
            RefreshCurrentFolder();
        }
        catch (AppOperationException ex)
        {
            LogHistory("名前変更", target.Name, originalLocation: CurrentPath, success: false, errorMessage: ex.Message);
            _dialogService.ShowError(ex.Message);
        }
    }

    // 仕様書62章「Undo」：ごみ箱からの復元にはWindowsごみ箱APIとの連携（COM相互運用）が
    // 必要になるため、削除はUndo対象外とする（ごみ箱から手動復元、またはWindows
    // エクスプローラー自体のUndoで対応可能）。
    private void DeleteSelection()
    {
        if (SelectedNodes.Count == 0)
        {
            return;
        }

        if (!_dialogService.Confirm($"選択した{SelectedNodes.Count}件をごみ箱へ移動しますか？"))
        {
            return;
        }

        var targetPaths = SelectedNodes.Select(n => n.FullPath).ToList();
        var target = targetPaths.Count == 1 ? Path.GetFileName(targetPaths[0]) : $"{targetPaths.Count}件";

        try
        {
            _fileSystemService.Delete(targetPaths);
            LogHistory("削除", target, originalLocation: CurrentPath);
            RefreshCurrentFolder();
        }
        catch (AppOperationException ex)
        {
            LogHistory("削除", target, originalLocation: CurrentPath, success: false, errorMessage: ex.Message);
            _dialogService.ShowError(ex.Message);
        }
    }

    private void CopySelectionToClipboard(bool isCut)
    {
        if (SelectedNodes.Count == 0)
        {
            return;
        }

        var fileList = new StringCollection();
        fileList.AddRange(SelectedNodes.Select(n => n.FullPath).ToArray());

        var dataObject = new DataObject();
        dataObject.SetFileDropList(fileList);

        var effect = isCut ? DragDropEffects.Move : DragDropEffects.Copy;
        dataObject.SetData(DropEffectFormat, new MemoryStream(BitConverter.GetBytes((int)effect)));

        Clipboard.SetDataObject(dataObject, true);
    }

    // 仕様書19章：登録済みのSSH接続先を一覧から選んで、接続を依頼する。
    private void OpenSshTerminal()
    {
        var profiles = _settingsService.Current.SshProfiles;
        if (profiles.Count == 0)
        {
            _dialogService.ShowInfo("SSHの接続先が登録されていません。「☰ メニュー」→「SSH接続の管理...」から登録してください。");
            return;
        }

        // 同じ表示になる接続先があっても、区別して選べるようにする。
        var labels = new List<string>();
        foreach (var profile in profiles)
        {
            var label = string.IsNullOrWhiteSpace(profile.UserName)
                ? $"{profile.DisplayName}（{profile.Host}）"
                : $"{profile.DisplayName}（{profile.UserName}@{profile.Host}）";

            var unique = label;
            for (var i = 2; labels.Contains(unique); i++)
            {
                unique = $"{label} #{i}";
            }

            labels.Add(unique);
        }

        var choice = _dialogService.SelectFromList("SSHターミナルを開く", "接続先を選んでください。", labels);
        if (choice is null)
        {
            return;
        }

        var index = labels.IndexOf(choice);
        if (index >= 0)
        {
            SshTerminalRequested?.Invoke(profiles[index]);
        }
    }

    // 仕様書19章「TerminalからExplorerへのドラッグ」：ターミナルで選んだ文字列（パス）をペインへドロップしたとき、
    // それが実在するファイル・フォルダを指していれば、そこへ移動する（ファイルなら、その場所を開いて選択する）。
    public bool CanNavigateToDroppedPath(string? text) =>
        DroppedPathResolver.TryResolve(text, CurrentPath, out _, out _);

    public bool NavigateToDroppedPath(string? text)
    {
        if (!DroppedPathResolver.TryResolve(text, CurrentPath, out var path, out var isDirectory))
        {
            _dialogService.ShowInfo("ドロップした文字列は、存在するファイル・フォルダのパスではありません。");
            return false;
        }

        if (isDirectory)
        {
            NavigateTo(path);
            return true;
        }

        var parent = Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(parent))
        {
            return false;
        }

        if (!string.Equals(parent, CurrentPath, StringComparison.OrdinalIgnoreCase))
        {
            NavigateTo(parent);
        }

        // ファイルは、その場所を開いたうえで、選択状態にする。
        var node = RootNodes.FirstOrDefault(n => string.Equals(n.FullPath, path, StringComparison.OrdinalIgnoreCase));
        if (node is not null)
        {
            node.IsSelected = true;
        }

        return true;
    }

    private const string QuickBrowseLabel = "フォルダを参照...";

    // 仕様書60章：最近のコピー先・移動先（ファイル操作履歴から求める）を一覧で示し、選んだ宛先へ
    // 選択項目をコピー/移動する。実際の処理は、ドラッグ&ドロップと同じ経路（確認・キュー・
    // Undo/Redo・履歴記録）を使うので、動作は他の移動・コピーと変わらない。
    private void QuickTransfer(bool isMove)
    {
        var sources = SelectedNodes.Select(n => n.FullPath).ToList();
        if (sources.Count == 0)
        {
            return;
        }

        var verb = isMove ? "移動" : "コピー";
        var recent = QuickDestinations.GetRecent(_fileOperationHistoryService.GetAll(), isMove, excludeFolder: CurrentPath);
        var items = recent.Append(QuickBrowseLabel).ToList();

        var choice = _dialogService.SelectFromList(
            $"クイック{verb}",
            recent.Count > 0
                ? $"{verb}先を選んでください（最近の{verb}先）。"
                : $"最近の{verb}先はまだありません。「{QuickBrowseLabel}」で選んでください。",
            items);

        if (choice is null)
        {
            return;
        }

        var destination = choice == QuickBrowseLabel
            ? _dialogService.ShowOpenFolderDialog($"{verb}先のフォルダを選択")
            : choice;

        if (string.IsNullOrWhiteSpace(destination))
        {
            return;
        }

        if (!Directory.Exists(destination))
        {
            _dialogService.ShowError($"フォルダ「{destination}」が見つかりません。");
            return;
        }

        if (sources.All(source => IsNoOpOrInvalidDrop(source, destination)))
        {
            _dialogService.ShowInfo($"選んだ項目は、すでにそのフォルダにあるか、そのフォルダ自身（の中）なので、{verb}できません。");
            return;
        }

        DropFiles(sources, destination, isMove);
    }

    private void PasteFromClipboard()
    {
        if (!Clipboard.ContainsFileDropList())
        {
            return;
        }

        var files = Clipboard.GetFileDropList().Cast<string>().ToList();
        if (files.Count == 0)
        {
            return;
        }

        var isMove = false;
        var dataObject = Clipboard.GetDataObject();

        if (dataObject?.GetDataPresent(DropEffectFormat) == true &&
            dataObject.GetData(DropEffectFormat) is MemoryStream stream)
        {
            var buffer = new byte[4];
            stream.Position = 0;
            _ = stream.Read(buffer, 0, buffer.Length);
            var effect = (DragDropEffects)BitConverter.ToInt32(buffer, 0);
            isMove = effect.HasFlag(DragDropEffects.Move);
        }

        if (!ConfirmOperationIfNeeded(isMove ? "移動" : "コピー", files, CurrentPath))
        {
            return;
        }

        EnqueueOperation(isMove ? "移動" : "コピー", files, CurrentPath, isMove);
    }

    private static string DescribeTargets(IReadOnlyList<string> paths) =>
        paths.Count == 1 ? Path.GetFileName(paths[0]) : $"{paths.Count}件";

    private static string? DescribeSourceFolder(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0)
        {
            return null;
        }

        var folders = paths
            .Select(p => Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(p)) ?? p)
            .Distinct()
            .ToList();

        return folders.Count == 1 ? folders[0] : string.Join(", ", folders);
    }

    private void LogHistory(
        string operation,
        string target,
        string? originalLocation = null,
        string? destination = null,
        bool success = true,
        string? errorMessage = null)
    {
        _fileOperationHistoryService.Record(new FileOperationHistoryEntry
        {
            Timestamp = DateTime.Now,
            Operation = operation,
            Target = target,
            OriginalLocation = originalLocation,
            Destination = destination,
            Success = success,
            ErrorMessage = errorMessage
        });
    }

    // 仕様書26章「ファイル操作キュー」：大量コピー/移動をバックグラウンドのキューへ委譲する。
    // 完了後の処理（Undo記録・履歴記録・一覧更新）はOnQueueItemCompletedで行う。
    private void EnqueueOperation(string kind, IReadOnlyList<string> sourcePaths, string destinationFolder, bool isMove)
    {
        var item = _fileOperationQueueService.Enqueue(kind, sourcePaths, destinationFolder, isMove);
        _ownedQueueItemIds.Add(item.Id);
    }

    // キューは全ペイン共有のため、自分（このペイン）がEnqueueした項目のみ処理する。
    private void OnQueueItemCompleted(FileOperationQueueItem item)
    {
        if (!_ownedQueueItemIds.Remove(item.Id))
        {
            return;
        }

        if (item.CompletedSourcePaths.Count > 0)
        {
            if (item.IsMove)
            {
                RecordMoveUndo(item.CompletedSourcePaths, item.DestinationFolder);
            }
            else
            {
                RecordCopyUndo(item.CompletedSourcePaths, item.DestinationFolder);
            }
        }

        var succeeded = item.Status == FileOperationQueueItemStatus.Completed;
        LogHistory(
            item.Kind,
            DescribeTargets(item.SourcePaths),
            originalLocation: DescribeSourceFolder(item.SourcePaths),
            destination: item.DestinationFolder,
            success: succeeded,
            errorMessage: item.ErrorMessage);

        if (!succeeded && item.Status == FileOperationQueueItemStatus.Failed)
        {
            _dialogService.ShowError($"{item.Kind}に失敗しました。({item.ErrorMessage})");
        }

        RefreshCurrentFolder();
    }

    // 仕様書62章「Undo」：移動は元の親フォルダへ戻す。選択項目が複数フォルダの階層に
    // またがっている場合に備え、項目ごとに元の親フォルダを個別に記録する。
    private void RecordMoveUndo(IReadOnlyList<string> sourcePaths, string destinationDirectory)
    {
        var items = sourcePaths
            .Select(source => (
                Source: source,
                Destination: Path.Combine(destinationDirectory, Path.GetFileName(source)),
                OriginalParent: Path.GetDirectoryName(source) ?? destinationDirectory))
            .ToList();

        var description = items.Count == 1
            ? $"「{Path.GetFileName(items[0].Destination)}」の移動"
            : $"{items.Count}件の移動";

        _undoService.Record(
            description,
            () =>
            {
                foreach (var item in items)
                {
                    _fileSystemService.Move(new[] { item.Destination }, item.OriginalParent);
                }
            },
            () =>
            {
                foreach (var item in items)
                {
                    _fileSystemService.Move(new[] { item.Source }, destinationDirectory);
                }
            });
    }

    private void RecordCopyUndo(IReadOnlyList<string> sourcePaths, string destinationDirectory)
    {
        var destinations = sourcePaths.Select(source => Path.Combine(destinationDirectory, Path.GetFileName(source))).ToList();

        var description = destinations.Count == 1
            ? $"「{Path.GetFileName(destinations[0])}」のコピー"
            : $"{destinations.Count}件のコピー";

        var sources = sourcePaths.ToList();
        _undoService.Record(
            description,
            () => _fileSystemService.Delete(destinations),
            () => _fileSystemService.Copy(sources, destinationDirectory));
    }

    private void DuplicateSelection()
    {
        if (SelectedNodes.Count == 0)
        {
            return;
        }

        var targets = SelectedNodes.Select(n => n.FullPath).ToList();

        try
        {
            var created = _fileSystemService.Duplicate(targets);

            if (created.Count > 0)
            {
                var description = created.Count == 1
                    ? $"「{Path.GetFileName(created[0])}」の複製"
                    : $"{created.Count}件の複製";
                _undoService.Record(
                    description,
                    () => _fileSystemService.Delete(created),
                    () => _fileSystemService.Duplicate(targets));
            }

            LogHistory("複製", DescribeTargets(targets), originalLocation: CurrentPath, destination: CurrentPath);
            RefreshCurrentFolder();
        }
        catch (AppOperationException ex)
        {
            LogHistory("複製", DescribeTargets(targets), originalLocation: CurrentPath, success: false, errorMessage: ex.Message);
            _dialogService.ShowError(ex.Message);
        }
    }

    // 仕様書20章「移動」に対応するドラッグ&ドロップ本体。ドロップ先フォルダの内部/子孫への
    // 移動・コピーや、同じフォルダへの無意味なドロップは黙って無視する（既存フォルダへの
    // File.Move/Directory.Move例外を避けるための最小限の防御）。
    /// <summary>仕様書32章「ファイル操作プレビュー」：設定でONの場合のみ、実行前に対象件数と
    /// 移動元/移動先を確認する。OFF時（既定）は常にtrueを返す。</summary>
    private bool ConfirmOperationIfNeeded(string verb, IReadOnlyList<string> targets, string destinationFolder)
    {
        if (!_settingsService.Current.View.ConfirmMoveAndCopy)
        {
            return true;
        }

        var sourceFolder = targets.Count == 1
            ? Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(targets[0])) ?? targets[0]
            : string.Join(", ", targets.Select(t => Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(t)) ?? t).Distinct());

        return _dialogService.Confirm(
            $"{targets.Count}個の項目を{verb}します。\n\n移動元：{sourceFolder}\n移動先：{destinationFolder}");
    }
}
