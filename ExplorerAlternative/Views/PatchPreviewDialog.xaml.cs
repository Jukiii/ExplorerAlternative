using System.Windows;
using ExplorerAlternative.ViewModels;

namespace ExplorerAlternative.Views;

public partial class PatchPreviewDialog : Window
{
    public PatchPreviewDialog(PatchPreviewViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        PatchTextBox.Document = viewModel.Document;
    }

    private void ApplyButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }
}
