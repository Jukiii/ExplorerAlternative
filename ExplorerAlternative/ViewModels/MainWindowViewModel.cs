using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
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

    private TabViewModel? _activeTab;
    private PreviewViewModel? _currentPreview;
    private List<FileSystemNodeViewModel> _previewNodes = new();
    private int _previewIndex = -1;
    private int _tabCounter;

    public MainWindowViewModel(
        IFileSystemService fileSystemService,
        IDialogService dialogService,
        IVersionControlService versionControlService,
        IExternalToolService externalToolService,
        ISettingsService settingsService,
        IPowerShellTerminalService terminalService,
        IWorkspaceService workspaceService,
        IThemeService themeService,
        IPatchService patchService,
        ISshService sshService,
        IVersionControlOperationsService versionControlOperationsService)
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

        NavigationPane = new NavigationPaneViewModel(settingsService, fileSystemService, NavigateActiveTo, OpenFile);
        NavigationPane.WorkspaceOpenRequested += name => LoadWorkspaceByName((Window)Application.Current!.MainWindow!, name);
        Terminal = new TerminalViewModel(terminalService, settingsService.Current.Terminal.SyncByDefault);

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

        AddTab(GetDefaultInitialPath());
    }

    public ObservableCollection<TabViewModel> Tabs { get; } = new();

    public NavigationPaneViewModel NavigationPane { get; }

    public TerminalViewModel Terminal { get; }

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
                Terminal.SyncCurrentDirectory(_activeTab.ActivePane.CurrentPath);
            }

            OnPropertyChanged();
        }
    }

    private static string GetDefaultInitialPath()
    {
        return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
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
        Terminal.SyncCurrentDirectory(path);
        NavigationPane.RecordRecentPlace(path);
    }

    // 仕様書50章「最近使った場所」・22章「アプリで開く」相当：既定のアプリでファイルを開く。
    private void OpenFile(string path)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            _dialogService.ShowError($"ファイルを開けませんでした。({ex.Message})");
        }
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
            initialPath,
            initialViewMode);

        pane.RunTerminalCommandRequested += RunTerminalCommand;
        pane.PinFileRequested += node => NavigationPane.AddPinnedFile(node.Name, node.FullPath);
        pane.OpenInNewTabRequested += OpenPathInNewTab;
        return pane;
    }

    // 仕様書13章・20章：Git/SVN操作コマンドを統合ターミナル（9章）上で実行する。
    // 資格情報の入力待ちなどの対話にも、通常のターミナル操作と同じ画面で対応できる。
    private void RunTerminalCommand(string command)
    {
        Terminal.IsVisible = true;
        Terminal.SendRawCommand(command);
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
        Terminal.SyncCurrentDirectory(newPane.CurrentPath);
    }

    private void ClosePane()
    {
        var tab = ActiveTab;
        if (tab is null || !tab.CanClosePane)
        {
            return;
        }

        tab.RemovePane(tab.ActivePane);
        Terminal.SyncCurrentDirectory(tab.ActivePane.CurrentPath);
    }

    private void SetActivePane(PaneViewModel pane)
    {
        ActiveTab?.SetActivePane(pane);
        Terminal.SyncCurrentDirectory(pane.CurrentPath);
    }

    private void ToggleTerminal()
    {
        Terminal.ToggleVisibilityCommand.Execute(null);

        if (Terminal.IsVisible && ActiveTab is not null && !ActiveTab.ActivePane.IsAtComputerRoot)
        {
            Terminal.SyncCurrentDirectory(ActiveTab.ActivePane.CurrentPath);
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
        var settingsViewModel = new SettingsViewModel(_settingsService, _dialogService, _themeService);
        _dialogService.ShowSettings(settingsViewModel);
    }

    // 仕様書15章：SSH接続情報を入力し、統合ターミナル(9章)上でssh接続を確立する。
    private void OpenSshConnection()
    {
        var sshViewModel = new SshConnectionViewModel();
        if (!_dialogService.ShowSshConnection(sshViewModel))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(sshViewModel.Host))
        {
            _dialogService.ShowError("接続先ホストを入力してください。");
            return;
        }

        try
        {
            var command = _sshService.BuildConnectCommand(sshViewModel.ToProfile());
            Terminal.IsVisible = true;
            Terminal.SendRawCommand(command);
        }
        catch (AppOperationException ex)
        {
            _dialogService.ShowError(ex.Message);
        }
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
