using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ExplorerAlternative.Models;
using ExplorerAlternative.ViewModels;

namespace ExplorerAlternative.Views;

public partial class SearchDialog : Window
{
    public SearchDialog(SearchViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Loaded += (_, _) => QueryTextBox.Focus();
    }

    private void ResultsListView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListView { SelectedItem: FileSystemEntry entry } &&
            DataContext is SearchViewModel viewModel &&
            viewModel.NavigateToResultCommand.CanExecute(entry))
        {
            viewModel.NavigateToResultCommand.Execute(entry);
            Close();
        }
    }
}
