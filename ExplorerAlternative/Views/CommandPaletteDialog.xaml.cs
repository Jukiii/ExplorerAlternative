using System.Windows;
using System.Windows.Input;
using ExplorerAlternative.ViewModels;

namespace ExplorerAlternative.Views;

public partial class CommandPaletteDialog : Window
{
    public CommandPaletteDialog(CommandPaletteViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.RequestClose += Close;
        Loaded += (_, _) => QueryTextBox.Focus();
    }

    // 検索欄にフォーカスがある状態でも、↑/↓で候補を選び、Enterで実行、Escで閉じられるようにする。
    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not CommandPaletteViewModel viewModel)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Escape:
                Close();
                e.Handled = true;
                break;

            case Key.Down:
                MoveSelection(viewModel, 1);
                e.Handled = true;
                break;

            case Key.Up:
                MoveSelection(viewModel, -1);
                e.Handled = true;
                break;

            case Key.Enter:
                viewModel.ExecuteSelectedCommand.Execute(null);
                e.Handled = true;
                break;
        }
    }

    private void MoveSelection(CommandPaletteViewModel viewModel, int offset)
    {
        var items = viewModel.FilteredCommands;
        if (items.Count == 0)
        {
            return;
        }

        var index = viewModel.SelectedCommand is null ? -1 : items.IndexOf(viewModel.SelectedCommand);
        index = Math.Clamp(index + offset, 0, items.Count - 1);
        viewModel.SelectedCommand = items[index];
        ResultsListBox.ScrollIntoView(viewModel.SelectedCommand);
    }

    private void ResultsListBox_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is CommandPaletteViewModel viewModel && viewModel.SelectedCommand is not null)
        {
            viewModel.ExecuteEntry(viewModel.SelectedCommand);
        }
    }
}
