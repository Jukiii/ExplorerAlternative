using System.Windows;
using ExplorerAlternative.ViewModels;

namespace ExplorerAlternative.Views;

public partial class FileOperationQueueDialog : Window
{
    public FileOperationQueueDialog(FileOperationQueueViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
