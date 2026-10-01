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
/// PaneViewModelのうち、Patch・Git/SVN操作・Diff（仕様書21〜24章）。
/// </summary>
public sealed partial class PaneViewModel
{
    // 仕様書14.1章：現在の変更内容（git diff / svn diff）からPatchファイルを作成する。
    private void CreatePatch()
    {
        var defaultName = VcsInfo.Kind == VersionControlKind.Git ? "changes.patch" : "changes.diff";
        var outputPath = _dialogService.ShowSaveFileDialog(
            "Patchの作成",
            "Patchファイル (*.patch;*.diff)|*.patch;*.diff|すべてのファイル (*.*)|*.*",
            defaultName);

        if (outputPath is null)
        {
            return;
        }

        try
        {
            _patchService.CreatePatch(VcsInfo, outputPath);
            _dialogService.ShowInfo($"Patchを作成しました。\n{outputPath}");
        }
        catch (AppOperationException ex)
        {
            _dialogService.ShowError(ex.Message);
        }
    }

    // 仕様書14.2章・24章：Patchファイルを選択し、内容確認ダイアログで承認後に
    // 現在のGit/SVN管理フォルダへ適用する。
    private void ApplyPatch()
    {
        var patchPath = _dialogService.ShowOpenFileDialog(
            "Patchの適用",
            "Patchファイル (*.patch;*.diff)|*.patch;*.diff|すべてのファイル (*.*)|*.*");

        if (patchPath is null)
        {
            return;
        }

        string patchText;
        try
        {
            patchText = File.ReadAllText(patchPath);
        }
        catch (IOException ex)
        {
            _dialogService.ShowError($"Patchファイルを読み込めませんでした。({ex.Message})");
            return;
        }

        var preview = PatchPreviewViewModel.Create(patchPath, VcsInfo.RootPath ?? CurrentPath, patchText);
        if (!_dialogService.ShowPatchPreview(preview))
        {
            return;
        }

        try
        {
            _patchService.ApplyPatch(VcsInfo, patchPath);
            RefreshCurrentFolder();
            _dialogService.ShowInfo("Patchを適用しました。");
        }
        catch (AppOperationException ex)
        {
            _dialogService.ShowError(ex.Message);
        }
    }

    // 仕様書13章・20章：Git/SVN操作。コマンドの組み立てはサービスに委譲し、実行は
    // 統合ターミナル（9章）で行う（資格情報プロンプト等の対話にも対応できるようにするため）。
    private void StageAll() => RunVcsCommand(() => _versionControlOperationsService.BuildStageCommand(VcsInfo));

    private void Commit()
    {
        var message = _dialogService.PromptText("コミット", "コミットメッセージを入力してください。");
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        RunVcsCommand(() => _versionControlOperationsService.BuildCommitCommand(VcsInfo, message));
    }

    private void Push() => RunVcsCommand(() => _versionControlOperationsService.BuildPushCommand(VcsInfo));

    private void Pull() => RunVcsCommand(() => _versionControlOperationsService.BuildPullCommand(VcsInfo));

    private void Update() => RunVcsCommand(() => _versionControlOperationsService.BuildUpdateCommand(VcsInfo));

    private void Fetch() => RunVcsCommand(() => _versionControlOperationsService.BuildFetchCommand(VcsInfo));

    private void Stash() => RunVcsCommand(() => _versionControlOperationsService.BuildStashCommand(VcsInfo));

    private void StashPop() => RunVcsCommand(() => _versionControlOperationsService.BuildStashPopCommand(VcsInfo));

    // 仕様書21章「Discard Changes」・22章「Revert」。破棄は元に戻せないため確認する（27章）。
    private void DiscardChanges()
    {
        var target = PrimarySelectedNode;
        if (target is null)
        {
            return;
        }

        if (!_dialogService.Confirm($"「{target.Name}」への変更を破棄します。この操作は元に戻せません。よろしいですか？"))
        {
            return;
        }

        RunVcsCommand(() => _versionControlOperationsService.BuildDiscardCommand(VcsInfo, target.FullPath));
    }

    // 仕様書21章「ブランチ一覧・Checkout」。未コミット変更がある場合は安全確認する。
    private void ShowBranches()
    {
        var branches = _versionControlService.GetBranches(VcsInfo);
        if (branches.Count == 0)
        {
            _dialogService.ShowInfo("ブランチが見つかりませんでした。");
            return;
        }

        var selected = _dialogService.SelectFromList("ブランチを切り替え", "チェックアウトするブランチを選択してください。", branches);
        if (selected is null)
        {
            return;
        }

        if (HasUncommittedChanges() && !_dialogService.Confirm("未コミットの変更があります。ブランチを切り替えるとコミットされていない変更に影響する場合があります。続行しますか？"))
        {
            return;
        }

        RunVcsCommand(() => _versionControlOperationsService.BuildCheckoutBranchCommand(VcsInfo, selected));
    }

    private void CreateBranch()
    {
        var name = _dialogService.PromptText("新規ブランチ作成", "ブランチ名を入力してください。");
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        RunVcsCommand(() => _versionControlOperationsService.BuildCreateBranchCommand(VcsInfo, name));
    }

