using System.Windows;
using ExplorerAlternative.ViewModels;

namespace ExplorerAlternative.Views;

public partial class FileOperationHistoryDialog : Window
{
    public FileOperationHistoryDialog(FileOperationHistoryViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
