using System.Windows;
using ExplorerAlternative.ViewModels;

namespace ExplorerAlternative.Views;

public partial class GitLogWindow : Window
{
    public GitLogWindow(GitLogViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Title = viewModel.Title;
    }
}
