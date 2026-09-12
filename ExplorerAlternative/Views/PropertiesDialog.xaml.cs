using System.Windows;
using ExplorerAlternative.ViewModels;

namespace ExplorerAlternative.Views;

public partial class PropertiesDialog : Window
{
    public PropertiesDialog(PropertiesViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
