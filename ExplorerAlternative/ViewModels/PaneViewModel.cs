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
/// メインペイン1枠分のビューモデル（仕様書4章・19章）。将来の分割ペイン対応のため、
/// タブは複数のPaneViewModelを保持できる構造にしている（Phase 1では1タブ1ペイン）。
/// </summary>
public sealed class PaneViewModel : ObservableObject, IDisposable
{
    private const string DropEffectFormat = "Preferred DropEffect";

    private readonly IFileSystemService _fileSystemService;
    private readonly IDialogService _dialogService;
    private readonly IVersionControlService _versionControlService;
    private readonly IExternalToolService _externalToolService;
    private readonly ISettingsService _settingsService;
    private readonly IPatchService _patchService;
    private readonly IVersionControlOperationsService _versionControlOperationsService;
    private readonly IDiffService _diffService;
    private readonly IProjectDetectionService _projectDetectionService;
    private readonly IFolderWatcherService _folderWatcherService;
    private readonly Stack<string> _backStack = new();
    private readonly Stack<string> _forwardStack = new();
    private DispatcherTimer? _externalChangeDebounceTimer;
    private bool _isDisposed;

    // 仕様書25章「比較対象として保持」。複数タブ・複数ペインをまたいで1つだけ保持すればよいため、
    // 専用のサービスを新設せずインスタンス間で共有するstaticフィールドとしている。
    private static string? _heldComparisonPath;

    private string _currentPath;
    private ViewMode _currentViewMode;
    private bool _isAddressEditing;
    private string _addressEditText = string.Empty;
    private FileSystemNodeViewModel? _primarySelectedNode;
    private VersionControlInfo _vcsInfo = VersionControlInfo.None;
    private IReadOnlyDictionary<string, string> _vcsStatusByPath = new Dictionary<string, string>();
    private bool _isActive;
    private ProjectInfo? _currentProject;
    private string? _solutionRootPath;
    private string? _lastCommitLogRoot;

