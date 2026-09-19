using System.Windows;
using System.Windows.Input;
using ExplorerAlternative.Services.Abstractions;
using ExplorerAlternative.ViewModels;

namespace ExplorerAlternative.Views;

public partial class UndoHistoryDialog : Window
{
    public UndoHistoryDialog(UndoHistoryViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void EntryText_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2 &&
            sender is FrameworkElement { DataContext: UndoHistoryEntry entry } &&
            DataContext is UndoHistoryViewModel viewModel)
        {
            viewModel.UndoToCommand.Execute(entry);
        }
    }

    private void Window_Closed(object sender, EventArgs e)
    {
        (DataContext as UndoHistoryViewModel)?.Detach();
    }
}
