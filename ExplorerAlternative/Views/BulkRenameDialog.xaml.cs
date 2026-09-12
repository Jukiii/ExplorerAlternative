using System.Windows;
using ExplorerAlternative.ViewModels;

namespace ExplorerAlternative.Views;

public partial class BulkRenameDialog : Window
{
    public BulkRenameDialog(BulkRenameViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void ApplyButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }
}
