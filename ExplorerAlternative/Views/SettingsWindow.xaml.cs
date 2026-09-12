using System.Windows;
using ExplorerAlternative.ViewModels;

namespace ExplorerAlternative.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow(SettingsViewModel settingsViewModel)
    {
        InitializeComponent();
        DataContext = settingsViewModel;
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