    public PaneViewModel(
        IFileSystemService fileSystemService,
        IDialogService dialogService,
        IVersionControlService versionControlService,
        IExternalToolService externalToolService,
        ISettingsService settingsService,
        IPatchService patchService,
        IVersionControlOperationsService versionControlOperationsService,
        IDiffService diffService,
        IProjectDetectionService projectDetectionService,
        Func<IFolderWatcherService> folderWatcherServiceFactory,
        string initialPath,
        ViewMode initialViewMode)
    {
        _fileSystemService = fileSystemService;
        _dialogService = dialogService;
        _versionControlService = versionControlService;
        _externalToolService = externalToolService;
        _settingsService = settingsService;
        _patchService = patchService;
        _versionControlOperationsService = versionControlOperationsService;
        _diffService = diffService;
        _projectDetectionService = projectDetectionService;
        _folderWatcherService = folderWatcherServiceFactory();
        _folderWatcherService.Changed += OnFolderChangedExternally;
        _currentPath = initialPath;
        _currentViewMode = initialViewMode;

        NavigateToCommand = new RelayCommand(p => NavigateTo((string)p!));
        GoUpCommand = new RelayCommand(_ => GoUp());
        SetViewModeCommand = new RelayCommand(p => CurrentViewMode = (ViewMode)p!);
        ToggleShowHiddenFilesCommand = new RelayCommand(_ => ShowHiddenFiles = !ShowHiddenFiles);
        RefreshCommand = new RelayCommand(_ => RefreshCurrentFolder());
        ToggleExpandCommand = new RelayCommand(p => ((FileSystemNodeViewModel)p!).IsExpanded ^= true);
        OpenCommand = new RelayCommand(_ => OpenSelection(), _ => PrimarySelectedNode is not null);
        OpenInNewTabCommand = new RelayCommand(_ => OpenInNewTab(), _ => PrimarySelectedNode is { IsDirectory: true });
        OpenWithDefaultAppCommand = new RelayCommand(_ => OpenSelection(), _ => PrimarySelectedNode is { IsDirectory: false });
        OpenWithBrowseCommand = new RelayCommand(_ => OpenWithBrowse(), _ => PrimarySelectedNode is { IsDirectory: false });
        OpenInWindowsExplorerCommand = new RelayCommand(_ => OpenInWindowsExplorer(), _ => PrimarySelectedNode is not null);
        OpenPowerShellHereCommand = new RelayCommand(_ => OpenPowerShellHere());
        OpenTerminalHereCommand = new RelayCommand(_ => OpenTerminalHere());
        NewFolderCommand = new RelayCommand(_ => CreateNewFolder());
        NewFileCommand = new RelayCommand(_ => CreateNewFile());
        ShowPropertiesCommand = new RelayCommand(_ => ShowPropertiesForSelection(), _ => PrimarySelectedNode is not null);
        CopyPathCommand = new RelayCommand(p => CopyPath((string)p!), _ => PrimarySelectedNode is not null);
        RenameCommand = new RelayCommand(_ => RenameSelection(), _ => PrimarySelectedNode is not null);
        DeleteCommand = new RelayCommand(_ => DeleteSelection(), _ => SelectedNodes.Count > 0);
        CopyCommand = new RelayCommand(_ => CopySelectionToClipboard(isCut: false), _ => SelectedNodes.Count > 0);
        CutCommand = new RelayCommand(_ => CopySelectionToClipboard(isCut: true), _ => SelectedNodes.Count > 0);
        PasteCommand = new RelayCommand(_ => PasteFromClipboard());
        DuplicateSelectionCommand = new RelayCommand(_ => DuplicateSelection(), _ => SelectedNodes.Count > 0);
        BeginAddressEditCommand = new RelayCommand(_ => BeginAddressEdit());
        CommitAddressEditCommand = new RelayCommand(_ => CommitAddressEdit());
        RunExternalToolCommand = new RelayCommand(p => RunExternalTool((ExternalToolDefinition)p!), _ => PrimarySelectedNode is not null);
        CancelAddressEditCommand = new RelayCommand(_ => IsAddressEditing = false);
        BulkRenameCommand = new RelayCommand(_ => BulkRenameSelection(), _ => SelectedNodes.Count > 1);
        GoBackCommand = new RelayCommand(_ => GoBack(), _ => CanGoBack);
        GoForwardCommand = new RelayCommand(_ => GoForward(), _ => CanGoForward);
        ToggleTagCommand = new RelayCommand(p => ToggleTag((TagDefinition)p!), _ => SelectedNodes.Count > 0);
        CreatePatchCommand = new RelayCommand(_ => CreatePatch(), _ => VcsInfo.Kind != VersionControlKind.None);
        ApplyPatchCommand = new RelayCommand(_ => ApplyPatch(), _ => VcsInfo.Kind != VersionControlKind.None);
        StageAllCommand = new RelayCommand(_ => StageAll(), _ => VcsInfo.Kind != VersionControlKind.None);
        CommitCommand = new RelayCommand(_ => Commit(), _ => VcsInfo.Kind != VersionControlKind.None);
        PushCommand = new RelayCommand(_ => Push(), _ => VcsInfo.Kind == VersionControlKind.Git);
        PullCommand = new RelayCommand(_ => Pull(), _ => VcsInfo.Kind == VersionControlKind.Git);
        UpdateCommand = new RelayCommand(_ => Update(), _ => VcsInfo.Kind == VersionControlKind.Svn);
        FetchCommand = new RelayCommand(_ => Fetch(), _ => VcsInfo.Kind == VersionControlKind.Git);
        StashCommand = new RelayCommand(_ => Stash(), _ => VcsInfo.Kind == VersionControlKind.Git);
        StashPopCommand = new RelayCommand(_ => StashPop(), _ => VcsInfo.Kind == VersionControlKind.Git);
        DiscardChangesCommand = new RelayCommand(_ => DiscardChanges(), _ => VcsInfo.Kind != VersionControlKind.None && PrimarySelectedNode is { IsDirectory: false });
        ShowBranchesCommand = new RelayCommand(_ => ShowBranches(), _ => VcsInfo.Kind == VersionControlKind.Git);
        CreateBranchCommand = new RelayCommand(_ => CreateBranch(), _ => VcsInfo.Kind == VersionControlKind.Git);
        MergeCommand = new RelayCommand(_ => Merge(), _ => VcsInfo.Kind == VersionControlKind.Git);
        RebaseCommand = new RelayCommand(_ => Rebase(), _ => VcsInfo.Kind == VersionControlKind.Git);
        InitRepositoryCommand = new RelayCommand(_ => InitRepository(), _ => VcsInfo.Kind == VersionControlKind.None);
        CloneRepositoryCommand = new RelayCommand(_ => CloneRepository());
        ShowDiffCommand = new RelayCommand(_ => ShowDiff(), _ => VcsInfo.Kind != VersionControlKind.None && PrimarySelectedNode is { IsDirectory: false });
        GoToProjectRootCommand = new RelayCommand(_ => NavigateTo(CurrentProject!.RootPath), _ => CurrentProject is not null && CurrentProject.RootPath != CurrentPath);
        GoToSolutionRootCommand = new RelayCommand(_ => NavigateTo(_solutionRootPath!), _ => _solutionRootPath is not null && _solutionRootPath != CurrentPath);
        GoToGitRootCommand = new RelayCommand(_ => NavigateTo(VcsInfo.RootPath!), _ => VcsInfo.Kind != VersionControlKind.None && VcsInfo.RootPath is not null && VcsInfo.RootPath != CurrentPath);
        HoldForComparisonCommand = new RelayCommand(_ => HoldForComparison(), _ => PrimarySelectedNode is { IsDirectory: false });
        CompareWithHeldCommand = new RelayCommand(
            _ => CompareWithHeld(),
            _ => PrimarySelectedNode is { IsDirectory: false } node &&
                 _heldComparisonPath is not null &&
                 !string.Equals(_heldComparisonPath, node.FullPath, StringComparison.OrdinalIgnoreCase));

        LoadPath(_currentPath);
    }

