using System.Windows;
using ExplorerAlternative.ViewModels;

namespace ExplorerAlternative.Views;

public partial class CheatSheetWindow : Window
{
    public CheatSheetWindow()
    {
        InitializeComponent();
        DataContext = ShortcutRegistry.All;
    }
}
