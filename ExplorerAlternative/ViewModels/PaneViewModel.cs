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
public sealed partial class PaneViewModel : ObservableObject, IDisposable
{
    /// <summary>ファイルのコピー/切り取りで使うクリップボード（テストでは、利用者のクリップボードを触らないよう差し替える）。</summary>
    internal IFileClipboard FileClipboard { get; set; } = new WindowsFileClipboard();

    private readonly IFileSystemService _fileSystemService;
    private readonly IDialogService _dialogService;
    private readonly IVersionControlService _versionControlService;
    private readonly IExternalToolService _externalToolService;
    private readonly ISettingsService _settingsService;
    private readonly IPatchService _patchService;
    private readonly IVersionControlOperationsService _versionControlOperationsService;
    private readonly IDiffService _diffService;
    private readonly IProjectDetectionService _projectDetectionService;
    private readonly IUndoService _undoService;
    private readonly IFileOperationHistoryService _fileOperationHistoryService;
    private readonly IFileOperationQueueService _fileOperationQueueService;
    private readonly HashSet<Guid> _ownedQueueItemIds = new();
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
    private VersionControlKind _preferredVcs = VersionControlKind.None;
    private int _loadGeneration;
    private string _sortColumn = "Name";
    private bool _sortAscending = true;
    private bool _suppressNodeToggleRebuild;

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
        IUndoService undoService,
        IFileOperationHistoryService fileOperationHistoryService,
        IFileOperationQueueService fileOperationQueueService,
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
        _undoService = undoService;
        _fileOperationHistoryService = fileOperationHistoryService;
        _fileOperationQueueService = fileOperationQueueService;
        _fileOperationQueueService.ItemCompleted += OnQueueItemCompleted;
        _folderWatcherService = folderWatcherServiceFactory();
        _folderWatcherService.Changed += OnFolderChangedExternally;
        _currentPath = initialPath;
        _currentViewMode = initialViewMode;

        NavigateToCommand = new RelayCommand(p => NavigateTo((string)p!));
        GoUpCommand = new RelayCommand(_ => GoUp());
        SetViewModeCommand = new RelayCommand(p => CurrentViewMode = (ViewMode)p!);
        SortByColumnCommand = new RelayCommand(p => SortByColumn((string)p!));
        ToggleShowHiddenFilesCommand = new RelayCommand(_ => ShowHiddenFiles = !ShowHiddenFiles);
        RefreshCommand = new RelayCommand(_ => RefreshCurrentFolder());
        ToggleExpandCommand = new RelayCommand(p => ((FileSystemNodeViewModel)p!).IsExpanded ^= true);
        OpenCommand = new RelayCommand(_ => OpenSelection(), _ => PrimarySelectedNode is not null);
        OpenInNewTabCommand = new RelayCommand(_ => OpenInNewTab(), _ => PrimarySelectedNode is { IsDirectory: true });
        OpenWithDefaultAppCommand = new RelayCommand(_ => OpenWithSystemDefaultSelection(), _ => PrimarySelectedNode is { IsDirectory: false });
        OpenWithBrowseCommand = new RelayCommand(_ => OpenWithBrowse(), _ => PrimarySelectedNode is { IsDirectory: false });
        OpenWithAlwaysCommand = new RelayCommand(_ => OpenWithAlways(), _ => PrimarySelectedNode is { IsDirectory: false });
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
        OpenSshTerminalCommand = new RelayCommand(_ => OpenSshTerminal());
        SwitchVcsCommand = new RelayCommand(_ => SwitchVcs(), _ => HasOtherVcs);
        QuickCopyCommand = new RelayCommand(_ => QuickTransfer(isMove: false), _ => SelectedNodes.Count > 0);
        QuickMoveCommand = new RelayCommand(_ => QuickTransfer(isMove: true), _ => SelectedNodes.Count > 0);
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
        ShowLogCommand = new RelayCommand(_ => ShowLog(null), _ => VcsInfo.Kind != VersionControlKind.None);
        ShowCommitCommand = new RelayCommand(p => ShowLog(p as CommitLogEntry), _ => VcsInfo.Kind != VersionControlKind.None);
        GoToProjectRootCommand = new RelayCommand(_ => NavigateTo(CurrentProject!.RootPath), _ => CurrentProject is not null && !PathsEqual(CurrentProject.RootPath, CurrentPath));
        GoToSolutionRootCommand = new RelayCommand(_ => NavigateTo(_solutionRootPath!), _ => _solutionRootPath is not null && !PathsEqual(_solutionRootPath, CurrentPath));
        GoToGitRootCommand = new RelayCommand(_ => NavigateTo(VcsInfo.RootPath!), _ => VcsInfo.Kind != VersionControlKind.None && VcsInfo.RootPath is not null && !PathsEqual(VcsInfo.RootPath, CurrentPath));
        HoldForComparisonCommand = new RelayCommand(_ => HoldForComparison(), _ => PrimarySelectedNode is { IsDirectory: false });
        CompareWithHeldCommand = new RelayCommand(
            _ => CompareWithHeld(),
            _ => PrimarySelectedNode is { IsDirectory: false } node &&
                 _heldComparisonPath is not null &&
                 !string.Equals(_heldComparisonPath, node.FullPath, StringComparison.OrdinalIgnoreCase));
        CompareSelectedCommand = new RelayCommand(
            _ => CompareSelected(),
            _ => SelectedNodes.Count == 2 && SelectedNodes.All(n => !n.IsDirectory));

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

