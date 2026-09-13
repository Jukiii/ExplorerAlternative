using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ExplorerAlternative.Models;
using ExplorerAlternative.Mvvm;
using ExplorerAlternative.Services;
using ExplorerAlternative.Services.Abstractions;

namespace ExplorerAlternative.ViewModels;

/// <summary>
/// メインウィンドウ全体のビューモデル。タブ・ナビゲーションペイン・ターミナルを統括する。
/// </summary>
public sealed class MainWindowViewModel : ObservableObject
{
    private readonly IFileSystemService _fileSystemService;
    private readonly IDialogService _dialogService;
    private readonly IVersionControlService _versionControlService;
    private readonly IExternalToolService _externalToolService;
    private readonly ISettingsService _settingsService;
    private readonly IWorkspaceService _workspaceService;
    private readonly IThemeService _themeService;
    private readonly IPatchService _patchService;
    private readonly ISshService _sshService;
    private readonly IVersionControlOperationsService _versionControlOperationsService;
    private readonly IDiffService _diffService;
    private readonly ISshCredentialStore _sshCredentialStore;
    private readonly IFolderScanService _folderScanService;
    private readonly IExplorerIntegrationService _explorerIntegrationService;
    private readonly ITrayIconService _trayIconService;
    private readonly IGlobalHotkeyService _globalHotkeyService;
    private readonly IJumpListService _jumpListService;
    private readonly IProjectDetectionService _projectDetectionService;
    private readonly Func<IFolderWatcherService> _folderWatcherServiceFactory;

    private TabViewModel? _activeTab;
    private PreviewViewModel? _currentPreview;
    private List<FileSystemNodeViewModel> _previewNodes = new();
    private int _previewIndex = -1;
    private int _tabCounter;
    private bool _isExiting;

    public MainWindowViewModel(
        IFileSystemService fileSystemService,
        IDialogService dialogService,
        IVersionControlService versionControlService,
        IExternalToolService externalToolService,
        ISettingsService settingsService,
        Func<IPowerShellTerminalService> terminalServiceFactory,
        IWorkspaceService workspaceService,
        IThemeService themeService,
        IPatchService patchService,
        ISshService sshService,
        IVersionControlOperationsService versionControlOperationsService,
        IDiffService diffService,
        ISshCredentialStore sshCredentialStore,
        IFolderScanService folderScanService,
        IExplorerIntegrationService explorerIntegrationService,
        ITrayIconService trayIconService,
        IGlobalHotkeyService globalHotkeyService,
        IJumpListService jumpListService,
        IProjectDetectionService projectDetectionService,
        Func<IFolderWatcherService> folderWatcherServiceFactory,
        string? startupPath = null)
    {
        _fileSystemService = fileSystemService;
        _dialogService = dialogService;
        _versionControlService = versionControlService;
        _externalToolService = externalToolService;
        _settingsService = settingsService;
        _workspaceService = workspaceService;
        _themeService = themeService;
        _patchService = patchService;
        _sshService = sshService;
        _versionControlOperationsService = versionControlOperationsService;
        _diffService = diffService;
        _sshCredentialStore = sshCredentialStore;
        _folderScanService = folderScanService;
        _explorerIntegrationService = explorerIntegrationService;
        _trayIconService = trayIconService;
        _globalHotkeyService = globalHotkeyService;
        _jumpListService = jumpListService;
        _projectDetectionService = projectDetectionService;
        _folderWatcherServiceFactory = folderWatcherServiceFactory;

        NavigationPane = new NavigationPaneViewModel(settingsService, fileSystemService, dialogService, NavigateActiveTo);
        NavigationPane.WorkspaceOpenRequested += name => LoadWorkspaceByName((Window)Application.Current!.MainWindow!, name);
        TerminalHost = new TerminalHostViewModel(terminalServiceFactory, settingsService.Current.Terminal.SyncByDefault);

        AddTabCommand = new RelayCommand(_ => AddTab(GetDefaultInitialPath()));
        CloseTabCommand = new RelayCommand(p => CloseTab((TabViewModel)p!), _ => Tabs.Count > 1);
        NextTabCommand = new RelayCommand(_ => ActivateNextTab(), _ => Tabs.Count > 1);
        GoUpCommand = new RelayCommand(_ => ActiveTab?.ActivePane.GoUpCommand.Execute(null));
        GoBackCommand = new RelayCommand(_ => ActiveTab?.ActivePane.GoBackCommand.Execute(null), _ => ActiveTab?.ActivePane.CanGoBack == true);
        GoForwardCommand = new RelayCommand(_ => ActiveTab?.ActivePane.GoForwardCommand.Execute(null), _ => ActiveTab?.ActivePane.CanGoForward == true);
        TogglePreviewCommand = new RelayCommand(_ => TogglePreview());
        ToggleTerminalCommand = new RelayCommand(_ => ToggleTerminal());
        OpenCheatSheetCommand = new RelayCommand(_ => _dialogService.ShowCheatSheet());
        OpenSettingsCommand = new RelayCommand(_ => OpenSettings());
        AddFavoriteCommand = new RelayCommand(_ => AddCurrentFolderToFavorites());
        AddTagCommand = new RelayCommand(_ => AddNewTag());
        SaveWorkspaceCommand = new RelayCommand(p => SaveWorkspace((Window)p!));
        LoadWorkspaceCommand = new RelayCommand(p => LoadWorkspace((Window)p!));
        DuplicateTabCommand = new RelayCommand(p => DuplicateTab((TabViewModel)p!));
        SplitHorizontalCommand = new RelayCommand(_ => SplitPane(Orientation.Horizontal), _ => ActiveTab?.CanSplit == true);
        SplitVerticalCommand = new RelayCommand(_ => SplitPane(Orientation.Vertical), _ => ActiveTab?.CanSplit == true);
        ClosePaneCommand = new RelayCommand(_ => ClosePane(), _ => ActiveTab?.CanClosePane == true);
        SetActivePaneCommand = new RelayCommand(p => SetActivePane((PaneViewModel)p!));
        OpenSshConnectionCommand = new RelayCommand(_ => OpenSshConnection());
        ToggleVcsPaneCommand = new RelayCommand(_ => IsVcsPaneVisible = !IsVcsPaneVisible);
        OpenSearchCommand = new RelayCommand(_ => OpenSearch());
        OpenDiskAnalysisCommand = new RelayCommand(_ => OpenDiskAnalysis());
        OpenCommandPaletteCommand = new RelayCommand(_ => OpenCommandPalette());

        AddTab(ResolveStartupPath(startupPath));
        RebuildJumpList();
    }

