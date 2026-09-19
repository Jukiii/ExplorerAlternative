using System.Windows;
using ExplorerAlternative.ViewModels;

namespace ExplorerAlternative.Views;

public partial class TagEditorDialog : Window
{
    public TagEditorDialog(TagEditorViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is TagEditorViewModel viewModel && string.IsNullOrWhiteSpace(viewModel.Name))
        {
            return;
        }

        DialogResult = true;
    }

    private void ClearColorButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is TagEditorViewModel viewModel)
        {
            viewModel.ColorHex = null;
        }
    }
}
