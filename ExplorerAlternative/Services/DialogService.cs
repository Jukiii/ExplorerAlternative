using System.Windows;
using ExplorerAlternative.Services.Abstractions;
using ExplorerAlternative.ViewModels;
using ExplorerAlternative.Views;

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
}
