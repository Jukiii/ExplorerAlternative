using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ExplorerAlternative.ViewModels;

namespace ExplorerAlternative;

public partial class MainWindow : Window
{
    private Point? _tabDragStartPoint;
    private bool _tabDragDuplicated;

    public MainWindow()
    {
        InitializeComponent();
        PreviewKeyDown += MainWindow_PreviewKeyDown;
    }

    private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // ListBox(Extended選択モード)はSpaceキーを選択切り替えとして内部消費し、
        // Window.InputBindingsのKeyBindingまでバブリングしないため、
        // トンネリング段階(PreviewKeyDown)でプレビューコマンドを直接実行する。
        if (e.Key != Key.Space || DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        if (e.OriginalSource is TextBox)
        {
            return;
        }

        e.Handled = true;

        if (viewModel.TogglePreviewCommand.CanExecute(null))
        {
            viewModel.TogglePreviewCommand.Execute(null);
        }
    }

    private void TreeListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ListBox listBox || listBox.DataContext is not PaneViewModel pane)
        {
            return;
        }

        pane.UpdateSelection(listBox.SelectedItems.Cast<FileSystemNodeViewModel>());
    }

    private void NodeListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not PaneViewModel pane)
        {
            return;
        }

        if (pane.OpenCommand.CanExecute(null))
        {
            pane.OpenCommand.Execute(null);
        }
    }

    private void TerminalOutputTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        (sender as TextBox)?.ScrollToEnd();
    }

    private void PaneContainer_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not PaneViewModel pane)
        {
            return;
        }

        if (DataContext is MainWindowViewModel viewModel && viewModel.SetActivePaneCommand.CanExecute(pane))
        {
            viewModel.SetActivePaneCommand.Execute(pane);
        }
    }

    // 仕様書18.1章：タブをCtrlキーを押しながらドラッグすると複製する。
    // ドラッグ中のフローティング表示までは行わず、ドラッグ検知の瞬間に複製する簡易実装。
    private void TabItem_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _tabDragStartPoint = e.GetPosition(null);
        _tabDragDuplicated = false;
    }

    private void TabItem_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_tabDragDuplicated || _tabDragStartPoint is null || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        if (Keyboard.Modifiers != ModifierKeys.Control)
        {
            return;
        }

        var current = e.GetPosition(null);
        var diff = _tabDragStartPoint.Value - current;

        if (Math.Abs(diff.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(diff.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        if (sender is not FrameworkElement element || element.DataContext is not TabViewModel tab)
        {
            return;
        }

        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        _tabDragDuplicated = true;
        viewModel.DuplicateTabCommand.Execute(tab);
    }

    private void TabItem_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _tabDragStartPoint = null;
        _tabDragDuplicated = false;
    }
}
