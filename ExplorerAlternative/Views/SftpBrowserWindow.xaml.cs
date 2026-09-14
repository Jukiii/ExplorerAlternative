using System.Windows;
using System.Windows.Controls;
using ExplorerAlternative.ViewModels;
using Microsoft.Win32;

namespace ExplorerAlternative.Views;

public partial class SftpBrowserWindow : Window
{
    public SftpBrowserWindow(SftpBrowserViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Title = $"SFTP - {viewModel.Profile.DisplayName}";

        viewModel.RequestLocalFileForUpload += () =>
        {
            var dialog = new OpenFileDialog { Title = "アップロードするファイルを選択" };
            return dialog.ShowDialog(this) == true ? dialog.FileName : null;
        };

        viewModel.RequestLocalFolderForDownload += () =>
        {
            var dialog = new OpenFolderDialog { Title = "ダウンロード先のフォルダを選択" };
            return dialog.ShowDialog(this) == true ? dialog.FolderName : null;
        };
    }

    private void ListView_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (DataContext is SftpBrowserViewModel viewModel && viewModel.OpenSelectedCommand.CanExecute(null))
        {
            viewModel.OpenSelectedCommand.Execute(null);
        }
    }

    private void Window_Closed(object? sender, EventArgs e)
    {
        (DataContext as SftpBrowserViewModel)?.Dispose();
    }
}