    public ObservableCollection<TabViewModel> Tabs { get; } = new();

    public NavigationPaneViewModel NavigationPane { get; }

    public TerminalHostViewModel TerminalHost { get; }

    public RelayCommand AddTabCommand { get; }

    public RelayCommand CloseTabCommand { get; }

    public RelayCommand NextTabCommand { get; }

    public RelayCommand GoUpCommand { get; }

    public RelayCommand GoBackCommand { get; }

    public RelayCommand GoForwardCommand { get; }

    public RelayCommand TogglePreviewCommand { get; }

    public RelayCommand ToggleTerminalCommand { get; }

    public RelayCommand OpenCheatSheetCommand { get; }

    public RelayCommand OpenSettingsCommand { get; }

    public RelayCommand AddFavoriteCommand { get; }

    public RelayCommand AddTagCommand { get; }

    public RelayCommand SaveWorkspaceCommand { get; }

    public RelayCommand LoadWorkspaceCommand { get; }

    public RelayCommand DuplicateTabCommand { get; }

    public RelayCommand SplitHorizontalCommand { get; }

    public RelayCommand SplitVerticalCommand { get; }

    public RelayCommand ClosePaneCommand { get; }

    public RelayCommand SetActivePaneCommand { get; }

    public RelayCommand OpenSshConnectionCommand { get; }

    public RelayCommand ToggleVcsPaneCommand { get; }

    /// <summary>仕様書12章「検索」（Ctrl+F）。</summary>
    public RelayCommand OpenSearchCommand { get; }

    /// <summary>仕様書38章・58章・59章：巨大ファイル/重複ファイル/空フォルダ検索。</summary>
    public RelayCommand OpenDiskAnalysisCommand { get; }

    /// <summary>仕様書46章「コマンドパレット」（Ctrl+Shift+P）。</summary>
    public RelayCommand OpenCommandPaletteCommand { get; }

    private bool _isVcsPaneVisible = true;

    /// <summary>Git/SVN情報ペイン（右側、表示/非表示切替可能）の表示状態。</summary>
    public bool IsVcsPaneVisible
    {
        get => _isVcsPaneVisible;
        set => SetProperty(ref _isVcsPaneVisible, value);
    }