    public event Action<string>? PathChanged;

    /// <summary>選択中ノードが変わったときに発火する（Quick Look追従用、仕様書11章）。</summary>
    public event Action? SelectionChanged;

    /// <summary>Git/SVN操作コマンドを統合ターミナル（9章）で実行してもらうための橋渡し。</summary>
    public event Action<string>? RunTerminalCommandRequested;


    /// <summary>フォルダを新しいタブで開く（仕様書10章・37章）要求を、MainWindowViewModelへ委譲するための橋渡し。</summary>
    public event Action<string>? OpenInNewTabRequested;

    public ObservableCollection<FileSystemNodeViewModel> RootNodes { get; } = new();

    public ObservableCollection<FileSystemNodeViewModel> VisibleNodes { get; } = new();

    public ObservableCollection<BreadcrumbSegmentViewModel> BreadcrumbSegments { get; } = new();

    public ObservableCollection<FileSystemNodeViewModel> SelectedNodes { get; } = new();

    public RelayCommand NavigateToCommand { get; }

    public RelayCommand GoUpCommand { get; }

    public RelayCommand SetViewModeCommand { get; }

    public RelayCommand ToggleExpandCommand { get; }

    public RelayCommand OpenCommand { get; }

    public RelayCommand OpenInNewTabCommand { get; }

    /// <summary>仕様書34章「アプリで開く」：既定のアプリで開く（OpenCommandのファイル限定版）。</summary>
    public RelayCommand OpenWithDefaultAppCommand { get; }

    /// <summary>仕様書34章「アプリで開く」：任意のEXEを選択して開く（今回だけ指定）。</summary>
    public RelayCommand OpenWithBrowseCommand { get; }

    /// <summary>仕様書35章「Windows Explorerで開く」。</summary>
    public RelayCommand OpenInWindowsExplorerCommand { get; }

    /// <summary>仕様書19章「ここでPowerShellを開く」：外部ウィンドウとしてPowerShellを起動する。</summary>
    public RelayCommand OpenPowerShellHereCommand { get; }

    /// <summary>仕様書19章「ここでターミナルを開く」：統合ターミナル（9章）をこのフォルダで開く。</summary>
    public RelayCommand OpenTerminalHereCommand { get; }

    public RelayCommand NewFolderCommand { get; }

    public RelayCommand NewFileCommand { get; }

    public RelayCommand ShowPropertiesCommand { get; }

    public RelayCommand CopyPathCommand { get; }

    public RelayCommand RenameCommand { get; }

    public RelayCommand DeleteCommand { get; }

    public RelayCommand CopyCommand { get; }

    public RelayCommand CutCommand { get; }

    public RelayCommand PasteCommand { get; }

    /// <summary>Ctrl+D：選択したファイル・フォルダを同じ場所に複製する。</summary>
    public RelayCommand DuplicateSelectionCommand { get; }

    public RelayCommand BeginAddressEditCommand { get; }

    public RelayCommand CommitAddressEditCommand { get; }

    public RelayCommand CancelAddressEditCommand { get; }

    public RelayCommand RunExternalToolCommand { get; }

    public RelayCommand BulkRenameCommand { get; }

    public RelayCommand GoBackCommand { get; }

    public RelayCommand GoForwardCommand { get; }

    public RelayCommand ToggleTagCommand { get; }

    public RelayCommand CreatePatchCommand { get; }

    public RelayCommand ApplyPatchCommand { get; }

    public RelayCommand StageAllCommand { get; }

    public RelayCommand CommitCommand { get; }

    public RelayCommand PushCommand { get; }

    public RelayCommand PullCommand { get; }

    public RelayCommand UpdateCommand { get; }

    public RelayCommand FetchCommand { get; }

    public RelayCommand StashCommand { get; }

    public RelayCommand StashPopCommand { get; }

    /// <summary>仕様書21章「Discard Changes」・22章「Revert」。</summary>
    public RelayCommand DiscardChangesCommand { get; }

    /// <summary>仕様書21章「ブランチ一覧・Checkout」。</summary>
    public RelayCommand ShowBranchesCommand { get; }

