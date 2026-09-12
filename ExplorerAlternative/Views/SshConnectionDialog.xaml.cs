using System.Windows;
using ExplorerAlternative.ViewModels;
using Microsoft.Win32;

namespace ExplorerAlternative.Views;

public partial class SshConnectionDialog : Window
{
    public SshConnectionDialog(SshConnectionViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void ConnectButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "秘密鍵ファイルを選択",
            Filter = "すべてのファイル (*.*)|*.*"
        };

        if (dialog.ShowDialog(this) == true && DataContext is SshConnectionViewModel viewModel)
        {
            viewModel.IdentityFilePath = dialog.FileName;
        }
    }
}
