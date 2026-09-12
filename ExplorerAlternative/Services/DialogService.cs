using System.Windows;
using ExplorerAlternative.Services.Abstractions;
using ExplorerAlternative.ViewModels;
using ExplorerAlternative.Views;
using Microsoft.Win32;

namespace ExplorerAlternative.Services;

public sealed class DialogService : IDialogService
{
    private PreviewWindow? _previewWindow;

    public void ShowError(string message)
    {
        MessageBox.Show(Application.Current?.MainWindow!, message, "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    public void ShowInfo(string message)
    {
        MessageBox.Show(Application.Current?.MainWindow!, message, "情報", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    public bool Confirm(string message)
    {
        return MessageBox.Show(Application.Current?.MainWindow!, message, "確認", MessageBoxButton.YesNo, MessageBoxImage.Question)
            == MessageBoxResult.Yes;
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
}