    public TabViewModel? ActiveTab
    {
        get => _activeTab;
        set
        {
            if (_activeTab == value)
            {
                return;
            }

            if (_activeTab is not null)
            {
                _activeTab.ActivePanePathChanged -= OnActivePanePathChanged;
                _activeTab.ActivePaneSelectionChanged -= OnActivePaneSelectionChanged;
            }

            _activeTab = value;

            if (_activeTab is not null)
            {
                _activeTab.ActivePanePathChanged += OnActivePanePathChanged;
                _activeTab.ActivePaneSelectionChanged += OnActivePaneSelectionChanged;
                TerminalHost.SyncCurrentDirectory(_activeTab.ActivePane.CurrentPath);
            }

            OnPropertyChanged();
        }
    }

    private static string GetDefaultInitialPath()
    {
        return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }

    // 仕様書35章「Explorerから本アプリへフォルダを渡して開く」：起動引数でフォルダ（または
    // ファイル。その場合は親フォルダ）を渡された場合、その場所を最初のタブとして開く。
    private static string ResolveStartupPath(string? startupPath)
    {
        if (!string.IsNullOrWhiteSpace(startupPath))
        {
            if (Directory.Exists(startupPath))
            {
                return startupPath;
            }

            if (File.Exists(startupPath))
            {
                var parent = Path.GetDirectoryName(startupPath);
                if (!string.IsNullOrEmpty(parent))
                {
                    return parent;
                }
            }
        }

        return GetDefaultInitialPath();
    }

    private void AddTab(string initialPath)
    {
        var pane = CreatePane(initialPath, _settingsService.Current.View.DefaultViewMode);

        _tabCounter++;
        var tab = new TabViewModel(pane, BuildTabHeader(initialPath));
        Tabs.Add(tab);
        ActiveTab = tab;
    }

    private static string BuildTabHeader(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return "PC";
        }