    /// <summary>仕様書21章「新規ブランチ作成」。</summary>
    public RelayCommand CreateBranchCommand { get; }

    public RelayCommand MergeCommand { get; }

    public RelayCommand RebaseCommand { get; }

    /// <summary>仕様書21章「Initialize」。管理外フォルダでのみ有効。</summary>
    public RelayCommand InitRepositoryCommand { get; }

    /// <summary>仕様書21章「Clone」。</summary>
    public RelayCommand CloneRepositoryCommand { get; }

    /// <summary>仕様書23章「Show Diff」：選択ファイルをコミット済み内容と比較する。</summary>
    public RelayCommand ShowDiffCommand { get; }

    /// <summary>仕様書25章「比較対象として保持」。</summary>
    public RelayCommand HoldForComparisonCommand { get; }

    /// <summary>仕様書25章「比較対象と比較」。</summary>
    public RelayCommand CompareWithHeldCommand { get; }

    /// <summary>コンテキストメニューの「タグ」サブメニューに表示する、登録済みタグ一覧。</summary>
    public IReadOnlyList<TagDefinition> AvailableTags => _settingsService.Current.TagDefinitions;

    public bool CanGoBack => _backStack.Count > 0;

    public bool CanGoForward => _forwardStack.Count > 0;

    /// <summary>コンテキストメニューの「外部ツール」サブメニュー（仕様書22章）用。</summary>
    public IReadOnlyList<ExternalToolDefinition> ExternalTools => _settingsService.Current.ExternalTools;

    public string CurrentPath
    {
        get => _currentPath;
        private set => SetProperty(ref _currentPath, value);
    }

    /// <summary>「PC」相当（ドライブ一覧）にいるかどうか。</summary>
    public bool IsAtComputerRoot => string.IsNullOrEmpty(CurrentPath);

    public ViewMode CurrentViewMode
    {
        get => _currentViewMode;
        set => SetProperty(ref _currentViewMode, value);
    }

    /// <summary>仕様書49章「隠しファイル」。設定に永続化し、切り替え時に再読み込みする。</summary>
    public bool ShowHiddenFiles
    {
        get => _settingsService.Current.View.ShowHiddenFiles;
        set
        {
            if (_settingsService.Current.View.ShowHiddenFiles == value)
            {
                return;
            }

            _settingsService.Current.View.ShowHiddenFiles = value;
            _settingsService.Save();
            OnPropertyChanged();
            RefreshCurrentFolder();
        }
    }

    public RelayCommand ToggleShowHiddenFilesCommand { get; }

    /// <summary>仕様書46章のコマンドパレット例「Refresh」。F5でも実行できる。</summary>
    public RelayCommand RefreshCommand { get; }

    public bool IsAddressEditing
    {
        get => _isAddressEditing;
        private set => SetProperty(ref _isAddressEditing, value);
    }

    public string AddressEditText
    {
        get => _addressEditText;
        set => SetProperty(ref _addressEditText, value);
    }

    public FileSystemNodeViewModel? PrimarySelectedNode
    {
        get => _primarySelectedNode;
        private set => SetProperty(ref _primarySelectedNode, value);
    }

    public VersionControlInfo VcsInfo
    {
        get => _vcsInfo;
        private set => SetProperty(ref _vcsInfo, value);
    }

    /// <summary>仕様書21章「Log」：直近のコミット履歴（新しい順）。</summary>
    public ObservableCollection<CommitLogEntry> CommitLog { get; } = new();

    private void RefreshCommitLog()
    {
        CommitLog.Clear();
        _lastCommitLogRoot = VcsInfo.RootPath;

        foreach (var entry in _versionControlService.GetCommitLog(VcsInfo, maxCount: 20))
        {
            CommitLog.Add(entry);
        }
    }

    /// <summary>仕様書54章：現在パスまたはその祖先で検出されたプロジェクト。</summary>
    public ProjectInfo? CurrentProject
    {
        get => _currentProject;
        private set => SetProperty(ref _currentProject, value);
    }

    /// <summary>仕様書56章：現在パスから見えるプロジェクトが切り替わったときに通知する。</summary>
    public event Action<ProjectInfo>? ProjectDetected;

    public RelayCommand GoToProjectRootCommand { get; }

    public RelayCommand GoToSolutionRootCommand { get; }

    public RelayCommand GoToGitRootCommand { get; }

    public bool IsActive
    {
        get => _isActive;
        set => SetProperty(ref _isActive, value);
    }

    public void UpdateSelection(IEnumerable<FileSystemNodeViewModel> selected)
    {
        SelectedNodes.Clear();

        foreach (var node in selected)
        {
            SelectedNodes.Add(node);
        }

        PrimarySelectedNode = SelectedNodes.Count == 1 ? SelectedNodes[0] : SelectedNodes.LastOrDefault();
        SelectionChanged?.Invoke();
    }

