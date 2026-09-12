using System.Windows;
using ExplorerAlternative.ViewModels;

namespace ExplorerAlternative.Views;

public partial class DiffWindow : Window
{
    public DiffWindow(DiffViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Title = $"Diff - {viewModel.Title}";
        viewModel.ScrollToRowRequested += index =>
        {
            if (index >= 0 && index < viewModel.Rows.Count)
            {
                RowsListBox.ScrollIntoView(viewModel.Rows[index]);
            }
        };
    }
}