    /// <summary>詳細表示の列ヘッダーをクリックしたときの並び替え（列名を文字列で受け取る）。</summary>
    public RelayCommand SortByColumnCommand { get; }

    public RelayCommand ToggleExpandCommand { get; }

    public RelayCommand OpenCommand { get; }

    public RelayCommand OpenInNewTabCommand { get; }

    /// <summary>仕様書34章「アプリで開く」：既定のアプリで開く（OpenCommandのファイル限定版）。</summary>
    public RelayCommand OpenWithDefaultAppCommand { get; }

    /// <summary>仕様書34章「アプリで開く」：任意のEXEを選択して開く（今回だけ指定）。</summary>
    public RelayCommand OpenWithBrowseCommand { get; }

    /// <summary>仕様書34章「常にこのアプリで開く」：アプリを選び、同じ拡張子のファイルを、今後もそのアプリで開くよう関連付ける。</summary>
    public RelayCommand OpenWithAlwaysCommand { get; }

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

    /// <summary>仕様書19章「SSHターミナルを開く」：登録済みの接続先を選んで、統合ターミナルでSSH接続する。</summary>
    public RelayCommand OpenSshTerminalCommand { get; }

    /// <summary>「SSHターミナルを開く」で接続先が選ばれたときに発火する。接続の実行は呼び出し側（MainWindowViewModel）が行う。</summary>
    public event Action<SshConnectionProfile>? SshTerminalRequested;

    /// <summary>仕様書60章「クイックコピー」：最近のコピー先から選んで、選択項目をコピーする。</summary>
    public RelayCommand QuickCopyCommand { get; }

    /// <summary>仕様書60章「クイック移動」：最近の移動先から選んで、選択項目を移動する。</summary>
    public RelayCommand QuickMoveCommand { get; }

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

    /// <summary>仕様書21章「Log」：コミット履歴と変更内容（Show Commit）をまとめて閲覧するウィンドウを開く。</summary>
    public RelayCommand ShowLogCommand { get; }

    /// <summary>仕様書21章「Show Commit」：右ペインの簡易履歴から特定のコミットを選んで開く。</summary>
    public RelayCommand ShowCommitCommand { get; }

    /// <summary>仕様書25章「比較対象として保持」。</summary>
    public RelayCommand HoldForComparisonCommand { get; }

    /// <summary>仕様書25章「比較対象と比較」。</summary>
    public RelayCommand CompareWithHeldCommand { get; }

    /// <summary>ファイルを2つ選択している場合に、保持の手順を省いて直接比較する簡易コマンド。</summary>
    public RelayCommand CompareSelectedCommand { get; }

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

    /// <summary>詳細表示の列ヘッダーに表示する、現在の並び替え方向を示す矢印（対象列以外は空）。</summary>
    public string SortIndicatorName => GetSortIndicator("Name");

    public string SortIndicatorSize => GetSortIndicator("Size");

    public string SortIndicatorLastModified => GetSortIndicator("LastModified");

    public string SortIndicatorKind => GetSortIndicator("Kind");