    /// <summary>通常のナビゲーション（フォルダを開く・パンくず・お気に入り等）。戻る/進む履歴に積む。</summary>
    public void NavigateTo(string path)
    {
        if (path == CurrentPath)
        {
            LoadPath(path);
            return;
        }

        _backStack.Push(CurrentPath);
        _forwardStack.Clear();
        RaiseHistoryChanged();
        LoadPath(path);
    }

    public void GoBack()
    {
        if (!CanGoBack)
        {
            return;
        }

        _forwardStack.Push(CurrentPath);
        var target = _backStack.Pop();
        RaiseHistoryChanged();
        LoadPath(target);
    }

    public void GoForward()
    {
        if (!CanGoForward)
        {
            return;
        }

        _backStack.Push(CurrentPath);
        var target = _forwardStack.Pop();
        RaiseHistoryChanged();
        LoadPath(target);
    }

    private string GetVcsStatus(string fullPath) => _vcsStatusByPath.TryGetValue(fullPath, out var status) ? status : string.Empty;

    private void RaiseHistoryChanged()
    {
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(CanGoForward));
        GoBackCommand.RaiseCanExecuteChanged();
        GoForwardCommand.RaiseCanExecuteChanged();
    }

    /// <summary>実際にフォルダ内容を読み込んで表示を更新する（履歴には影響しない）。</summary>
    private void LoadPath(string path)
    {
        try
        {
            var entries = IsPathComputerRoot(path)
                ? _fileSystemService.GetDrives()
                : _fileSystemService.GetChildren(path);

            CurrentPath = path;

            VcsInfo = IsPathComputerRoot(path) ? VersionControlInfo.None : _versionControlService.Detect(path);
            _vcsStatusByPath = _versionControlService.GetFileStatuses(VcsInfo);
            _folderWatcherService.SetPath(IsPathComputerRoot(path) ? null : path);

            // 仕様書21章「Log」：VCSルートが変わった場合のみ取得し直す（同じリポジトリ内の
            // フォルダ移動のたびにgit/svnプロセスを起動しないようにするため）。
            if (VcsInfo.Kind != VersionControlKind.None && VcsInfo.RootPath != _lastCommitLogRoot)
            {
                RefreshCommitLog();
            }
            else if (VcsInfo.Kind == VersionControlKind.None && _lastCommitLogRoot is not null)
            {
                CommitLog.Clear();
                _lastCommitLogRoot = null;
            }

            var previousProjectRoot = CurrentProject?.RootPath;
            CurrentProject = IsPathComputerRoot(path) ? null : _projectDetectionService.Detect(path);
            _solutionRootPath = IsPathComputerRoot(path) ? null : _projectDetectionService.FindSolutionRoot(path);
            GoToProjectRootCommand.RaiseCanExecuteChanged();
            GoToSolutionRootCommand.RaiseCanExecuteChanged();
            GoToGitRootCommand.RaiseCanExecuteChanged();

            if (CurrentProject is not null && CurrentProject.RootPath != previousProjectRoot)
            {
                ProjectDetected?.Invoke(CurrentProject);
            }

            RootNodes.Clear();

            var showHidden = _settingsService.Current.View.ShowHiddenFiles;
            foreach (var entry in entries
                .Where(e => showHidden || !e.IsHidden)
                .OrderByDescending(e => e.IsDirectory)
                .ThenBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                RootNodes.Add(new FileSystemNodeViewModel(entry, 0, _fileSystemService, _dialogService, _settingsService, GetVcsStatus, RebuildVisibleNodes));
            }

            RebuildVisibleNodes();
            RebuildBreadcrumb();

            CreatePatchCommand.RaiseCanExecuteChanged();
            ApplyPatchCommand.RaiseCanExecuteChanged();
            StageAllCommand.RaiseCanExecuteChanged();
            CommitCommand.RaiseCanExecuteChanged();
            PushCommand.RaiseCanExecuteChanged();
            PullCommand.RaiseCanExecuteChanged();
            UpdateCommand.RaiseCanExecuteChanged();
            FetchCommand.RaiseCanExecuteChanged();
            StashCommand.RaiseCanExecuteChanged();
            StashPopCommand.RaiseCanExecuteChanged();
            ShowBranchesCommand.RaiseCanExecuteChanged();
            CreateBranchCommand.RaiseCanExecuteChanged();
            MergeCommand.RaiseCanExecuteChanged();
            RebaseCommand.RaiseCanExecuteChanged();
            InitRepositoryCommand.RaiseCanExecuteChanged();

            PathChanged?.Invoke(path);
        }
        catch (AppOperationException ex)
        {
            _dialogService.ShowError(ex.Message);
        }
    }

    /// <summary>現在フォルダを再読込する（履歴には積まない）。</summary>
    public void RefreshCurrentFolder() => LoadPath(CurrentPath);

    // 仕様書64章：FileSystemWatcherの通知は背景スレッドから来るため、UIスレッドへ
    // マーシャリングした上で、短時間に連続する変化をまとめるためデバウンスしてから更新する。
    private void OnFolderChangedExternally()
    {
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
        {
            if (_isDisposed)
            {
                return;
            }

            _externalChangeDebounceTimer ??= CreateDebounceTimer();
            _externalChangeDebounceTimer.Stop();
            _externalChangeDebounceTimer.Start();
        }));
    }

    private DispatcherTimer CreateDebounceTimer()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            RefreshCurrentFolder();
        };
        return timer;
    }

    /// <summary>タブ/ペインを閉じる際に呼び出し、フォルダ監視を解放する。</summary>
    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _externalChangeDebounceTimer?.Stop();
        _folderWatcherService.Changed -= OnFolderChangedExternally;
        _folderWatcherService.Dispose();
    }

    private static bool IsPathComputerRoot(string path) => string.IsNullOrEmpty(path);

    private void GoUp()
    {
        if (IsAtComputerRoot)
        {
            return;
        }

        var parent = _fileSystemService.GetParent(CurrentPath);
        NavigateTo(parent ?? string.Empty);
    }

    private void RebuildVisibleNodes()
    {
        VisibleNodes.Clear();
        AppendVisible(RootNodes);
    }

    private void AppendVisible(IEnumerable<FileSystemNodeViewModel> nodes)
    {
        foreach (var node in nodes)
        {
            VisibleNodes.Add(node);

            if (node.IsDirectory && node.IsExpanded && node.Children is not null)
            {
                AppendVisible(node.Children);
            }
        }
    }

    private void RebuildBreadcrumb()
    {
        BreadcrumbSegments.Clear();
        // 「PC」自身は親を持たないため、そのドロップダウンには自分の子（＝ドライブ一覧）を表示する。
        BreadcrumbSegments.Add(CreateSegment("PC", string.Empty, parentPath: null, remainder: string.Empty));

        if (IsAtComputerRoot)
        {
            return;
        }

        var root = Path.GetPathRoot(CurrentPath) ?? string.Empty;
        if (string.IsNullOrEmpty(root))
        {
            return;
        }

        var driveRoot = root.TrimEnd('\\');
        BreadcrumbSegments.Add(CreateSegment(driveRoot, root, parentPath: string.Empty, remainder: ComputeRemainder(root)));

        var relative = CurrentPath[root.Length..];
        var parts = relative.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        var accumulated = root;

        foreach (var part in parts)
        {
            var parentPath = accumulated;
            accumulated = Path.Combine(accumulated, part);
            BreadcrumbSegments.Add(CreateSegment(part, accumulated, parentPath, ComputeRemainder(accumulated)));
        }
    }

    // 仕様書11章：パンくずドロップダウンの右クリック「部分置換」用に、指定した階層より下の
    // パスを算出する（例：現在パスが C:\aa\2026\05 で階層が C:\aa の場合は "2026\05"）。
    private string ComputeRemainder(string segmentPath)
    {
        return CurrentPath.Length > segmentPath.Length
            ? CurrentPath[segmentPath.Length..].TrimStart(Path.DirectorySeparatorChar)
            : string.Empty;
    }

    private BreadcrumbSegmentViewModel CreateSegment(string name, string path, string? parentPath, string remainder)
    {
        return new BreadcrumbSegmentViewModel(name, path, parentPath, remainder, p => NavigateTo(p), NavigateToPartial, LoadDropdownChildren);
    }

    // 仕様書11章：右クリックでの部分パス置換。下層が存在しない場合は安全にそのフォルダへ移動する。
    private void NavigateToPartial(string basePath, string remainder)
    {
        if (!string.IsNullOrEmpty(remainder))
        {
            var candidate = Path.Combine(basePath, remainder);
            if (_fileSystemService.DirectoryExists(candidate))
            {
                NavigateTo(candidate);
                return;
            }
        }

        NavigateTo(basePath);
    }

    private IReadOnlyList<(string Name, string Path)> LoadDropdownChildren(string path)
    {
        try
        {
            var entries = IsPathComputerRoot(path) ? _fileSystemService.GetDrives() : _fileSystemService.GetChildren(path);
            return entries.Where(e => e.IsDirectory).Select(e => (e.Name, e.FullPath)).ToList();
        }
        catch (AppOperationException)
        {
            return Array.Empty<(string, string)>();
        }
    }

    private void BeginAddressEdit()
    {
        AddressEditText = CurrentPath;
        IsAddressEditing = true;
    }

    private void CommitAddressEdit()
    {
        IsAddressEditing = false;

        if (string.IsNullOrWhiteSpace(AddressEditText))
        {
            return;
        }

        if (!_fileSystemService.DirectoryExists(AddressEditText))
        {
            _dialogService.ShowError($"フォルダ「{AddressEditText}」が見つかりません。");
            return;
        }

        NavigateTo(AddressEditText);
    }

    private void OpenSelection()
    {
        var target = PrimarySelectedNode;
        if (target is null)
        {
            return;
        }

        if (target.IsDirectory)
        {
            NavigateTo(target.FullPath);
            return;
        }

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = target.FullPath,
                UseShellExecute = true
            });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            _dialogService.ShowError($"「{target.Name}」を開けませんでした。({ex.Message})");
        }
    }

    // 仕様書10章・37章：フォルダを新しいタブで開く。実際のタブ作成はMainWindowViewModelへ委譲する。
    private void OpenInNewTab()
    {
        var target = PrimarySelectedNode;
        if (target is null || !target.IsDirectory)
        {
            return;
        }

        OpenInNewTabRequested?.Invoke(target.FullPath);
    }

    // 仕様書34章「アプリで開く」：任意のEXEを選択し、選択ファイルを引数として起動する（今回だけ指定）。
    private void OpenWithBrowse()
    {
        var target = PrimarySelectedNode;
        if (target is null || target.IsDirectory)
        {
            return;
        }

        var exePath = _dialogService.ShowOpenFileDialog("アプリを選択", "実行ファイル (*.exe)|*.exe|すべてのファイル (*.*)|*.*");
        if (string.IsNullOrEmpty(exePath))
        {
            return;
        }

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = exePath,
                Arguments = $"\"{target.FullPath}\"",
                UseShellExecute = true
            });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            _dialogService.ShowError($"「{target.Name}」を開けませんでした。({ex.Message})");
        }
    }

    // 仕様書35章「Windows Explorerで開く」：フォルダはそのまま開き、ファイルは
    // 親フォルダをExplorerで開いて対象を選択状態にする。
    private void OpenInWindowsExplorer()
    {
        var target = PrimarySelectedNode;
        if (target is null)
        {
            return;
        }

        try
        {
            var arguments = target.IsDirectory
                ? $"\"{target.FullPath}\""
                : $"/select,\"{target.FullPath}\"";

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = arguments,
                UseShellExecute = true
            });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            _dialogService.ShowError($"Windows Explorerで開けませんでした。({ex.Message})");
        }
    }

    // 仕様書19章：選択中の項目（フォルダ）を「ここ」とする。ファイルが選択されている場合は
    // その親フォルダ、何も選択されていない場合は現在のフォルダを対象にする。
    private string GetHereDirectory()
    {
        var target = PrimarySelectedNode;
        if (target is null)
        {
            return CurrentPath;
        }

        return target.IsDirectory ? target.FullPath : (Path.GetDirectoryName(target.FullPath) ?? CurrentPath);
    }

    // 仕様書19章「ここでPowerShellを開く」：外部のPowerShellウィンドウを起動する。
    private void OpenPowerShellHere()
    {
        var directory = GetHereDirectory();

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = _settingsService.Current.Terminal.ShellExecutable,
                WorkingDirectory = directory,
                UseShellExecute = true
            });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            _dialogService.ShowError($"PowerShellを起動できませんでした。({ex.Message})");
        }
    }

    // 仕様書19章「ここでターミナルを開く」：統合ターミナル（9章）で対象フォルダへ移動する。
    // 実行は既存のRunTerminalCommandRequested経由でMainWindowViewModelへ委譲する。
    private void OpenTerminalHere()
    {
        var directory = GetHereDirectory();
        RunTerminalCommandRequested?.Invoke($"Set-Location -LiteralPath \"{directory}\"");
    }

    private void CreateNewFolder()
    {
        var name = _dialogService.PromptText("新しいフォルダ", "フォルダ名を入力してください。", "新しいフォルダ");
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        try
        {
            _fileSystemService.CreateDirectory(CurrentPath, name);
            RefreshCurrentFolder();
        }
        catch (AppOperationException ex)
        {
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
            _fileSystemService.CreateFile(CurrentPath, name);
            RefreshCurrentFolder();
        }
        catch (AppOperationException ex)
        {
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
            _fileSystemService.Rename(target.FullPath, newName);
            RefreshCurrentFolder();
        }
        catch (AppOperationException ex)
        {
            _dialogService.ShowError(ex.Message);
        }
    }

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

        try
        {
            _fileSystemService.Delete(SelectedNodes.Select(n => n.FullPath));
            RefreshCurrentFolder();
        }
        catch (AppOperationException ex)
        {
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

        try
        {
            if (isMove)
            {
                _fileSystemService.Move(files, CurrentPath);
            }
            else
            {
                _fileSystemService.Copy(files, CurrentPath);
            }

            RefreshCurrentFolder();
        }
        catch (AppOperationException ex)
        {
            _dialogService.ShowError(ex.Message);
        }
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
            _fileSystemService.Duplicate(targets);
            RefreshCurrentFolder();
        }
        catch (AppOperationException ex)
        {
            _dialogService.ShowError(ex.Message);
        }
    }

    // 仕様書20章「移動」に対応するドラッグ&ドロップ本体。ドロップ先フォルダの内部/子孫への
    // 移動・コピーや、同じフォルダへの無意味なドロップは黙って無視する（既存フォルダへの
    // File.Move/Directory.Move例外を避けるための最小限の防御）。
    public void DropFiles(IReadOnlyList<string> sourcePaths, string destinationFolder, bool isMove)
    {
        var targets = sourcePaths
            .Where(source => !IsNoOpOrInvalidDrop(source, destinationFolder))
            .ToList();

        if (targets.Count == 0)
        {
            return;
        }

        try
        {
            if (isMove)
            {
                _fileSystemService.Move(targets, destinationFolder);
            }
            else
            {
                _fileSystemService.Copy(targets, destinationFolder);
            }

            RefreshCurrentFolder();
        }
        catch (AppOperationException ex)
        {
            _dialogService.ShowError(ex.Message);
        }
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

        try
        {
            foreach (var source in targets)
            {
                var normalizedSource = Path.TrimEndingDirectorySeparator(source);
                var normalizedDestination = Path.TrimEndingDirectorySeparator(destinationFolder);
                var sourceParent = Path.GetDirectoryName(normalizedSource);

                if (string.Equals(sourceParent, normalizedDestination, StringComparison.OrdinalIgnoreCase))
                {
                    _fileSystemService.Duplicate(new[] { source });
                    continue;
                }

                var destinationPath = Path.Combine(destinationFolder, Path.GetFileName(source));
                if (!Directory.Exists(destinationPath) && !File.Exists(destinationPath))
                {
                    _fileSystemService.Copy(new[] { source }, destinationFolder);
                    continue;
                }

                var fileName = Path.GetFileName(source);
                var choice = _dialogService.SelectFromList(
                    "ファイルの競合",
                    $"「{fileName}」は移動先に既に存在します。どうしますか？",
                    new[] { "名前を変更してコピー（*_copy）", "上書きする" });

                if (choice == "名前を変更してコピー（*_copy）")
                {
                    _fileSystemService.CopyRenamed(source, destinationFolder);
                }
                else if (choice == "上書きする")
                {
                    _fileSystemService.CopyReplacing(source, destinationFolder);
                }

                // それ以外（キャンセル・ダイアログを閉じた）は何もしない。
            }

            RefreshCurrentFolder();
        }
        catch (AppOperationException ex)
        {
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
            _fileSystemService.CreateShortcuts(targets, destinationFolder);
            RefreshCurrentFolder();
        }
        catch (AppOperationException ex)
        {
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

    public void BulkRename(IReadOnlyList<FileSystemNodeViewModel> targets, string pattern)
    {
        for (var i = 0; i < targets.Count; i++)
        {
            var newName = RenamePatternExpander.Expand(pattern, targets[i].Name, i);

            try
            {
                _fileSystemService.Rename(targets[i].FullPath, newName);
            }
            catch (AppOperationException ex)
            {
                _dialogService.ShowError(ex.Message);
                break;
            }
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
            BulkRename(targets, viewModel.Pattern);
        }
    }

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

    // 仕様書14.2章：Patchファイルを選択して現在のGit/SVN管理フォルダへ適用する。
    private void ApplyPatch()
    {
        var patchPath = _dialogService.ShowOpenFileDialog(
            "Patchの適用",
            "Patchファイル (*.patch;*.diff)|*.patch;*.diff|すべてのファイル (*.*)|*.*");

        if (patchPath is null)
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

        string leftText;
        string rightText;

        try
        {
            leftText = _fileSystemService.ReadTextPreview(_heldComparisonPath, 5_000_000, out _);
            rightText = _fileSystemService.ReadTextPreview(target.FullPath, 5_000_000, out _);
        }
        catch (AppOperationException ex)
        {
            _dialogService.ShowError(ex.Message);
            return;
        }

        var diffViewModel = DiffViewModel.Create(
            $"{Path.GetFileName(_heldComparisonPath)} ⇔ {target.Name}",
            _heldComparisonPath,
            target.FullPath,
            leftText,
            rightText,
            _diffService,
            _dialogService);

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