    private void Merge()
    {
        var branches = _versionControlService.GetBranches(VcsInfo);
        var selected = _dialogService.SelectFromList("Merge", "マージするブランチを選択してください。", branches);
        if (selected is null)
        {
            return;
        }

        RunVcsCommand(() => _versionControlOperationsService.BuildMergeCommand(VcsInfo, selected));
    }

    private void Rebase()
    {
        var branches = _versionControlService.GetBranches(VcsInfo);
        var selected = _dialogService.SelectFromList("Rebase", "Rebase先のブランチを選択してください。", branches);
        if (selected is null)
        {
            return;
        }

        RunVcsCommand(() => _versionControlOperationsService.BuildRebaseCommand(VcsInfo, selected));
    }

    private bool HasUncommittedChanges() => _vcsStatusByPath.Count > 0;

    // 仕様書21章「Initialize」。現在のフォルダをGitリポジトリとして初期化する。
    private void InitRepository()
    {
        if (!_dialogService.Confirm($"「{CurrentPath}」をGitリポジトリとして初期化します。よろしいですか？"))
        {
            return;
        }

        try
        {
            RunTerminalCommandRequested?.Invoke(_versionControlOperationsService.BuildInitCommand(CurrentPath));
        }
        catch (AppOperationException ex)
        {
            _dialogService.ShowError(ex.Message);
        }
    }

    // 仕様書21章「Clone」。現在のフォルダへリポジトリをCloneする。
    private void CloneRepository()
    {
        var url = _dialogService.PromptText("Clone", "Clone元のリポジトリURLを入力してください。");
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        try
        {
            RunTerminalCommandRequested?.Invoke(_versionControlOperationsService.BuildCloneCommand(CurrentPath, url));
        }
        catch (AppOperationException ex)
        {
            _dialogService.ShowError(ex.Message);
        }
    }

    // 仕様書21章「Log」「Show Commit」：コミット履歴一覧と、選択コミットの変更内容を表示する。
    private void ShowLog(CommitLogEntry? initialSelection)
    {
        var repoName = VcsInfo.RootPath is null ? string.Empty : Path.GetFileName(VcsInfo.RootPath.TrimEnd('\\', '/'));
        var viewModel = new GitLogViewModel(_versionControlService, VcsInfo, $"Log - {repoName}", initialSelection);
        _dialogService.ShowGitLog(viewModel);
    }

    // 仕様書23章「Show Diff」：選択ファイルのコミット済み内容と現在の内容を比較する。
    private void ShowDiff()
    {
        var target = PrimarySelectedNode;
        if (target is null)
        {
            return;
        }

        var committedText = _versionControlService.GetCommittedFileContent(VcsInfo, target.FullPath);

        string currentText;
        try
        {
            currentText = _fileSystemService.ReadTextPreview(target.FullPath, 5_000_000, out _);
        }
        catch (AppOperationException ex)
        {
            _dialogService.ShowError(ex.Message);
            return;
        }

        var diffViewModel = DiffViewModel.Create(
            target.Name,
            committedText is null ? "(新規)" : "コミット済み",
            "現在の内容",
            committedText ?? string.Empty,
            currentText,
            _diffService,
            _dialogService);

        _dialogService.ShowDiff(diffViewModel);
    }

    // 仕様書25章「比較対象として保持」・「比較対象と比較」。Git管理外でも利用できる。
    private void HoldForComparison()
    {
        var target = PrimarySelectedNode;
        if (target is null)
        {
            return;
        }

        _heldComparisonPath = target.FullPath;
    }

    private void CompareWithHeld()
    {
        var target = PrimarySelectedNode;
        if (target is null || _heldComparisonPath is null)
        {
            return;
        }

        ShowFileDiff(_heldComparisonPath, target.FullPath, $"{Path.GetFileName(_heldComparisonPath)} ⇔ {target.Name}");
    }

    // 「比較対象として保持」→「比較対象と比較」の2手順を省き、ファイルを2つ選択した状態から
    // 直接比較する（保持したパスは変更しない）。
    private void CompareSelected()
    {
        if (SelectedNodes.Count != 2)
        {
            return;
        }

        var left = SelectedNodes[0];
        var right = SelectedNodes[1];

        ShowFileDiff(left.FullPath, right.FullPath, $"{left.Name} ⇔ {right.Name}");
    }

    private void ShowFileDiff(string leftPath, string rightPath, string title)
    {
        string leftText;
        string rightText;

        try
        {
            leftText = _fileSystemService.ReadTextPreview(leftPath, 5_000_000, out _);
            rightText = _fileSystemService.ReadTextPreview(rightPath, 5_000_000, out _);
        }
        catch (AppOperationException ex)
        {
            _dialogService.ShowError(ex.Message);
            return;
        }

        var diffViewModel = DiffViewModel.Create(title, leftPath, rightPath, leftText, rightText, _diffService, _dialogService);
        _dialogService.ShowDiff(diffViewModel);
    }

    private void RunVcsCommand(Func<string> buildCommand)
    {
        try
        {
            RunTerminalCommandRequested?.Invoke(buildCommand());
        }
        catch (AppOperationException ex)
        {
            _dialogService.ShowError(ex.Message);
        }
    }
}
