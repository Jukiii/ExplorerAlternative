using System.Windows;
using System.Windows.Input;
using ExplorerAlternative.ViewModels;

namespace ExplorerAlternative.Views;

public partial class SshProfilesDialog : Window
{
    public SshProfilesDialog(SshProfilesViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.RequestClose += () => DialogResult = true;
    }

    private void ProfilesListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is SshProfilesViewModel viewModel && viewModel.ConnectCommand.CanExecute(null))
        {
            viewModel.ConnectCommand.Execute(null);
        }
    }
}