    private string GetSortIndicator(string column) => _sortColumn == column ? (_sortAscending ? " ▲" : " ▼") : string.Empty;

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
        private set
        {
            if (SetProperty(ref _vcsInfo, value))
            {
                OnPropertyChanged(nameof(HasOtherVcs));
                OnPropertyChanged(nameof(OtherVcsSwitchLabel));
                SwitchVcsCommand?.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>GitとSVNの両方がある場所か（仕様書20章）。両方ある場合は、右ペインで表示する方を切り替えられる。</summary>
    public bool HasOtherVcs => VcsInfo.HasOther;

    /// <summary>切り替えボタンの表示（例：「SVNに切り替え」）。</summary>
    public string OtherVcsSwitchLabel => VcsInfo.OtherKind switch
    {
        VersionControlKind.Svn => "SVNに切り替え",
        VersionControlKind.Git => "Gitに切り替え",
        _ => string.Empty
    };

    /// <summary>GitとSVNの両方がある場所で、表示・操作の対象を、もう一方に切り替える。</summary>
    public RelayCommand SwitchVcsCommand { get; }

    /// <summary>仕様書21章「Log」：直近のコミット履歴（新しい順）。</summary>
    public ObservableCollection<CommitLogEntry> CommitLog { get; } = new();

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

    /// <summary>
    /// 実際にフォルダ内容を読み込んで表示を更新する（履歴には影響しない）。
    /// <paramref name="raisePathChanged"/>は、パスの変更の通知（ターミナルの同期・最近使った場所の記録）を行うか。
    /// 同じフォルダの再読み込み（F5・外部のファイル変更による自動更新・Git/SVNの切替）では、パスは変わっていないので、
    /// 通知しない（通知すると、ファイルが変わるたびに、ターミナルへ「Set-Location」が送られ続けてしまう）。
    /// </summary>
    private void LoadPath(string path, bool raisePathChanged = true)
    {
        try
        {
            var entries = IsPathComputerRoot(path)
                ? _fileSystemService.GetDrives()
                : _fileSystemService.GetChildren(path);

            // 仕様書64章「既知の制限」：同じフォルダの再読み込み（F5・外部変更検知）の場合のみ、
            // 選択状態・展開状態をパスで照合して復元する（別フォルダへの移動時は復元不要）。
            Dictionary<string, NodeState>? previousState = null;
            if (string.Equals(_currentPath, path, StringComparison.OrdinalIgnoreCase) && RootNodes.Count > 0)
            {
                previousState = new Dictionary<string, NodeState>(StringComparer.OrdinalIgnoreCase);
                CollectNodeState(RootNodes, previousState);
            }

            CurrentPath = path;

            var generation = ++_loadGeneration;
            var isComputerRoot = IsPathComputerRoot(path);

            // VCS種別の判定自体はファイルの存在確認のみで軽量なため同期で行う。ただし
            // git/svnプロセスを起動するステータス取得・コミットログ取得、および複数階層を
            // walkするプロジェクト検出は重く、フォルダ移動のたびにUIスレッドを固まらせて
            // いたため、以下でLoadVcsAndProjectInfoAsyncへ逃がして非同期に取得する。
            VcsInfo = isComputerRoot ? VersionControlInfo.None : _versionControlService.Detect(path, _preferredVcs);
            _folderWatcherService.SetPath(isComputerRoot ? null : path);
            _vcsStatusByPath = new Dictionary<string, string>();

            if (VcsInfo.Kind == VersionControlKind.None && _lastCommitLogRoot is not null)
            {
                CommitLog.Clear();
                _lastCommitLogRoot = null;
            }

            CurrentProject = null;
            _solutionRootPath = null;
            GoToProjectRootCommand.RaiseCanExecuteChanged();
            GoToSolutionRootCommand.RaiseCanExecuteChanged();
            GoToGitRootCommand.RaiseCanExecuteChanged();

            RootNodes.Clear();

            var showHidden = _settingsService.Current.View.ShowHiddenFiles;
            var newNodes = entries
                .Where(e => showHidden || !e.IsHidden)
                .Select(e => new FileSystemNodeViewModel(e, 0, _fileSystemService, _dialogService, _settingsService, GetVcsStatus, OnNodeToggled));

            foreach (var node in SortNodes(newNodes))
            {
                RootNodes.Add(node);
            }

            if (previousState is not null)
            {
                // 展開状態の復元中は、展開済みフォルダ1件ごとにOnNodeToggledが表示リスト全体を
                // 再構築してしまうと、展開済みフォルダ数が多い場合に再構築が連鎖してフリーズ
                // したように見える不具合があったため、復元完了後に1回だけ再構築する。
                _suppressNodeToggleRebuild = true;
                try
                {
                    ApplyNodeState(RootNodes, previousState);
                }
                finally
                {
                    _suppressNodeToggleRebuild = false;
                }
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

            if (raisePathChanged)
            {
                PathChanged?.Invoke(path);
            }

            if (!isComputerRoot)
            {
                LoadVcsAndProjectInfoAsync(path, VcsInfo, generation);
            }
        }
        catch (AppOperationException ex)
        {
            _dialogService.ShowError(ex.Message);
        }
    }

    /// <summary>
    /// git/svnプロセスを起動するステータス取得・コミットログ取得と、複数階層をwalkする
    /// プロジェクト検出をバックグラウンドスレッドで行う。完了時点で既に別のフォルダへ
    /// 移動済み（generationが不一致）の場合は結果を破棄する。
    /// </summary>
    private void LoadVcsAndProjectInfoAsync(string path, VersionControlInfo vcsInfo, int generation)
    {
        var needsCommitLog = vcsInfo.Kind != VersionControlKind.None && CommitLogKey(vcsInfo) != _lastCommitLogRoot;

        Task.Run(() =>
        {
            var statuses = vcsInfo.Kind != VersionControlKind.None
                ? _versionControlService.GetFileStatuses(vcsInfo)
                : new Dictionary<string, string>();
            var commitLogEntries = needsCommitLog
                ? _versionControlService.GetCommitLog(vcsInfo, maxCount: 20)
                : null;
            var project = _projectDetectionService.Detect(path);
            var solutionRoot = _projectDetectionService.FindSolutionRoot(path);

            System.Windows.Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_isDisposed || generation != _loadGeneration)
                {
                    return;
                }

                _vcsStatusByPath = statuses;
                RefreshVisibleVcsStatusDisplay();

                if (commitLogEntries is not null)
                {
                    CommitLog.Clear();
                    _lastCommitLogRoot = CommitLogKey(vcsInfo);
                    foreach (var entry in commitLogEntries)
                    {
                        CommitLog.Add(entry);
                    }
                }

                var previousProjectRoot = CurrentProject?.RootPath;
                CurrentProject = project;
                _solutionRootPath = solutionRoot;
                GoToProjectRootCommand.RaiseCanExecuteChanged();
                GoToSolutionRootCommand.RaiseCanExecuteChanged();

                if (CurrentProject is not null && CurrentProject.RootPath != previousProjectRoot)
                {
                    ProjectDetected?.Invoke(CurrentProject);
                }
            }));
        });
    }

    // コミット履歴を取り直すかの判定用。ルートのフォルダが同じでも、GitとSVNでは履歴が別なので、種別も含める。
    private static string CommitLogKey(VersionControlInfo info) => $"{info.Kind}|{info.RootPath}";

    // 仕様書20章：GitとSVNの両方がある場所で、表示する方を切り替える（選択は、このペインの中で保持する）。
    private void SwitchVcs()
    {
        if (!VcsInfo.HasOther)
        {
            return;
        }

        _preferredVcs = VcsInfo.OtherKind;
        LoadPath(CurrentPath, raisePathChanged: false);
    }

    private void RefreshVisibleVcsStatusDisplay()
    {
        foreach (var node in VisibleNodes)
        {
            node.RefreshVcsStatus();
        }
    }

    /// <summary>現在フォルダを再読込する（履歴には積まない）。</summary>
    public void RefreshCurrentFolder() => LoadPath(CurrentPath, raisePathChanged: false);

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
        _fileOperationQueueService.ItemCompleted -= OnQueueItemCompleted;
    }

    private static bool IsPathComputerRoot(string path) => string.IsNullOrEmpty(path);

    // Windowsのパスは大文字小文字を区別しないため、単純な==比較ではなくこちらを使う
    // （56章のルート移動系コマンドのCanExecuteで、大小文字違いにより誤って有効化されるのを防ぐ）。
    private static bool PathsEqual(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private void GoUp()
    {
        if (IsAtComputerRoot)
        {
            return;
        }

        var parent = _fileSystemService.GetParent(CurrentPath);
        NavigateTo(parent ?? string.Empty);
    }
}
