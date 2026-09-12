using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
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

    private TabViewModel? _activeTab;
    private bool _isPreviewOpen;
    private int _tabCounter;

    public MainWindowViewModel(
        IFileSystemService fileSystemService,
        IDialogService dialogService,
        IVersionControlService versionControlService,
        IExternalToolService externalToolService,
        ISettingsService settingsService,
        IPowerShellTerminalService terminalService,
        IWorkspaceService workspaceService)
    {
        _fileSystemService = fileSystemService;
        _dialogService = dialogService;
        _versionControlService = versionControlService;
        _externalToolService = externalToolService;
        _settingsService = settingsService;
        _workspaceService = workspaceService;

        NavigationPane = new NavigationPaneViewModel(settingsService, NavigateActiveTo);
        Terminal = new TerminalViewModel(terminalService, settingsService.Current.Terminal.SyncByDefault);

        AddTabCommand = new RelayCommand(_ => AddTab(GetDefaultInitialPath()));
        CloseTabCommand = new RelayCommand(p => CloseTab((TabViewModel)p!), _ => Tabs.Count > 1);
        GoUpCommand = new RelayCommand(_ => ActiveTab?.ActivePane.GoUpCommand.Execute(null));
        TogglePreviewCommand = new RelayCommand(_ => TogglePreview());
        ToggleTerminalCommand = new RelayCommand(_ => Terminal.ToggleVisibilityCommand.Execute(null));
        OpenCheatSheetCommand = new RelayCommand(_ => _dialogService.ShowCheatSheet());
        OpenSettingsCommand = new RelayCommand(_ => OpenSettings());
        AddFavoriteCommand = new RelayCommand(_ => AddCurrentFolderToFavorites());
        SaveWorkspaceCommand = new RelayCommand(p => SaveWorkspace((Window)p!));
        LoadWorkspaceCommand = new RelayCommand(p => LoadWorkspace((Window)p!));

        AddTab(GetDefaultInitialPath());
    }

    public ObservableCollection<TabViewModel> Tabs { get; } = new();

    public NavigationPaneViewModel NavigationPane { get; }

    public TerminalViewModel Terminal { get; }

    public RelayCommand AddTabCommand { get; }

    public RelayCommand CloseTabCommand { get; }

    public RelayCommand GoUpCommand { get; }

    public RelayCommand TogglePreviewCommand { get; }

    public RelayCommand ToggleTerminalCommand { get; }

    public RelayCommand OpenCheatSheetCommand { get; }

    public RelayCommand OpenSettingsCommand { get; }

    public RelayCommand AddFavoriteCommand { get; }

    public RelayCommand SaveWorkspaceCommand { get; }

    public RelayCommand LoadWorkspaceCommand { get; }

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
                _activeTab.ActivePane.PathChanged -= OnActivePanePathChanged;
                _activeTab.ActivePane.IsActive = false;
            }

            _activeTab = value;

            if (_activeTab is not null)
            {
                _activeTab.ActivePane.PathChanged += OnActivePanePathChanged;
                _activeTab.ActivePane.IsActive = true;
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
        var pane = new PaneViewModel(
            _fileSystemService,
            _dialogService,
            _versionControlService,
            _externalToolService,
            _settingsService,
            initialPath,
            _settingsService.Current.View.DefaultViewMode);

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

        var index = Tabs.IndexOf(tab);
        Tabs.Remove(tab);

        if (ReferenceEquals(ActiveTab, tab))
        {
            ActiveTab = Tabs[Math.Min(index, Tabs.Count - 1)];
        }
    }

    private void NavigateActiveTo(string path)
    {
        ActiveTab?.ActivePane.NavigateTo(path);
    }

    private void OnActivePanePathChanged(string path)
    {
        Terminal.SyncCurrentDirectory(path);
    }

    private void TogglePreview()
    {
        if (_isPreviewOpen)
        {
            _dialogService.ClosePreview();
            _isPreviewOpen = false;
            return;
        }

        var node = ActiveTab?.ActivePane.PrimarySelectedNode;
        if (node is null)
        {
            return;
        }

        var preview = PreviewViewModel.Create(node, _fileSystemService, _settingsService.Current.TextFileExtensions);
        _dialogService.ShowPreview(preview);
        _isPreviewOpen = true;
    }

    private void OpenSettings()
    {
        var settingsViewModel = new SettingsViewModel(_settingsService, _dialogService);
        _dialogService.ShowSettings(settingsViewModel);
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
                Panes = t.Panes.Select(p => new PaneState
                {
                    CurrentPath = p.CurrentPath,
                    ViewMode = p.CurrentViewMode
                }).ToList()
            }).ToList()
        };

        _workspaceService.SaveWorkspace(state);
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

        var state = _workspaceService.GetWorkspace(selected);
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
            var paneState = tabState.Panes.FirstOrDefault() ?? new PaneState { CurrentPath = GetDefaultInitialPath() };
            var pane = new PaneViewModel(
                _fileSystemService,
                _dialogService,
                _versionControlService,
                _externalToolService,
                _settingsService,
                paneState.CurrentPath,
                paneState.ViewMode);

            var tab = new TabViewModel(pane, tabState.Header);
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