        var name = Path.GetFileName(path.TrimEnd('\\'));
        return string.IsNullOrEmpty(name) ? path : name;
    }

    private void CloseTab(TabViewModel tab)
    {
        if (Tabs.Count <= 1)
        {
            return;
        }

        if (tab.IsPinned)
        {
            _dialogService.ShowInfo("固定されたタブです。閉じるには先に固定を解除してください。");
            return;
        }

        var index = Tabs.IndexOf(tab);
        Tabs.Remove(tab);

        foreach (var pane in tab.Panes)
        {
            pane.Dispose();
        }

        if (ReferenceEquals(ActiveTab, tab))
        {
            ActiveTab = Tabs[Math.Min(index, Tabs.Count - 1)];
        }
    }

    // 仕様書10章：Ctrl+Tabでのタブ切替。
    private void ActivateNextTab()
    {
        if (Tabs.Count <= 1 || ActiveTab is null)
        {
            return;
        }

        var index = Tabs.IndexOf(ActiveTab);
        ActiveTab = Tabs[(index + 1) % Tabs.Count];
    }

    private void NavigateActiveTo(string path)
    {
        ActiveTab?.ActivePane.NavigateTo(path);
    }

    private void OnActivePanePathChanged(string path)
    {
        // 仕様書57章「プロジェクト単位ターミナル」：プロジェクトが検出されている場合は
        // プロジェクトルートをカレントディレクトリとする（Terminal Syncと連動）。
        var syncTarget = ActiveTab?.ActivePane.CurrentProject?.RootPath ?? path;
        TerminalHost.SyncCurrentDirectory(syncTarget);
        NavigationPane.RecordRecentPlace(path);
        RebuildJumpList();
    }

    // 仕様書39章：最近使った場所・お気に入り・ワークスペースが変化するたびに反映し直す。
    private void RebuildJumpList()
    {
        _jumpListService.Rebuild(
            NavigationPane.RecentPlaces.Select(f => (f.Name, f.Path)),
            NavigationPane.Favorites.Select(f => (f.Name, f.Path)),
            NavigationPane.Workspaces);
    }


    private PaneViewModel CreatePane(string initialPath, ViewMode initialViewMode)
    {
        var pane = new PaneViewModel(
            _fileSystemService,
            _dialogService,
            _versionControlService,
            _externalToolService,
            _settingsService,
            _patchService,
            _versionControlOperationsService,
            _diffService,
            _projectDetectionService,
            _folderWatcherServiceFactory,
            initialPath,
            initialViewMode);

        pane.RunTerminalCommandRequested += RunTerminalCommand;
        pane.OpenInNewTabRequested += OpenPathInNewTab;
        pane.ProjectDetected += project => NavigationPane.RecordRecentProject(project.Name, project.RootPath);

        // PaneViewModelのコンストラクタ内で初回のLoadPathが実行済みのため、上のイベント購読より前に
        // 初回分のProjectDetectedが発火してしまっている。取りこぼした初回分をここで補う。
        if (pane.CurrentProject is { } initialProject)
        {
            NavigationPane.RecordRecentProject(initialProject.Name, initialProject.RootPath);
        }

        return pane;
    }

    // 仕様書13章・20章：Git/SVN操作コマンドを統合ターミナル（9章）上で実行する。
    // 資格情報の入力待ちなどの対話にも、通常のターミナル操作と同じ画面で対応できる。
    private void RunTerminalCommand(string command)
    {
        TerminalHost.SendRawCommand(command);
    }

    // 仕様書10章「ここでフォルダを新しいタブで開く」・37章「スマートタブ」。
    // ReuseExisting/Autoでは、指定パスを表示中のタブが既にあればそれをアクティブにする
    // （どちらも同じ「重複検出して再利用する」挙動とし、Autoは将来の判定拡張の余地として区別している）。
    private void OpenPathInNewTab(string path)
    {
        if (_settingsService.Current.Tabs.DuplicateBehavior != DuplicateTabBehavior.AlwaysNew)
        {
            var existing = Tabs.FirstOrDefault(t =>
                t.Panes.Any(p => string.Equals(p.CurrentPath, path, StringComparison.OrdinalIgnoreCase)));

            if (existing is not null)
            {
                ActiveTab = existing;
                return;
            }
        }

        AddTab(path);
    }

    private void DuplicateTab(TabViewModel source)
    {
        var basePane = source.ActivePane;
        var newPane = CreatePane(basePane.CurrentPath, basePane.CurrentViewMode);
        var newTab = new TabViewModel(newPane, source.Header);

        var index = Tabs.IndexOf(source);
        Tabs.Insert(index + 1, newTab);
        ActiveTab = newTab;
    }

    private void SplitPane(Orientation orientation)
    {
        var tab = ActiveTab;
        if (tab is null || !tab.CanSplit)
        {
            return;
        }

        var basePane = tab.ActivePane;
        var newPane = CreatePane(basePane.CurrentPath, basePane.CurrentViewMode);
        tab.SplitOrientation = orientation;
        tab.AddPane(newPane);
        TerminalHost.SyncCurrentDirectory(newPane.CurrentPath);
    }

    private void ClosePane()
    {
        var tab = ActiveTab;
        if (tab is null || !tab.CanClosePane)
        {
            return;
        }

        var paneToClose = tab.ActivePane;
        tab.RemovePane(paneToClose);
        paneToClose.Dispose();
        TerminalHost.SyncCurrentDirectory(tab.ActivePane.CurrentPath);
    }

    private void SetActivePane(PaneViewModel pane)
    {
        ActiveTab?.SetActivePane(pane);
        TerminalHost.SyncCurrentDirectory(pane.CurrentPath);
    }

    private void ToggleTerminal()
    {
        TerminalHost.ToggleVisibilityCommand.Execute(null);

        if (TerminalHost.IsVisible && ActiveTab is not null && !ActiveTab.ActivePane.IsAtComputerRoot)
        {
            TerminalHost.SyncCurrentDirectory(ActiveTab.ActivePane.CurrentPath);
        }
    }

    // 仕様書11章・13章：Quick Look。開いている間は選択変更に追従するが、
    // 固定（15章）中は追従を止める。← / →（13章）はプレビューを開いた時点の
    // 一覧（VisibleNodes）内で前後移動する。
    private void TogglePreview()
    {
        if (_currentPreview is not null)
        {
            ClosePreviewInternal();
            return;
        }

        var pane = ActiveTab?.ActivePane;
        var node = pane?.PrimarySelectedNode;
        if (pane is null || node is null)
        {
            return;
        }

        _previewNodes = pane.VisibleNodes.ToList();
        _previewIndex = _previewNodes.IndexOf(node);
        ShowPreviewAtCurrentIndex();
    }

    private void ShowPreviewAtCurrentIndex()
    {
        if (_previewIndex < 0 || _previewIndex >= _previewNodes.Count)
        {
            return;
        }

        var wasPinned = _currentPreview?.IsPinned ?? false;

        var preview = PreviewViewModel.Create(_previewNodes[_previewIndex], _fileSystemService, _versionControlService, _settingsService);
        preview.IsPinned = wasPinned;
        preview.RequestPrevious = () => MovePreview(-1);
        preview.RequestNext = () => MovePreview(1);
        preview.RequestClose = ClosePreviewInternal;

        _currentPreview = preview;
        _dialogService.ShowPreview(preview);
    }

    private void MovePreview(int offset)
    {
        var newIndex = _previewIndex + offset;
        if (newIndex < 0 || newIndex >= _previewNodes.Count)
        {
            return;
        }

        _previewIndex = newIndex;
        ShowPreviewAtCurrentIndex();
    }

    private void ClosePreviewInternal()
    {
        _dialogService.ClosePreview();
        _currentPreview = null;
        _previewNodes = new List<FileSystemNodeViewModel>();
        _previewIndex = -1;
    }

    // 仕様書11章：プレビューを開いたまま選択を変更すると、固定（15章）中でない限り追従する。
    private void OnActivePaneSelectionChanged()
    {
        if (_currentPreview is null || _currentPreview.IsPinned)
        {
            return;
        }

        var pane = ActiveTab?.ActivePane;
        var node = pane?.PrimarySelectedNode;
        if (pane is null || node is null)
        {
            return;
        }

        _previewNodes = pane.VisibleNodes.ToList();
        _previewIndex = _previewNodes.IndexOf(node);
        ShowPreviewAtCurrentIndex();
    }

    private void OpenSettings()
    {
        var settingsViewModel = new SettingsViewModel(
            _settingsService,
            _dialogService,
            _themeService,
            _explorerIntegrationService,
            SetTrayEnabled,
            SetGlobalHotkeyEnabled);
        _dialogService.ShowSettings(settingsViewModel);
    }

    // 仕様書40章：システムトレイの常駐ON/OFF。
    private void SetTrayEnabled(bool enabled)
    {
        if (Application.Current?.MainWindow is not { } window)
        {
            return;
        }

        if (enabled)
        {
            _trayIconService.Show(window, BuildTrayMenuItems(), () => ShowMainWindow(window), () => OpenSettingsFromTray(window), ExitApplication);
        }
        else
        {
            _trayIconService.Hide();
        }
    }

    // 仕様書41章：グローバルホットキーの登録/解除。競合時はfalseを返す。
    private bool SetGlobalHotkeyEnabled(bool enabled, ModifierKeys modifiers, Key key)
    {
        _globalHotkeyService.Unregister();

        if (!enabled)
        {
            return true;
        }

        if (Application.Current?.MainWindow is not { } window)
        {
            return false;
        }

        return _globalHotkeyService.Register(window, modifiers, key, () => ShowMainWindow(window));
    }

    /// <summary>起動時（App.xaml.cs）に設定済みのトレイ・ホットキーを反映する。</summary>
    public void InitializeWindowsIntegration(Window window)
    {
        var settings = _settingsService.Current.WindowsIntegration;

        if (settings.MinimizeToTray)
        {
            _trayIconService.Show(window, BuildTrayMenuItems(), () => ShowMainWindow(window), () => OpenSettingsFromTray(window), ExitApplication);
        }

        if (settings.GlobalHotkeyEnabled)
        {
            var modifiers = Enum.TryParse<ModifierKeys>(settings.HotkeyModifiers, out var parsedModifiers)
                ? parsedModifiers
                : ModifierKeys.Control | ModifierKeys.Alt;
            var key = Enum.TryParse<Key>(settings.HotkeyKey, ignoreCase: true, out var parsedKey) ? parsedKey : Key.E;

            _globalHotkeyService.Register(window, modifiers, key, () => ShowMainWindow(window));
        }
    }

    /// <summary>仕様書39章：ジャンプリストからのワークスペース直接起動（`--workspace 名前`）。</summary>
    public void LoadWorkspaceFromStartup(Window window, string name) => LoadWorkspaceByName(window, name);

    /// <summary>常駐中、ウィンドウを閉じた際にトレイへ格納すべきかどうか。</summary>
    public bool ShouldHideToTrayOnClose => !_isExiting && _settingsService.Current.WindowsIntegration.MinimizeToTray && _trayIconService.IsVisible;

    private void OpenSettingsFromTray(Window window)
    {
        ShowMainWindow(window);
        OpenSettings();
    }

    private static void ShowMainWindow(Window window)
    {
        window.Show();

        if (window.WindowState == WindowState.Minimized)
        {
            window.WindowState = WindowState.Normal;
        }

        window.Activate();
    }

    private void ExitApplication()
    {
        _isExiting = true;
        _globalHotkeyService.Unregister();
        _trayIconService.Hide();
        Application.Current.Shutdown();
    }

    /// <summary>アプリ終了時（App.OnExit）にトレイアイコン・ホットキー登録を後始末する。</summary>
    public void ShutdownWindowsIntegration()
    {
        _globalHotkeyService.Unregister();
        _trayIconService.Hide();
    }

    // 仕様書40章：トレイメニュー本体。最近の場所・お気に入り・ワークスペースは
    // サブメニューとして都度最新の内容を構築する。
    private IReadOnlyList<TrayMenuItem> BuildTrayMenuItems()
    {
        Window? window = Application.Current?.MainWindow;

        List<TrayMenuItem> BuildPathItems(IEnumerable<FavoriteEntry> entries) =>
            entries.Select(entry => new TrayMenuItem
            {
                Text = entry.Name,
                Execute = () =>
                {
                    if (window is not null)
                    {
                        ShowMainWindow(window);
                    }

                    NavigateActiveTo(entry.Path);
                }
            }).ToList();

        var recentItems = BuildPathItems(NavigationPane.RecentPlaces);
        var favoriteItems = BuildPathItems(NavigationPane.Favorites);
        var workspaceItems = NavigationPane.Workspaces.Select(name => new TrayMenuItem
        {
            Text = name,
            Execute = () =>
            {
                if (window is not null)
                {
                    ShowMainWindow(window);
                    LoadWorkspaceByName(window, name);
                }
            }
        }).ToList();

        return new List<TrayMenuItem>
        {
            new() { Text = "最近の場所", Children = recentItems.Count > 0 ? recentItems : null },
            new() { Text = "お気に入り", Children = favoriteItems.Count > 0 ? favoriteItems : null },
            new() { Text = "ワークスペース", Children = workspaceItems.Count > 0 ? workspaceItems : null },
            new()
            {
                Text = "SSH接続の管理...",
                Execute = () =>
                {
                    if (window is not null)
                    {
                        ShowMainWindow(window);
                    }

                    OpenSshConnection();
                }
            },
            new()
            {
                Text = "ターミナル表示/非表示",
                Execute = () =>
                {
                    if (window is not null)
                    {
                        ShowMainWindow(window);
                    }

                    ToggleTerminalCommand.Execute(null);
                }
            }
        };
    }

    // 仕様書44章：SSH接続の登録・管理ダイアログを開く。「接続」実行時は
    // 統合ターミナル(9章)上でssh接続を確立する。
    private void OpenSshConnection()
    {
        var profilesViewModel = new SshProfilesViewModel(_settingsService, _dialogService, _sshService, _sshCredentialStore);
        profilesViewModel.RequestConnect += (command, password) => TerminalHost.SendRawCommand(command, password);
        _dialogService.ShowSshProfiles(profilesViewModel);
    }

    // 仕様書12章：現在フォルダ以下を検索する。
    private void OpenSearch()
    {
        var pane = ActiveTab?.ActivePane;
        if (pane is null || pane.IsAtComputerRoot)
        {
            return;
        }

        var searchViewModel = new SearchViewModel(pane.CurrentPath, _folderScanService, _dialogService);
        searchViewModel.NavigateRequested += folder => pane.NavigateTo(folder);
        _dialogService.ShowSearch(searchViewModel);
    }

    // 仕様書38章・58章・59章：巨大ファイル/重複ファイル/空フォルダ検索。
    private void OpenDiskAnalysis()
    {
        var initialPath = ActiveTab?.ActivePane is { IsAtComputerRoot: false } pane
            ? pane.CurrentPath
            : GetDefaultInitialPath();

        var viewModel = new DiskAnalysisViewModel(initialPath, _folderScanService, _fileSystemService, _dialogService);
        _dialogService.ShowDiskAnalysis(viewModel);
    }

    // 仕様書46章：主要操作を検索・実行するコマンドパレット。
    // 各エントリはクロージャとして持つため、パレットを開いた後にActiveTab/ActivePaneが
    // 変わっても常に「実行時点」の状態を参照する。
    private void OpenCommandPalette()
    {
        var entries = new List<CommandPaletteEntry>
        {
            new() { Name = "検索を開く (Ctrl+F)", Execute = OpenSearch },
            new() { Name = "ディスク解析を開く", Execute = OpenDiskAnalysis },
            new() { Name = "設定を開く", Execute = () => OpenSettingsCommand.Execute(null) },
            new() { Name = "ショートカット一覧", Execute = () => OpenCheatSheetCommand.Execute(null) },
            new() { Name = "新しいタブ (Ctrl+T)", Execute = () => AddTabCommand.Execute(null) },
            new() { Name = "タブを閉じる (Ctrl+W)", Execute = () => CloseTabCommand.Execute(ActiveTab), CanExecute = () => CloseTabCommand.CanExecute(ActiveTab) },
            new() { Name = "右に分割", Execute = () => SplitHorizontalCommand.Execute(null), CanExecute = () => SplitHorizontalCommand.CanExecute(null) },
            new() { Name = "下に分割", Execute = () => SplitVerticalCommand.Execute(null), CanExecute = () => SplitVerticalCommand.CanExecute(null) },
            new() { Name = "ペインを閉じる", Execute = () => ClosePaneCommand.Execute(null), CanExecute = () => ClosePaneCommand.CanExecute(null) },
            new() { Name = "ペインを入れ替え", Execute = () => ActiveTab?.SwapPanesCommand.Execute(null), CanExecute = () => ActiveTab?.SwapPanesCommand.CanExecute(null) == true },
            new() { Name = "隠しファイルの表示切替", Execute = () => ActiveTab?.ActivePane.ToggleShowHiddenFilesCommand.Execute(null) },
            new() { Name = "最新の情報に更新 (F5)", Execute = () => ActiveTab?.ActivePane.RefreshCommand.Execute(null) },
            new() { Name = "ターミナルの表示/非表示 (Ctrl+@)", Execute = ToggleTerminal },
            new() { Name = "プレビュー (Space)", Execute = () => TogglePreviewCommand.Execute(null) },
            new() { Name = "Git/SVN情報ペインの表示/非表示", Execute = () => ToggleVcsPaneCommand.Execute(null) },
            new() { Name = "現在の場所をブックマークに追加 (Ctrl+D)", Execute = () => AddFavoriteCommand.Execute(null) },
            new()
            {
                Name = "Patchを作成...",
                Execute = () => ActiveTab?.ActivePane.CreatePatchCommand.Execute(null),
                CanExecute = () => ActiveTab?.ActivePane.CreatePatchCommand.CanExecute(null) == true
            },
            new()
            {
                Name = "Patchを適用...",
                Execute = () => ActiveTab?.ActivePane.ApplyPatchCommand.Execute(null),
                CanExecute = () => ActiveTab?.ActivePane.ApplyPatchCommand.CanExecute(null) == true
            },
            new()
            {
                Name = "変更をステージ",
                Execute = () => ActiveTab?.ActivePane.StageAllCommand.Execute(null),
                CanExecute = () => ActiveTab?.ActivePane.StageAllCommand.CanExecute(null) == true
            },
            new()
            {
                Name = "コミット...",
                Execute = () => ActiveTab?.ActivePane.CommitCommand.Execute(null),
                CanExecute = () => ActiveTab?.ActivePane.CommitCommand.CanExecute(null) == true
            },
            new() { Name = "SSH接続の管理...", Execute = OpenSshConnection },
            new()
            {
                Name = "プロジェクトルートへ移動",
                Execute = () => ActiveTab?.ActivePane.GoToProjectRootCommand.Execute(null),
                CanExecute = () => ActiveTab?.ActivePane.GoToProjectRootCommand.CanExecute(null) == true
            },
            new()
            {
                Name = "ソリューションルートへ移動",
                Execute = () => ActiveTab?.ActivePane.GoToSolutionRootCommand.Execute(null),
                CanExecute = () => ActiveTab?.ActivePane.GoToSolutionRootCommand.CanExecute(null) == true
            },
            new()
            {
                Name = "Gitルートへ移動",
                Execute = () => ActiveTab?.ActivePane.GoToGitRootCommand.Execute(null),
                CanExecute = () => ActiveTab?.ActivePane.GoToGitRootCommand.CanExecute(null) == true
            },
            new()
            {
                Name = "ワークスペースを保存...",
                Execute = () => SaveWorkspaceCommand.Execute(Application.Current?.MainWindow)
            },
            new()
            {
                Name = "ワークスペースを開く...",
                Execute = () => LoadWorkspaceCommand.Execute(Application.Current?.MainWindow)
            }
        };

        var paletteViewModel = new CommandPaletteViewModel(entries);
        _dialogService.ShowCommandPalette(paletteViewModel);
    }

    private void AddCurrentFolderToFavorites()
    {
        var pane = ActiveTab?.ActivePane;
        if (pane is null || pane.IsAtComputerRoot)
        {
            return;
        }

        var name = Path.GetFileName(pane.CurrentPath.TrimEnd('\\'));
        NavigationPane.AddFavorite(string.IsNullOrEmpty(name) ? pane.CurrentPath : name, pane.CurrentPath);
        RebuildJumpList();
    }

    private void AddNewTag()
    {
        var name = _dialogService.PromptText("新しいタグ", "タグ名を入力してください。");
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        NavigationPane.AddTag(name.Trim());
    }

    private void SaveWorkspace(Window window)
    {
        var name = _dialogService.PromptText("ワークスペースの保存", "ワークスペース名を入力してください。", "既定のワークスペース");
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        var state = new WorkspaceState
        {
            Name = name,
            WindowWidth = window.Width,
            WindowHeight = window.Height,
            WindowLeft = window.Left,
            WindowTop = window.Top,
            ActiveTabIndex = ActiveTab is null ? 0 : Tabs.IndexOf(ActiveTab),
            NavigationPaneCollapsed = NavigationPane.IsCollapsed,
            Tabs = Tabs.Select(t => new TabState
            {
                Header = t.Header,
                ActivePaneIndex = t.ActivePaneIndex,
                SplitOrientation = t.SplitOrientation,
                IsPinned = t.IsPinned,
                Panes = t.Panes.Select(p => new PaneState
                {
                    CurrentPath = p.CurrentPath,
                    ViewMode = p.CurrentViewMode
                }).ToList()
            }).ToList()
        };

        _workspaceService.SaveWorkspace(state);
        NavigationPane.RefreshWorkspaces(_workspaceService.GetWorkspaceNames());
        RebuildJumpList();
        _dialogService.ShowInfo($"ワークスペース「{name}」を保存しました。");
    }

    private void LoadWorkspace(Window window)
    {
        var names = _workspaceService.GetWorkspaceNames();
        if (names.Count == 0)
        {
            _dialogService.ShowInfo("保存済みのワークスペースがありません。");
            return;
        }

        var selected = _dialogService.SelectFromList("ワークスペースを開く", "読み込むワークスペースを選択してください。", names);
        if (selected is null)
        {
            return;
        }

        LoadWorkspaceByName(window, selected);
    }

    // 仕様書43章：左ペインの「ワークスペース」一覧からの直接読み込みにも使う。
    private void LoadWorkspaceByName(Window window, string name)
    {
        var state = _workspaceService.GetWorkspace(name);
        if (state is null)
        {
            return;
        }

        window.Width = state.WindowWidth;
        window.Height = state.WindowHeight;
        window.Left = state.WindowLeft;
        window.Top = state.WindowTop;
        NavigationPane.IsCollapsed = state.NavigationPaneCollapsed;

        foreach (var oldTab in Tabs)
        {
            foreach (var pane in oldTab.Panes)
            {
                pane.Dispose();
            }
        }

        Tabs.Clear();

        foreach (var tabState in state.Tabs)
        {
            var paneStates = tabState.Panes.Count > 0
                ? tabState.Panes
                : new List<PaneState> { new() { CurrentPath = GetDefaultInitialPath() } };

            var firstPane = CreatePane(paneStates[0].CurrentPath, paneStates[0].ViewMode);
            var tab = new TabViewModel(firstPane, tabState.Header)
            {
                SplitOrientation = tabState.SplitOrientation,
                IsPinned = tabState.IsPinned
            };

            for (var i = 1; i < paneStates.Count; i++)
            {
                tab.AddPane(CreatePane(paneStates[i].CurrentPath, paneStates[i].ViewMode));
            }

            tab.SetActivePane(tab.Panes[Math.Clamp(tabState.ActivePaneIndex, 0, tab.Panes.Count - 1)]);
            Tabs.Add(tab);
        }

        if (Tabs.Count == 0)
        {
            AddTab(GetDefaultInitialPath());
        }
        else
        {
            ActiveTab = Tabs[Math.Clamp(state.ActiveTabIndex, 0, Tabs.Count - 1)];
        }
    }
}
