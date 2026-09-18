using System.Windows;
using ExplorerAlternative.Services.Abstractions;
using ExplorerAlternative.ViewModels;
using ExplorerAlternative.Views;
using Microsoft.Win32;

namespace ExplorerAlternative.Services;

public sealed class DialogService : IDialogService
{
    private PreviewWindow? _previewWindow;

    // 仕様書27章：MainWindow構築中（起動引数のフォルダ読み込み失敗など、MainWindowが
    // まだApplication.Current.MainWindowに割り当てられる前）にエラーを表示する経路があるため、
    // owner未確定時はowner無しのオーバーロードにフォールバックし、クラッシュを避ける。
    public void ShowError(string message)
    {
        if (Application.Current?.MainWindow is { } owner)
        {
            MessageBox.Show(owner, message, "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        else
        {
            MessageBox.Show(message, "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    public void ShowInfo(string message)
    {
        if (Application.Current?.MainWindow is { } owner)
        {
            MessageBox.Show(owner, message, "情報", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            MessageBox.Show(message, "情報", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    public bool Confirm(string message)
    {
        var result = Application.Current?.MainWindow is { } owner
            ? MessageBox.Show(owner, message, "確認", MessageBoxButton.YesNo, MessageBoxImage.Question)
            : MessageBox.Show(message, "確認", MessageBoxButton.YesNo, MessageBoxImage.Question);

        return result == MessageBoxResult.Yes;
    }

    public string? PromptText(string title, string message, string defaultValue = "")
    {
        var dialog = new InputDialog(title, message, defaultValue)
        {
            Owner = Application.Current?.MainWindow
        };

        return dialog.ShowDialog() == true ? dialog.InputText : null;
    }

    public string? SelectFromList(string title, string message, IReadOnlyList<string> items)
    {
        var dialog = new SelectionDialog(title, message, items)
        {
            Owner = Application.Current?.MainWindow
        };

        return dialog.ShowDialog() == true ? dialog.SelectedItem : null;
    }

    public void ShowPreview(PreviewViewModel previewViewModel)
    {
        if (_previewWindow is null)
        {
            _previewWindow = new PreviewWindow { Owner = Application.Current?.MainWindow };
            _previewWindow.Closed += (_, _) => _previewWindow = null;
        }

        _previewWindow.SetPreview(previewViewModel);

        if (!_previewWindow.IsVisible)
        {
            _previewWindow.Show();
        }
    }

    public void ClosePreview()
    {
        _previewWindow?.Close();
        _previewWindow = null;
    }

    public void ShowCheatSheet()
    {
        var window = new CheatSheetWindow { Owner = Application.Current?.MainWindow };
        window.Show();
    }

    public void ShowSettings(SettingsViewModel settingsViewModel)
    {
        var window = new SettingsWindow(settingsViewModel) { Owner = Application.Current?.MainWindow };
        window.ShowDialog();
    }

    public bool ShowBulkRename(BulkRenameViewModel bulkRenameViewModel)
    {
        var window = new BulkRenameDialog(bulkRenameViewModel) { Owner = Application.Current?.MainWindow };
        return window.ShowDialog() == true;
    }

    public string? ShowSaveFileDialog(string title, string filter, string defaultFileName)
    {
        var dialog = new SaveFileDialog { Title = title, Filter = filter, FileName = defaultFileName };
        return dialog.ShowDialog(Application.Current?.MainWindow) == true ? dialog.FileName : null;
    }

    public string? ShowOpenFileDialog(string title, string filter)
    {
        var dialog = new OpenFileDialog { Title = title, Filter = filter };
        return dialog.ShowDialog(Application.Current?.MainWindow) == true ? dialog.FileName : null;
    }

    public bool ShowSshConnection(SshConnectionViewModel sshConnectionViewModel)
    {
        var window = new SshConnectionDialog(sshConnectionViewModel) { Owner = Application.Current?.MainWindow };
        return window.ShowDialog() == true;
    }

    public bool ShowTagEditor(TagEditorViewModel tagEditorViewModel)
    {
        var window = new TagEditorDialog(tagEditorViewModel) { Owner = Application.Current?.MainWindow };
        return window.ShowDialog() == true;
    }

    public void ShowUndoHistory(UndoHistoryViewModel undoHistoryViewModel)
    {
        var window = new UndoHistoryDialog(undoHistoryViewModel) { Owner = Application.Current?.MainWindow };
        window.Show();
    }

    public void ShowSshProfiles(SshProfilesViewModel sshProfilesViewModel)
    {
        var window = new SshProfilesDialog(sshProfilesViewModel) { Owner = Application.Current?.MainWindow };
        window.ShowDialog();
    }

    public void ShowSearch(SearchViewModel searchViewModel)
    {
        var window = new SearchDialog(searchViewModel) { Owner = Application.Current?.MainWindow };
        window.Show();
    }

    public void ShowDiskAnalysis(DiskAnalysisViewModel diskAnalysisViewModel)
    {
        var window = new DiskAnalysisDialog(diskAnalysisViewModel) { Owner = Application.Current?.MainWindow };
        window.Show();
    }

    public void ShowCommandPalette(CommandPaletteViewModel commandPaletteViewModel)
    {
        var window = new CommandPaletteDialog(commandPaletteViewModel) { Owner = Application.Current?.MainWindow };
        window.ShowDialog();
    }

    public void ShowProperties(PropertiesViewModel propertiesViewModel)
    {
        var window = new PropertiesDialog(propertiesViewModel) { Owner = Application.Current?.MainWindow };
        window.ShowDialog();
    }

    public void ShowDiff(DiffViewModel diffViewModel)
    {
        var window = new DiffWindow(diffViewModel) { Owner = Application.Current?.MainWindow };
        window.Show();
    }

    public void ShowGitLog(GitLogViewModel gitLogViewModel)
    {
        var window = new GitLogWindow(gitLogViewModel) { Owner = Application.Current?.MainWindow };
        window.Show();
    }

    public void ShowFolderCompare(FolderCompareViewModel folderCompareViewModel)
    {
        var window = new FolderCompareWindow(folderCompareViewModel) { Owner = Application.Current?.MainWindow };
        window.Show();
    }

    public void ShowSftpBrowser(SftpBrowserViewModel sftpBrowserViewModel)
    {
        var window = new SftpBrowserWindow(sftpBrowserViewModel) { Owner = Application.Current?.MainWindow };
        window.Show();
    }
}
