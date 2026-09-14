using System.Windows;
using ExplorerAlternative.ViewModels;
using Microsoft.Win32;

namespace ExplorerAlternative.Views;

public partial class FolderCompareWindow : Window
{
    public FolderCompareWindow(FolderCompareViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void BrowseLeftButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "左側のフォルダを選択" };
        if (dialog.ShowDialog(this) == true && DataContext is FolderCompareViewModel viewModel)
        {
            viewModel.LeftFolder = dialog.FolderName;
        }
    }

    private void BrowseRightButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "右側のフォルダを選択" };
        if (dialog.ShowDialog(this) == true && DataContext is FolderCompareViewModel viewModel)
        {
            viewModel.RightFolder = dialog.FolderName;
        }
    }
}
