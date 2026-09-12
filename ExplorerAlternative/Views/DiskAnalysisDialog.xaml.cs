using System.Windows;
using ExplorerAlternative.ViewModels;
using Microsoft.Win32;

namespace ExplorerAlternative.Views;

public partial class DiskAnalysisDialog : Window
{
    public DiskAnalysisDialog(DiskAnalysisViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "対象フォルダを選択" };

        if (dialog.ShowDialog(this) == true && DataContext is DiskAnalysisViewModel viewModel)
        {
            viewModel.TargetPath = dialog.FolderName;
        }
    }
}
