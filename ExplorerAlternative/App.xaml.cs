using System.Windows;
using ExplorerAlternative.Services;
using ExplorerAlternative.Services.Abstractions;
using ExplorerAlternative.ViewModels;

namespace ExplorerAlternative;

public partial class App : Application
{
    private IPowerShellTerminalService? _terminalService;
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

        var terminalService = new PowerShellTerminalService(settingsService.Current.Terminal.ShellExecutable);
        _terminalService = terminalService;

        var workspaceService = new WorkspaceService(settingsService);
        var patchService = new PatchService();
        var sshService = new SshService();
        var versionControlOperationsService = new VersionControlOperationsService();

        var mainWindowViewModel = new MainWindowViewModel(
            fileSystemService,
            dialogService,
            versionControlService,
            externalToolService,
            settingsService,
            terminalService,
            workspaceService,
            themeService,
            patchService,
            sshService,
            versionControlOperationsService);

        var mainWindow = new MainWindow { DataContext = mainWindowViewModel };
        MainWindow = mainWindow;
        mainWindow.Show();
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

        _terminalService?.Dispose();
        base.OnExit(e);
    }
}
