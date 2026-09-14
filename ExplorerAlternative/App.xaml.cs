using System.Windows;
using ExplorerAlternative.Services;
using ExplorerAlternative.Services.Abstractions;
using ExplorerAlternative.ViewModels;

namespace ExplorerAlternative;

public partial class App : Application
{
    private MainWindowViewModel? _mainWindowViewModel;
    private ISettingsService? _settingsService;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var dialogService = new DialogService();
        var fileSystemService = new FileSystemService();
        var versionControlService = new VersionControlService();
        var externalToolService = new ExternalToolService();
        var settingsService = new SettingsService();
        settingsService.Load();
        _settingsService = settingsService;

        var themeService = new ThemeService();
        themeService.Apply(settingsService.Current.Appearance.Theme);
        Resources["ActivePaneHighlightOpacity"] = settingsService.Current.Appearance.ActivePaneHighlightOpacity;

        // 新しく開かれるウィンドウ（設定・各種ダイアログ含む）すべてに対して、生成のたびに
        // ThemeServiceを個別に呼び出す必要がないよう、Window型のLoadedをクラスハンドラで
        // 一括購読し、タイトルバーの明暗を自動的に追従させる（Loaded時点でHWNDは確定済み）。
        EventManager.RegisterClassHandler(
            typeof(Window),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler((sender, _) =>
            {
                if (sender is Window window)
                {
                    themeService.ApplyTitleBarToWindow(window);
                }
            }));

        IPowerShellTerminalService TerminalServiceFactory() =>
            new PowerShellTerminalService(
                settingsService.Current.Terminal.ShellExecutable,
                settingsService.Current.Terminal.LoadProfile);

        var workspaceService = new WorkspaceService(settingsService);
        var patchService = new PatchService();
        var sshService = new SshService();
        var sshCredentialStore = new WindowsCredentialSshStore();
        var sftpService = new SftpService();
        var folderScanService = new FolderScanService();
        var folderCompareService = new FolderCompareService();
        var versionControlOperationsService = new VersionControlOperationsService();
        var diffService = new DiffService();
        var explorerIntegrationService = new ExplorerIntegrationService();
        var trayIconService = new TrayIconService();
        var globalHotkeyService = new GlobalHotkeyService();
        var jumpListService = new JumpListService();
        var projectDetectionService = new ProjectDetectionService();
        IFolderWatcherService FolderWatcherServiceFactory() => new FolderWatcherService();

        // 仕様書39章：`--workspace 名前` はジャンプリストからのワークスペース直接起動。
        // それ以外の第1引数は35章「Explorerから本アプリへフォルダを渡して開く」用のパス。
        string? startupPath = null;
        string? startupWorkspace = null;

        if (e.Args.Length > 0)
        {
            if (e.Args[0] == "--workspace" && e.Args.Length > 1)
            {
                startupWorkspace = e.Args[1];
            }
            else
            {
                startupPath = e.Args[0];
            }
        }

        var mainWindowViewModel = new MainWindowViewModel(
            fileSystemService,
            dialogService,
            versionControlService,
            externalToolService,
            settingsService,
            TerminalServiceFactory,
            workspaceService,
            themeService,
            patchService,
            sshService,
            sftpService,
            versionControlOperationsService,
            diffService,
            sshCredentialStore,
            folderScanService,
            folderCompareService,
            explorerIntegrationService,
            trayIconService,
            globalHotkeyService,
            jumpListService,
            projectDetectionService,
            FolderWatcherServiceFactory,
            startupPath);

        _mainWindowViewModel = mainWindowViewModel;

        var mainWindow = new MainWindow { DataContext = mainWindowViewModel };
        MainWindow = mainWindow;
        mainWindow.Show();

        if (startupWorkspace is not null)
        {
            mainWindowViewModel.LoadWorkspaceFromStartup(mainWindow, startupWorkspace);
        }

        mainWindowViewModel.InitializeWindowsIntegration(mainWindow);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            _settingsService?.Save();
        }
        catch (AppOperationException)
        {
            // 終了時の保存失敗はアプリの終了を妨げない。
        }

        _mainWindowViewModel?.ShutdownWindowsIntegration();
        _mainWindowViewModel?.TerminalHost.Dispose();
        base.OnExit(e);
    }
}
