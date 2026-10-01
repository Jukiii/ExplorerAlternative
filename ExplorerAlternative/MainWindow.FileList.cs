using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ExplorerAlternative.Models;
using ExplorerAlternative.ViewModels;
using ExplorerAlternative.Views;

namespace ExplorerAlternative;

/// <summary>
/// MainWindowのうち、ファイル一覧（詳細・階層）の選択・キー操作・列ヘッダー（仕様書4〜6章）。
/// </summary>
public partial class MainWindow
{
    private void TreeListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ListBox listBox || listBox.DataContext is not PaneViewModel pane)
        {
            return;
        }

        pane.UpdateSelection(listBox.SelectedItems.Cast<FileSystemNodeViewModel>());
    }

    // 詳細表示の列ヘッダークリック：DisplayMemberBindingのパスから並び替え対象の列を判定する
    // （「名前」列だけはアイコン付きCellTemplateでDisplayMemberBindingを持たないためnull判定）。
    private void GridViewColumnHeader_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not GridViewColumnHeader { Column: { } column, DataContext: PaneViewModel pane })
        {
            return;
        }

        var sortKey = column.DisplayMemberBinding switch
        {
            null => "Name",
            Binding { Path.Path: "SizeDisplay" } => "Size",
            Binding { Path.Path: "LastModifiedDisplay" } => "LastModified",
            Binding { Path.Path: "KindDisplay" } => "Kind",
            _ => null,
        };

        if (sortKey is not null && pane.SortByColumnCommand.CanExecute(sortKey))
        {
            pane.SortByColumnCommand.Execute(sortKey);
        }
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

    // 仕様書7章：Enter(開く)/F2(名前変更)/Delete(削除) は階層表示・詳細表示の両方で共通。
    private void NodeListBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not PaneViewModel pane)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Enter when pane.OpenCommand.CanExecute(null):
                pane.OpenCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.F2 when pane.RenameCommand.CanExecute(null):
                pane.RenameCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.Delete when pane.DeleteCommand.CanExecute(null):
                pane.DeleteCommand.Execute(null);
                e.Handled = true;
                break;
        }
    }

    // 仕様書7章：階層表示のみ →(展開)/←(折りたたみ) に対応。
    // ←は、選択中がすでに折りたたみ済み/ファイルの場合は親フォルダを選択し、
    // 展開中のフォルダを選択している場合はそのフォルダを折りたたむ（Windows標準ツリーの挙動）。
    private void TreeListBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        NodeListBox_PreviewKeyDown(sender, e);

        if (e.Handled || sender is not ListBox listBox || listBox.DataContext is not PaneViewModel pane)
        {
            return;
        }

        var node = pane.PrimarySelectedNode;
        if (node is null)
        {
            return;
        }

        if (e.Key == Key.Right && node.IsDirectory && !node.IsExpanded)
        {
            node.IsExpanded = true;
            e.Handled = true;
        }
        else if (e.Key == Key.Left)
        {
            if (node.IsDirectory && node.IsExpanded)
            {
                node.IsExpanded = false;
                e.Handled = true;
            }
            else
            {
                var parent = FindParentNode(pane.VisibleNodes, node);
                if (parent is not null)
                {
                    SelectTreeNode(listBox, parent);
                    e.Handled = true;
                }
            }
        }
    }

    private static FileSystemNodeViewModel? FindParentNode(IList<FileSystemNodeViewModel> visibleNodes, FileSystemNodeViewModel node)
    {
        var index = visibleNodes.IndexOf(node);
        if (index < 0)
        {
            return null;
        }

        for (var i = index - 1; i >= 0; i--)
        {
            if (visibleNodes[i].Depth == node.Depth - 1)
            {
                return visibleNodes[i];
            }
        }

        return null;
    }

    private static void SelectTreeNode(ListBox listBox, FileSystemNodeViewModel node)
    {
        listBox.SelectedItem = node;
        listBox.ScrollIntoView(node);

        listBox.Dispatcher.BeginInvoke(new Action(() =>
        {
            if (listBox.ItemContainerGenerator.ContainerFromItem(node) is ListBoxItem container)
            {
                container.Focus();
            }
        }), DispatcherPriority.ContextIdle);
    }
}
